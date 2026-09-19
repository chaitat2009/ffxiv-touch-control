package dev.touchcontrol.companion.views

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.os.SystemClock
import android.view.MotionEvent
import dev.touchcontrol.companion.Bridge
import dev.touchcontrol.companion.JoystickConfig
import kotlin.math.cos
import kotlin.math.sin
import kotlin.math.sqrt

/**
 * Virtual left stick. Sends the normalized stick vector (x right, y down, |v| <= 1) to the plugin at ~30 Hz
 * while a finger is down and a zero vector when it lifts. The plugin turns it into held movement keys.
 */
class JoystickView(context: Context, host: OverlayHost, private val cfg: JoystickConfig) : ControlView(context, host) {

    override val title = "Joystick"

    private var pointerId = -1
    private var originX = 0f
    private var originY = 0f
    private var knobX = 0f
    private var knobY = 0f
    private var lastSent = 0L
    private var lastX = 0f
    private var lastY = 0f

    private val basePaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(140, 20, 20, 26) }
    private val ringPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(140, 255, 255, 255); style = Paint.Style.STROKE; strokeWidth = 3f }
    private val tickPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(200, 255, 255, 255) }
    private val knobPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(220, 240, 240, 255) }
    private val knobEdge = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(90, 0, 0, 0); style = Paint.Style.STROKE; strokeWidth = 3f }
    private val path = Path()

    private val zone get() = cfg.zoneRadiusDp * dp * cfg.placement.scale
    private val baseR get() = cfg.baseRadiusDp * dp * cfg.placement.scale
    private val knobR get() = cfg.knobRadiusDp * dp * cfg.placement.scale

    override fun desiredSize(): Pair<Int, Int> {
        val s = (zone * 2f).toInt()
        return s to s
    }

    override fun handleTouch(ev: MotionEvent): Boolean {
        when (ev.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                if (pointerId != -1) return true
                val idx = ev.actionIndex
                pointerId = ev.getPointerId(idx)
                val x = ev.getX(idx)
                val y = ev.getY(idx)
                if (cfg.floating) {
                    val margin = baseR + knobR
                    originX = x.coerceIn(margin, width - margin)
                    originY = y.coerceIn(margin, height - margin)
                } else {
                    originX = width / 2f
                    originY = height / 2f
                }
                update(x, y, force = true)
            }

            MotionEvent.ACTION_MOVE -> {
                val idx = ev.findPointerIndex(pointerId)
                if (idx >= 0) update(ev.getX(idx), ev.getY(idx), force = false)
            }

            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> release()

            MotionEvent.ACTION_POINTER_UP -> {
                if (ev.getPointerId(ev.actionIndex) == pointerId) release()
            }
        }
        return true
    }

    private fun update(x: Float, y: Float, force: Boolean) {
        var dx = x - originX
        var dy = y - originY
        val len = sqrt(dx * dx + dy * dy)
        val r = baseR
        if (len > r) {
            dx *= r / len
            dy *= r / len
        }
        knobX = originX + dx
        knobY = originY + dy

        val sx = dx / r
        val sy = dy / r
        val now = SystemClock.uptimeMillis()
        val changed = kotlin.math.abs(sx - lastX) > 0.02f || kotlin.math.abs(sy - lastY) > 0.02f
        if (force || (changed && now - lastSent >= 33) || now - lastSent >= 200) {
            Bridge.move(sx, sy)
            lastSent = now
            lastX = sx
            lastY = sy
        }
        invalidate()
    }

    override fun release() {
        if (pointerId == -1) return
        pointerId = -1
        Bridge.move(0f, 0f)
        lastX = 0f
        lastY = 0f
        invalidate()
    }

    override fun onDraw(canvas: Canvas) {
        val active = pointerId != -1
        val cx = if (active) originX else width / 2f
        val cy = if (active) originY else height / 2f
        val alpha = host.opacity

        basePaint.alpha = (140 * alpha).toInt()
        ringPaint.alpha = (140 * alpha).toInt()
        tickPaint.alpha = (200 * alpha).toInt()
        knobPaint.alpha = (220 * alpha).toInt()

        canvas.drawCircle(cx, cy, baseR, basePaint)
        ringPaint.strokeWidth = if (active) 4f else 2.5f
        canvas.drawCircle(cx, cy, baseR, ringPaint)
        ringPaint.alpha = (60 * alpha).toInt()
        canvas.drawCircle(cx, cy, baseR * cfg.deadZone, ringPaint)

        // Direction ticks so it reads as a d-pad at a glance.
        val inner = baseR * 0.72f
        val arrow = baseR * 0.09f
        for (i in 0 until 4) {
            val a = i * Math.PI.toFloat() / 2f
            val dxs = sin(a)
            val dys = -cos(a)
            val sx = dys
            val sy = -dxs
            path.reset()
            path.moveTo(cx + dxs * (inner + arrow), cy + dys * (inner + arrow))
            path.lineTo(cx + dxs * inner + sx * arrow, cy + dys * inner + sy * arrow)
            path.lineTo(cx + dxs * inner - sx * arrow, cy + dys * inner - sy * arrow)
            path.close()
            canvas.drawPath(path, tickPaint)
        }

        val kx = if (active) knobX else cx
        val ky = if (active) knobY else cy
        canvas.drawCircle(kx, ky, knobR, knobPaint)
        canvas.drawCircle(kx, ky, knobR, knobEdge)

        drawEditFrame(canvas)
    }
}
