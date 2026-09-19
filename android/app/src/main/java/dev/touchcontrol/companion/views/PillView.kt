package dev.touchcontrol.companion.views

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.RectF
import android.view.MotionEvent
import android.view.View

/** A small rounded text button used for "Done" (edit mode) and "Show" (while the overlay is hidden). */
class PillView(context: Context, private val density: Float, private val text: String, private val onTap: () -> Unit) : View(context) {

    private val bg = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(215, 30, 120, 220) }
    private val fg = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE; textSize = 14f * density; typeface = android.graphics.Typeface.DEFAULT_BOLD }
    private var down = false

    fun desiredSize(): Pair<Int, Int> {
        val w = (fg.measureText(text) + 28f * density).toInt()
        val h = (32f * density).toInt()
        return w to h
    }

    override fun onTouchEvent(ev: MotionEvent): Boolean {
        when (ev.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                down = true
                invalidate()
            }
            MotionEvent.ACTION_UP -> {
                down = false
                invalidate()
                onTap()
            }
            MotionEvent.ACTION_CANCEL -> {
                down = false
                invalidate()
            }
        }
        return true
    }

    override fun onDraw(canvas: Canvas) {
        bg.alpha = if (down) 255 else 215
        canvas.drawRoundRect(RectF(0f, 0f, width.toFloat(), height.toFloat()), height / 2f, height / 2f, bg)
        val w = fg.measureText(text)
        canvas.drawText(text, width / 2f - w / 2f, height / 2f + fg.textSize * 0.35f, fg)
    }
}
