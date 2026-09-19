package dev.touchcontrol.companion

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.content.res.Configuration
import android.graphics.PixelFormat
import android.graphics.Point
import android.os.Build
import android.os.IBinder
import android.provider.Settings
import android.util.Log
import android.view.Gravity
import android.view.View
import android.view.WindowManager
import dev.touchcontrol.companion.views.CameraPadView
import dev.touchcontrol.companion.views.ControlView
import dev.touchcontrol.companion.views.JoystickView
import dev.touchcontrol.companion.views.MenuBarView
import dev.touchcontrol.companion.views.OverlayHost
import dev.touchcontrol.companion.views.PillView
import dev.touchcontrol.companion.views.RoundButtonView
import dev.touchcontrol.companion.views.SkillWheelView

/**
 * Foreground service that owns the overlay. Every control gets its own small window (TYPE_APPLICATION_OVERLAY
 * with FLAG_SPLIT_TOUCH), so fingers landing on different controls are dispatched independently and touches
 * outside any control fall straight through to the game underneath.
 */
class OverlayService : Service(), OverlayHost {

    companion object {
        const val ACTION_START = "dev.touchcontrol.companion.START"
        const val ACTION_STOP = "dev.touchcontrol.companion.STOP"
        const val ACTION_TOGGLE_EDIT = "dev.touchcontrol.companion.TOGGLE_EDIT"
        const val ACTION_TOGGLE_HIDE = "dev.touchcontrol.companion.TOGGLE_HIDE"
        const val ACTION_RELOAD = "dev.touchcontrol.companion.RELOAD"

        private const val CHANNEL = "overlay"
        private const val NOTIFICATION_ID = 1
        private const val TAG = "OverlayService"

        @Volatile var running = false
            private set

        fun start(ctx: Context, action: String = ACTION_START) {
            val i = Intent(ctx, OverlayService::class.java).setAction(action)
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) ctx.startForegroundService(i) else ctx.startService(i)
        }
    }

    private class Entry(
        val view: View,
        val params: WindowManager.LayoutParams,
        val centerOffsetX: Float,
        val centerOffsetY: Float,
        val placement: Placement?,
        val camera: CameraPadConfig?,
    )

    private lateinit var wm: WindowManager
    private lateinit var layout: Layout
    private val windows = ArrayList<Entry>()
    private var pill: Entry? = null
    private var editing = false
    private var hidden = false
    private var lastConnected = false
    private val bridgeListener: () -> Unit = {
        if (lastConnected != Bridge.connected) {
            lastConnected = Bridge.connected
            updateNotification()
        }
    }

    override val density: Float get() = resources.displayMetrics.density
    override val opacity: Float get() = layout.opacity
    override val globalScale: Float get() = layout.globalScale

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        wm = getSystemService(WINDOW_SERVICE) as WindowManager
        layout = Prefs.layout(this)
        createChannel()
        startInForeground()
        running = true
        Bridge.listeners.add(bridgeListener)
        Bridge.start(Prefs.host(this), Prefs.port(this))
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (!Settings.canDrawOverlays(this)) {
            Log.w(TAG, "overlay permission missing")
            stopSelf()
            return START_NOT_STICKY
        }

        when (intent?.action) {
            ACTION_STOP -> {
                stopSelf()
                return START_NOT_STICKY
            }
            ACTION_TOGGLE_EDIT -> editing = !editing
            ACTION_TOGGLE_HIDE -> {
                hidden = !hidden
                editing = false
            }
            ACTION_RELOAD -> {
                layout = Prefs.layout(this)
                Bridge.start(Prefs.host(this), Prefs.port(this))
            }
        }

        rebuild()
        return START_STICKY
    }

    override fun onDestroy() {
        Bridge.listeners.remove(bridgeListener)
        removeAll()
        Bridge.move(0f, 0f)
        Bridge.stop()
        running = false
        super.onDestroy()
    }

    override fun onConfigurationChanged(newConfig: Configuration) {
        super.onConfigurationChanged(newConfig)
        rebuild()
    }

    // ---- windows ----------------------------------------------------------------------------------------

    private fun screenSize(): Point {
        val p = Point()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
            val b = wm.maximumWindowMetrics.bounds
            p.set(b.width(), b.height())
        } else {
            @Suppress("DEPRECATION")
            wm.defaultDisplay.getRealSize(p)
        }
        return p
    }

    private fun baseParams(w: Int, h: Int): WindowManager.LayoutParams {
        val p = WindowManager.LayoutParams(
            w, h,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL or
                WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN or
                WindowManager.LayoutParams.FLAG_LAYOUT_NO_LIMITS or
                WindowManager.LayoutParams.FLAG_SPLIT_TOUCH,
            PixelFormat.TRANSLUCENT,
        )
        p.gravity = Gravity.TOP or Gravity.START
        return p
    }

    private fun rebuild() {
        removeAll()
        val screen = screenSize()
        val sw = screen.x.toFloat()
        val sh = screen.y.toFloat()

        if (hidden) {
            pill = addPill("Show", 0.5f, 0.03f) { hidden = false; rebuild() }
            updateNotification()
            return
        }

        if (layout.joystick.enabled) {
            val v = JoystickView(this, this, layout.joystick)
            addControl(v, layout.joystick.placement, sw, sh)
        }

        for (w in layout.wheels) {
            if (!w.enabled) continue
            val v = SkillWheelView(this, this, w)
            val (cox, coy) = v.centerOffset()
            addControl(v, w.placement, sw, sh, cox, coy)
        }

        for (b in layout.buttons) {
            if (!b.enabled) continue
            addControl(RoundButtonView(this, this, b), b.placement, sw, sh)
        }

        if (layout.menuBar.enabled) addControl(MenuBarView(this, this, layout.menuBar), layout.menuBar.placement, sw, sh)

        if (layout.cameraPad.enabled) {
            val cam = layout.cameraPad
            val v = CameraPadView(this, this, cam, screen.x, screen.y)
            val (w, h) = v.desiredSize()
            val p = baseParams(w, h)
            p.x = (cam.x * sw).toInt()
            p.y = (cam.y * sh).toInt()
            v.editing = editing
            wm.addView(v, p)
            windows.add(Entry(v, p, 0f, 0f, null, cam))
        }

        if (editing) pill = addPill("Done", 0.5f, 0.12f) { editing = false; rebuild() }

        subscribe()
        updateNotification()
    }

    private fun addControl(view: ControlView, placement: Placement, sw: Float, sh: Float, cox: Float = -1f, coy: Float = -1f) {
        val (w, h) = view.desiredSize()
        val offX = if (cox >= 0f) cox else w / 2f
        val offY = if (coy >= 0f) coy else h / 2f
        val p = baseParams(w, h)
        p.x = (placement.cx * sw - offX).toInt()
        p.y = (placement.cy * sh - offY).toInt()
        view.editing = editing
        try {
            wm.addView(view, p)
            windows.add(Entry(view, p, offX, offY, placement, null))
        } catch (e: Exception) {
            Log.e(TAG, "addView failed: ${e.message}")
        }
    }

    private fun addPill(text: String, cx: Float, cy: Float, onTap: () -> Unit): Entry? {
        val screen = screenSize()
        val v = PillView(this, density, text, onTap)
        val (w, h) = v.desiredSize()
        val p = baseParams(w, h)
        p.x = (cx * screen.x - w / 2f).toInt()
        p.y = (cy * screen.y - h / 2f).toInt()
        return try {
            wm.addView(v, p)
            Entry(v, p, w / 2f, h / 2f, null, null)
        } catch (e: Exception) {
            Log.e(TAG, "addView failed: ${e.message}")
            null
        }
    }

    private fun removeAll() {
        for (e in windows) {
            (e.view as? ControlView)?.release()
            try { wm.removeView(e.view) } catch (_: Exception) {}
        }
        windows.clear()
        pill?.let { try { wm.removeView(it.view) } catch (_: Exception) {} }
        pill = null
    }

    private fun subscribe() {
        val bars = layout.wheels.filter { it.enabled }.map { it.hotbar }.distinct()
        val ga = layout.buttons.filter { it.enabled && it.kind == ButtonKind.GENERAL_ACTION }.map { it.value }.distinct()
        Bridge.subscribe(bars, ga)
    }

    // ---- OverlayHost ------------------------------------------------------------------------------------

    override fun moveWindow(view: View, dx: Float, dy: Float) {
        val e = windows.firstOrNull { it.view === view } ?: return
        e.params.x += dx.toInt()
        e.params.y += dy.toInt()
        try { wm.updateViewLayout(view, e.params) } catch (_: Exception) {}
    }

    override fun dragEnded(view: View) {
        val e = windows.firstOrNull { it.view === view } ?: return
        val screen = screenSize()
        if (e.placement != null) {
            e.placement.cx = ((e.params.x + e.centerOffsetX) / screen.x).coerceIn(0f, 1f)
            e.placement.cy = ((e.params.y + e.centerOffsetY) / screen.y).coerceIn(0f, 1f)
        } else if (e.camera != null) {
            e.camera.x = (e.params.x.toFloat() / screen.x).coerceIn(0f, 1f)
            e.camera.y = (e.params.y.toFloat() / screen.y).coerceIn(0f, 1f)
        }
        Prefs.saveLayout(this, layout)
    }

    override fun toggleEditMode() {
        editing = !editing
        rebuild()
    }

    override fun hideOverlay() {
        hidden = true
        editing = false
        rebuild()
    }

    // ---- notification -----------------------------------------------------------------------------------

    private fun createChannel() {
        val nm = getSystemService(NotificationManager::class.java)
        if (nm.getNotificationChannel(CHANNEL) == null) {
            nm.createNotificationChannel(NotificationChannel(CHANNEL, getString(R.string.notification_channel), NotificationManager.IMPORTANCE_LOW))
        }
    }

    private fun serviceIntent(action: String, code: Int): PendingIntent =
        PendingIntent.getService(
            this, code, Intent(this, OverlayService::class.java).setAction(action),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )

    private fun buildNotification(): Notification {
        val open = PendingIntent.getActivity(
            this, 0, Intent(this, MainActivity::class.java),
            PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT,
        )
        val status = when {
            hidden -> "Overlay hidden"
            Bridge.connected -> "Connected to the game"
            else -> "Waiting for the game plugin on ${Bridge.host}:${Bridge.port}"
        }
        return Notification.Builder(this, CHANNEL)
            .setSmallIcon(android.R.drawable.ic_menu_compass)
            .setContentTitle("Touch Control")
            .setContentText(status)
            .setContentIntent(open)
            .setOngoing(true)
            .addAction(Notification.Action.Builder(null, if (editing) "Done editing" else "Edit layout", serviceIntent(ACTION_TOGGLE_EDIT, 1)).build())
            .addAction(Notification.Action.Builder(null, if (hidden) "Show" else "Hide", serviceIntent(ACTION_TOGGLE_HIDE, 2)).build())
            .addAction(Notification.Action.Builder(null, "Stop", serviceIntent(ACTION_STOP, 3)).build())
            .build()
    }

    private fun startInForeground() {
        val n = buildNotification()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            startForeground(NOTIFICATION_ID, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE)
        } else {
            startForeground(NOTIFICATION_ID, n)
        }
    }

    private fun updateNotification() {
        getSystemService(NotificationManager::class.java).notify(NOTIFICATION_ID, buildNotification())
    }
}
