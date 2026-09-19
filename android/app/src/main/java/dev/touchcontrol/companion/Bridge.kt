package dev.touchcontrol.companion

import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Handler
import android.os.Looper
import android.util.Base64
import android.util.Log
import android.util.LruCache
import org.json.JSONArray
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.OutputStreamWriter
import java.net.InetSocketAddress
import java.net.Socket
import java.util.concurrent.CopyOnWriteArrayList
import java.util.concurrent.LinkedBlockingQueue
import java.util.concurrent.atomic.AtomicBoolean

/** One hotbar slot as reported by the plugin. */
data class SlotState(val icon: Int, val empty: Boolean, val cd: Float, val total: Float, val key: String)

data class CatalogEntry(val id: Int, val name: String, val icon: Int)

/** Latest snapshot from the plugin; replaced wholesale ten times a second. */
class GameState(
    val loggedIn: Boolean,
    val mounted: Boolean,
    val combat: Boolean,
    val bars: Map<Int, List<SlotState>>,
    val generalActions: Map<Int, SlotState>,
)

/**
 * TCP line-JSON client for the plugin's bridge. Reconnects forever while started; every callback is delivered
 * on the main thread so views can invalidate directly.
 */
object Bridge {
    private const val TAG = "Bridge"
    private const val PROTOCOL = 1

    @Volatile var host: String = "127.0.0.1"
    @Volatile var port: Int = 47800

    private val running = AtomicBoolean(false)
    private val outgoing = LinkedBlockingQueue<String>()
    private var thread: Thread? = null
    private val main = Handler(Looper.getMainLooper())

    @Volatile var connected: Boolean = false
        private set
    @Volatile var state: GameState? = null
        private set
    @Volatile var generalActionCatalog: List<CatalogEntry> = emptyList()
        private set
    @Volatile var mainCommandCatalog: List<CatalogEntry> = emptyList()
        private set

    @Volatile private var subscribedBars: List<Int> = listOf(0, 1)
    @Volatile private var subscribedGa: List<Int> = emptyList()

    /** Called on the main thread after every state message, connection change or icon arrival. */
    val listeners = CopyOnWriteArrayList<() -> Unit>()

    private val icons = LruCache<Int, Bitmap>(256)
    private val pendingIcons = HashSet<Int>()

    fun start(host: String, port: Int) {
        this.host = host
        this.port = port
        if (running.getAndSet(true)) {
            // Already running: drop the socket so the loop reconnects to the new address.
            restartRequested = true
            return
        }
        thread = Thread({ loop() }, "bridge").apply { isDaemon = true; start() }
    }

    fun stop() {
        running.set(false)
        restartRequested = true
        thread = null
    }

    @Volatile private var restartRequested = false

    // ---- outgoing helpers ----------------------------------------------------------------------------

    private fun send(obj: JSONObject) {
        if (outgoing.size < 200) outgoing.offer(obj.toString())
    }

    fun move(x: Float, y: Float) = send(JSONObject().put("t", "move").put("x", x.toDouble()).put("y", y.toDouble()))
    fun camera(dx: Float, dy: Float) = send(JSONObject().put("t", "cam").put("dx", dx.toDouble()).put("dy", dy.toDouble()))
    fun slot(hotbar: Int, slot: Int) = send(JSONObject().put("t", "slot").put("h", hotbar).put("s", slot))
    fun key(vk: Int, down: Boolean) = send(JSONObject().put("t", "key").put("vk", vk).put("down", down))
    fun generalAction(id: Int) = send(JSONObject().put("t", "ga").put("id", id))
    fun mainCommand(id: Int) = send(JSONObject().put("t", "mc").put("id", id))
    fun mountToggle() = send(JSONObject().put("t", "mount"))

    fun subscribe(bars: List<Int>, generalActions: List<Int>) {
        subscribedBars = bars
        subscribedGa = generalActions
        sendSubscription()
    }

    private fun sendSubscription() {
        send(
            JSONObject()
                .put("t", "sub")
                .put("bars", JSONArray(subscribedBars))
                .put("ga", JSONArray(subscribedGa))
        )
    }

    /** Cached icon bitmap, or null while it is being fetched (a listener fires once it arrives). */
    fun icon(id: Int): Bitmap? {
        if (id <= 0) return null
        icons.get(id)?.let { return it }
        synchronized(pendingIcons) {
            if (pendingIcons.add(id)) send(JSONObject().put("t", "icon").put("id", id))
        }
        return null
    }

    // ---- socket loop ---------------------------------------------------------------------------------

    private fun loop() {
        while (running.get()) {
            restartRequested = false
            var socket: Socket? = null
            try {
                socket = Socket().apply {
                    tcpNoDelay = true
                    connect(InetSocketAddress(host, port), 2000)
                }
                Log.i(TAG, "connected to $host:$port")
                setConnected(true)

                outgoing.clear()
                send(JSONObject().put("t", "hello").put("name", "android").put("ver", PROTOCOL))
                sendSubscription()
                synchronized(pendingIcons) { pendingIcons.clear() }

                val writer = OutputStreamWriter(socket.getOutputStream(), Charsets.UTF_8)
                val writerThread = Thread({
                    try {
                        while (!restartRequested && running.get()) {
                            val line = outgoing.poll(250, java.util.concurrent.TimeUnit.MILLISECONDS) ?: continue
                            writer.write(line)
                            writer.write("\n")
                            writer.flush()
                        }
                    } catch (_: Exception) {
                    }
                }, "bridge-writer").apply { isDaemon = true; start() }

                val reader = BufferedReader(InputStreamReader(socket.getInputStream(), Charsets.UTF_8))
                while (running.get() && !restartRequested) {
                    val line = reader.readLine() ?: break
                    if (line.isEmpty()) continue
                    try {
                        handle(JSONObject(line))
                    } catch (e: Exception) {
                        Log.w(TAG, "bad message: ${e.message}")
                    }
                }
                writerThread.interrupt()
            } catch (e: Exception) {
                Log.d(TAG, "connection failed: ${e.message}")
            } finally {
                try { socket?.close() } catch (_: Exception) {}
                setConnected(false)
            }

            if (running.get()) Thread.sleep(1000)
        }
    }

    private fun setConnected(value: Boolean) {
        if (connected == value) return
        connected = value
        if (!value) state = null
        notifyListeners()
    }

    private fun handle(obj: JSONObject) {
        when (obj.optString("t")) {
            "hello" -> Log.i(TAG, "plugin ${obj.optString("plugin")} protocol ${obj.optInt("ver")}")

            "catalog" -> {
                generalActionCatalog = parseCatalog(obj.optJSONArray("ga"))
                mainCommandCatalog = parseCatalog(obj.optJSONArray("mc"))
                notifyListeners()
            }

            "state" -> {
                val bars = HashMap<Int, List<SlotState>>()
                val arr = obj.optJSONArray("bars")
                if (arr != null) {
                    for (i in 0 until arr.length()) {
                        val bar = arr.getJSONObject(i)
                        bars[bar.optInt("h")] = parseSlots(bar.optJSONArray("slots"))
                    }
                }
                val ga = HashMap<Int, SlotState>()
                val gaIds = obj.optJSONArray("gaIds")
                val gaStates = obj.optJSONArray("ga")
                if (gaIds != null && gaStates != null) {
                    val parsed = parseSlots(gaStates)
                    for (i in 0 until minOf(gaIds.length(), parsed.size)) ga[gaIds.getInt(i)] = parsed[i]
                }
                state = GameState(
                    loggedIn = obj.optBoolean("in"),
                    mounted = obj.optBoolean("mounted"),
                    combat = obj.optBoolean("combat"),
                    bars = bars,
                    generalActions = ga,
                )
                notifyListeners()
            }

            "icon" -> {
                val id = obj.optInt("id")
                val png = obj.optString("png", "")
                synchronized(pendingIcons) { pendingIcons.remove(id) }
                if (png.isNotEmpty()) {
                    try {
                        val bytes = Base64.decode(png, Base64.DEFAULT)
                        val bmp = BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
                        if (bmp != null) {
                            icons.put(id, bmp)
                            notifyListeners()
                        }
                    } catch (e: Exception) {
                        Log.w(TAG, "icon $id decode failed: ${e.message}")
                    }
                }
            }
        }
    }

    private fun parseSlots(arr: JSONArray?): List<SlotState> {
        if (arr == null) return emptyList()
        val list = ArrayList<SlotState>(arr.length())
        for (i in 0 until arr.length()) {
            val s = arr.getJSONObject(i)
            list.add(
                SlotState(
                    icon = s.optInt("i"),
                    empty = s.optBoolean("e", true),
                    cd = s.optDouble("cd", 0.0).toFloat(),
                    total = s.optDouble("tot", 0.0).toFloat(),
                    key = s.optString("k", ""),
                )
            )
        }
        return list
    }

    private fun parseCatalog(arr: JSONArray?): List<CatalogEntry> {
        if (arr == null) return emptyList()
        val list = ArrayList<CatalogEntry>(arr.length())
        for (i in 0 until arr.length()) {
            val e = arr.getJSONObject(i)
            list.add(CatalogEntry(e.optInt("id"), e.optString("n"), e.optInt("i")))
        }
        return list
    }

    private fun notifyListeners() {
        main.post { for (l in listeners) l() }
    }
}
