package dev.touchcontrol.companion.views

import android.content.Context
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import android.graphics.Rect
import android.graphics.RectF
import android.view.MotionEvent
import android.view.View
import kotlin.math.ceil

/** What a control needs from the service that owns its window. */
interface OverlayHost {
    val density: Float
    val opacity: Float
    val globalScale: Float
    fun moveWindow(view: View, dx: Float, dy: Float)
    fun dragEnded(view: View)
    fun toggleEditMode()
    fun hideOverlay()
}

/**
 * Base for every on-screen control. Each control lives in its own overlay window, so it only ever sees the
 * pointers that landed on it; that is what makes several fingers on several controls work at once.
 * In edit mode touches drag the window instead of acting.
 */
abstract class ControlView(context: Context, protected val host: OverlayHost) : View(context) {

    var editing = false
        set(value) {
            field = value
            invalidate()
        }

    open val title: String = ""

    private var lastRawX = 0f
    private var lastRawY = 0f

    protected val dp: Float get() = host.density * host.globalScale

    /** Pixel size of the window this view wants. */
    abstract fun desiredSize(): Pair<Int, Int>

    protected abstract fun handleTouch(ev: MotionEvent): Boolean

    /** Called when the finger(s) are gone for any reason (window removed, hidden). Release held things here. */
    open fun release() {}

    override fun onTouchEvent(ev: MotionEvent): Boolean {
        if (!editing) return handleTouch(ev)

        when (ev.actionMasked) {
            MotionEvent.ACTION_DOWN -> {
                lastRawX = ev.rawX
                lastRawY = ev.rawY
            }
            MotionEvent.ACTION_MOVE -> {
                host.moveWindow(this, ev.rawX - lastRawX, ev.rawY - lastRawY)
                lastRawX = ev.rawX
                lastRawY = ev.rawY
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> host.dragEnded(this)
        }
        return true
    }

    override fun onDetachedFromWindow() {
        release()
        super.onDetachedFromWindow()
    }

    protected fun drawEditFrame(canvas: Canvas) {
        if (!editing) return
        val p = Painter.editFill
        canvas.drawRoundRect(RectF(0f, 0f, width.toFloat(), height.toFloat()), 12f, 12f, p)
        canvas.drawRoundRect(RectF(2f, 2f, width - 2f, height - 2f), 12f, 12f, Painter.editStroke)
        val t = Painter.text(host.density * 11f)
        val w = t.measureText(title)
        canvas.drawRect(width / 2f - w / 2f - 6f, 4f, width / 2f + w / 2f + 6f, 8f + t.textSize + 4f, Painter.labelBg)
        canvas.drawText(title, width / 2f - w / 2f, 8f + t.textSize, t)
    }
}

/** Shared drawing for round buttons: fill, icon clipped to a circle, ring, cooldown wedge, text. */
object Painter {
    val editFill: Paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(30, 60, 150, 255); style = Paint.Style.FILL }
    val editStroke: Paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(230, 80, 170, 255); style = Paint.Style.STROKE; strokeWidth = 4f }
    val labelBg: Paint = Paint().apply { color = Color.argb(180, 0, 0, 0) }

    private val fill = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.FILL }
    private val ring = Paint(Paint.ANTI_ALIAS_FLAG).apply { style = Paint.Style.STROKE }
    private val bitmapPaint = Paint(Paint.ANTI_ALIAS_FLAG or Paint.FILTER_BITMAP_FLAG)
    private val cooldown = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.argb(150, 0, 0, 0); style = Paint.Style.FILL }
    private val textPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.WHITE; typeface = android.graphics.Typeface.DEFAULT_BOLD }
    private val shadowPaint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.BLACK; typeface = android.graphics.Typeface.DEFAULT_BOLD }
    private val clip = Path()
    private val dst = Rect()
    private val arc = RectF()

    fun text(size: Float): Paint {
        textPaint.textSize = size
        return textPaint
    }

    fun roundButton(
        canvas: Canvas,
        cx: Float,
        cy: Float,
        radius: Float,
        fillColor: Int,
        alpha: Float,
        icon: Bitmap?,
        label: String?,
        held: Boolean,
        cdRemaining: Float = 0f,
        cdTotal: Float = 0f,
        density: Float = 2f,
    ) {
        val r = if (held) radius * 0.92f else radius
        val a = (alpha * 255).toInt().coerceIn(0, 255)

        fill.color = fillColor
        fill.alpha = (Color.alpha(fillColor) * alpha).toInt()
        canvas.drawCircle(cx, cy, r, fill)

        if (icon != null) {
            val ir = r * 0.92f
            clip.reset()
            clip.addCircle(cx, cy, ir, Path.Direction.CW)
            canvas.save()
            canvas.clipPath(clip)
            dst.set((cx - ir).toInt(), (cy - ir).toInt(), ceil(cx + ir).toInt(), ceil(cy + ir).toInt())
            bitmapPaint.alpha = a
            canvas.drawBitmap(icon, null, dst, bitmapPaint)
            canvas.restore()
        } else if (!label.isNullOrEmpty()) {
            val t = text(maxOf(10f, r * 0.42f))
            t.alpha = a
            val w = t.measureText(label)
            canvas.drawText(label, cx - w / 2f, cy + t.textSize * 0.35f, t)
        }

        if (cdTotal > 0f && cdRemaining > 0f) {
            val frac = (cdRemaining / cdTotal).coerceIn(0f, 1f)
            arc.set(cx - r, cy - r, cx + r, cy + r)
            cooldown.alpha = (150 * alpha).toInt()
            canvas.drawArc(arc, -90f, 360f * frac, true, cooldown)

            val s = if (cdRemaining >= 10f) ceil(cdRemaining).toInt().toString() else String.format("%.1f", cdRemaining)
            val t = text(maxOf(10f, r * 0.55f))
            t.alpha = a
            shadowPaint.textSize = t.textSize
            shadowPaint.alpha = a
            val w = t.measureText(s)
            canvas.drawText(s, cx - w / 2f + density, cy + t.textSize * 0.35f + density, shadowPaint)
            canvas.drawText(s, cx - w / 2f, cy + t.textSize * 0.35f, t)
        }

        ring.color = Color.WHITE
        ring.alpha = ((if (held) 240 else 90) * alpha).toInt()
        ring.strokeWidth = if (held) 3f * density else 1.5f * density
        canvas.drawCircle(cx, cy, r, ring)
    }
}
