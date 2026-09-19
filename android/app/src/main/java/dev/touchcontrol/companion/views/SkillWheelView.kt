package dev.touchcontrol.companion.views

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.view.MotionEvent
import dev.touchcontrol.companion.Bridge
import dev.touchcontrol.companion.WheelConfig
import kotlin.math.PI
import kotlin.math.abs
import kotlin.math.ceil
import kotlin.math.cos
import kotlin.math.max
import kotlin.math.min
import kotlin.math.sin

/**
 * A run of hotbar slots as round icon buttons: a big "main attack" in the middle with the rest fanned out on
 * rings (gacha style), or a plain row. Icons and cooldowns come from the plugin; a tap executes the slot.
 * Every finger is tracked separately, so mashing two skills at once is fine.
 */
class SkillWheelView(context: Context, host: OverlayHost, private val cfg: WheelConfig) : ControlView(context, host) {

    override val title get() = "Hotbar ${cfg.hotbar + 1}"

    private class Button(val ox: Float, val oy: Float, val r: Float)

    private val buttons = ArrayList<Button>()
    private var originX = 0f   // window-space position of the group centre
    private var originY = 0f
    private val pressed = HashMap<Int, Int>()   // pointerId -> button index

    private val padding get() = 6f * dp
    private val listener: () -> Unit = { invalidate() }

    // Declared before init: Kotlin runs property initializers in order, and computeLayout() writes these.
    private var sizeW = 0
    private var sizeH = 0

    init {
        computeLayout()
    }

    private fun computeLayout() {
        buttons.clear()
        val s = dp * cfg.placement.scale
        val count = cfg.count.coerceIn(1, 12)
        val buttonR = cfg.buttonRadiusDp * s

        if (cfg.row) {
            val step = buttonR * 2f + cfg.rowSpacingDp * s
            for (i in 0 until count) buttons.add(Button((i - (count - 1) / 2f) * step, 0f, buttonR))
        } else {
            var ringStart = 0
            if (cfg.centerFirst) {
                buttons.add(Button(0f, 0f, cfg.centerRadiusDp * s))
                ringStart = 1
            }
            val onRings = count - ringStart
            val capacity = max(1, cfg.ringCapacity)
            val startRad = cfg.arcStartDeg * PI.toFloat() / 180f
            val endRad = cfg.arcEndDeg * PI.toFloat() / 180f
            val fullCircle = abs(abs(endRad - startRad) - PI.toFloat() * 2f) < 0.01f
            for (i in 0 until onRings) {
                val ring = i / capacity
                val indexInRing = i % capacity
                val inThisRing = min(capacity, onRings - ring * capacity)
                val t = when {
                    inThisRing == 1 -> 0.5f
                    fullCircle -> indexInRing.toFloat() / inThisRing
                    else -> indexInRing.toFloat() / (inThisRing - 1)
                }
                val angle = startRad + (endRad - startRad) * t
                val radius = (cfg.ringRadiusDp + ring * cfg.ringSpacingDp) * s
                buttons.add(Button(sin(angle) * radius, -cos(angle) * radius, buttonR))
            }
        }

        var minX = Float.MAX_VALUE; var minY = Float.MAX_VALUE
        var maxX = -Float.MAX_VALUE; var maxY = -Float.MAX_VALUE
        for (b in buttons) {
            minX = min(minX, b.ox - b.r); minY = min(minY, b.oy - b.r)
            maxX = max(maxX, b.ox + b.r); maxY = max(maxY, b.oy + b.r)
        }
        minX -= padding; minY -= padding; maxX += padding; maxY += padding
        originX = -minX
        originY = -minY
        sizeW = ceil(maxX - minX).toInt()
        sizeH = ceil(maxY - minY).toInt()
    }

    override fun desiredSize(): Pair<Int, Int> = sizeW to sizeH

    /** Offset of the group centre from the window's top-left, so the service can position the window by centre. */
    fun centerOffset(): Pair<Float, Float> = originX to originY

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        Bridge.listeners.add(listener)
    }

    override fun onDetachedFromWindow() {
        Bridge.listeners.remove(listener)
        super.onDetachedFromWindow()
    }

    private fun slotState(i: Int) = Bridge.state?.bars?.get(cfg.hotbar)?.getOrNull(cfg.firstSlot + i)

    private fun hit(x: Float, y: Float): Int {
        // Later buttons are drawn on top, so test them first.
        for (i in buttons.indices.reversed()) {
            val b = buttons[i]
            val dx = x - (originX + b.ox)
            val dy = y - (originY + b.oy)
            if (dx * dx + dy * dy <= b.r * b.r) return i
        }
        return -1
    }

    override fun handleTouch(ev: MotionEvent): Boolean {
        when (ev.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                val idx = ev.actionIndex
                val i = hit(ev.getX(idx), ev.getY(idx))
                if (i >= 0) {
                    val st = slotState(i)
                    if (st != null && !st.empty) {
                        pressed[ev.getPointerId(idx)] = i
                        Bridge.slot(cfg.hotbar, cfg.firstSlot + i)
                        invalidate()
                    }
                }
            }

            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                pressed.clear()
                invalidate()
            }

            MotionEvent.ACTION_POINTER_UP -> {
                pressed.remove(ev.getPointerId(ev.actionIndex))
                invalidate()
            }
        }
        return true
    }

    override fun release() {
        pressed.clear()
    }

    override fun onDraw(canvas: Canvas) {
        val alpha = host.opacity
        val fill = Color.argb(170, 20, 20, 26)
        for (i in buttons.indices) {
            val b = buttons[i]
            val st = slotState(i)
            val empty = st == null || st.empty
            if (empty && cfg.hideEmpty && !editing) continue

            val icon = if (empty) null else Bridge.icon(st!!.icon)
            val label = if (empty) "${cfg.firstSlot + i + 1}" else null
            val held = pressed.containsValue(i)
            Painter.roundButton(
                canvas, originX + b.ox, originY + b.oy, b.r, fill, alpha, icon, label, held,
                st?.cd ?: 0f, st?.total ?: 0f, host.density,
            )
        }
        drawEditFrame(canvas)
    }
}
