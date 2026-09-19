package dev.touchcontrol.companion.views

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.view.MotionEvent
import dev.touchcontrol.companion.Bridge
import dev.touchcontrol.companion.Layout
import dev.touchcontrol.companion.MenuBarConfig

/**
 * The strip along the top edge: Menu (Escape), the main-menu windows (Character, Inventory, Map, ...), then
 * Edit layout and Hide overlay.
 */
class MenuBarView(context: Context, host: OverlayHost, private val cfg: MenuBarConfig) : ControlView(context, host) {

    override val title = "Menu bar"

    private sealed class Item {
        object Escape : Item()
        class Command(val id: Int) : Item()
        object Edit : Item()
        object Hide : Item()
    }

    private val items = ArrayList<Item>()
    private val pressed = HashMap<Int, Int>()   // pointerId -> item index
    private val listener: () -> Unit = { invalidate() }

    private val radius get() = cfg.radiusDp * dp * cfg.placement.scale
    private val step get() = radius * 2f + cfg.spacingDp * dp * cfg.placement.scale
    private val pad get() = 4f * dp

    init {
        items.add(Item.Escape)
        for (id in cfg.commands) items.add(Item.Command(id))
        items.add(Item.Edit)
        items.add(Item.Hide)
    }

    override fun desiredSize(): Pair<Int, Int> {
        val w = (step * items.size - cfg.spacingDp * dp * cfg.placement.scale + pad * 2f).toInt()
        val h = ((radius + pad) * 2f).toInt()
        return w to h
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        Bridge.listeners.add(listener)
    }

    override fun onDetachedFromWindow() {
        Bridge.listeners.remove(listener)
        super.onDetachedFromWindow()
    }

    private fun centerX(i: Int) = pad + radius + i * step

    private fun hit(x: Float, y: Float): Int {
        val cy = height / 2f
        for (i in items.indices) {
            val dx = x - centerX(i)
            val dy = y - cy
            if (dx * dx + dy * dy <= radius * radius) return i
        }
        return -1
    }

    override fun handleTouch(ev: MotionEvent): Boolean {
        when (ev.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                val idx = ev.actionIndex
                val i = hit(ev.getX(idx), ev.getY(idx))
                if (i >= 0) {
                    pressed[ev.getPointerId(idx)] = i
                    press(items[i])
                    invalidate()
                }
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> {
                for (i in pressed.values) unpress(items[i])
                pressed.clear()
                invalidate()
            }
            MotionEvent.ACTION_POINTER_UP -> {
                pressed.remove(ev.getPointerId(ev.actionIndex))?.let { unpress(items[it]) }
                invalidate()
            }
        }
        return true
    }

    private fun press(item: Item) {
        when (item) {
            Item.Escape -> Bridge.key(Layout.VK_ESCAPE, true)
            is Item.Command -> Bridge.mainCommand(item.id)
            Item.Edit -> host.toggleEditMode()
            Item.Hide -> host.hideOverlay()
        }
    }

    private fun unpress(item: Item) {
        if (item == Item.Escape) Bridge.key(Layout.VK_ESCAPE, false)
    }

    override fun release() {
        for (i in pressed.values) unpress(items[i])
        pressed.clear()
    }

    override fun onDraw(canvas: Canvas) {
        val cy = height / 2f
        val fill = Color.argb(180, 20, 20, 26)
        for (i in items.indices) {
            val item = items[i]
            var icon: android.graphics.Bitmap? = null
            var label = ""
            when (item) {
                Item.Escape -> label = "≡"
                is Item.Command -> {
                    val entry = Bridge.mainCommandCatalog.firstOrNull { it.id == item.id }
                    icon = Bridge.icon(entry?.icon ?: 0)
                    if (icon == null) label = entry?.name?.take(3) ?: "?"
                }
                Item.Edit -> label = "✎"
                Item.Hide -> label = "◌"
            }
            Painter.roundButton(canvas, centerX(i), cy, radius, fill, host.opacity, icon, label, pressed.containsValue(i), density = host.density)
        }
        drawEditFrame(canvas)
    }
}
