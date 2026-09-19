package dev.touchcontrol.companion.views

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.view.MotionEvent
import dev.touchcontrol.companion.Bridge
import dev.touchcontrol.companion.CameraPadConfig

/**
 * Optional drag zone that rotates the camera through the plugin. Off by default: the emulator normally turns a
 * finger drag on the game view into a mouse drag, which FFXIV already uses to orbit the camera. Turn it on if
 * your emulator's touch mode does not drag.
 */
class CameraPadView(context: Context, host: OverlayHost, private val cfg: CameraPadConfig, private val screenW: Int, private val screenH: Int) :
    ControlView(context, host) {

    override val title = "Camera pad"

    private var pointerId = -1
    private var lastX = 0f
    private var lastY = 0f

    private val outline = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(40, 255, 255, 255); style = Paint.Style.STROKE; strokeWidth = 2f }

    override fun desiredSize(): Pair<Int, Int> = (cfg.w * screenW).toInt() to (cfg.h * screenH).toInt()

    override fun handleTouch(ev: MotionEvent): Boolean {
        when (ev.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                if (pointerId != -1) return true
                val idx = ev.actionIndex
                pointerId = ev.getPointerId(idx)
                lastX = ev.getX(idx)
                lastY = ev.getY(idx)
            }
            MotionEvent.ACTION_MOVE -> {
                val idx = ev.findPointerIndex(pointerId)
                if (idx >= 0) {
                    val x = ev.getX(idx)
                    val y = ev.getY(idx)
                    // Send density-independent deltas so the plugin's sensitivity means the same on every phone.
                    val dx = (x - lastX) / host.density * cfg.gain
                    val dy = (y - lastY) / host.density * cfg.gain
                    if (dx != 0f || dy != 0f) Bridge.camera(dx, dy)
                    lastX = x
                    lastY = y
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> pointerId = -1
            MotionEvent.ACTION_POINTER_UP -> if (ev.getPointerId(ev.actionIndex) == pointerId) pointerId = -1
        }
        return true
    }

    override fun release() {
        pointerId = -1
    }

    override fun onDraw(canvas: Canvas) {
        outline.alpha = (40 * host.opacity).toInt()
        canvas.drawRoundRect(RectF(1f, 1f, width - 1f, height - 1f), 12f, 12f, outline)
        drawEditFrame(canvas)
    }
}
