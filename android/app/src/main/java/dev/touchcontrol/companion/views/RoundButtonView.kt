package dev.touchcontrol.companion.views

import android.content.Context
import android.graphics.Canvas
import android.graphics.Color
import android.view.MotionEvent
import dev.touchcontrol.companion.Bridge
import dev.touchcontrol.companion.ButtonConfig
import dev.touchcontrol.companion.ButtonKind
import dev.touchcontrol.companion.Layout

/**
 * A single free-floating quick button (Jump, Sprint, Mount, Target, ...). Key buttons hold the key for as long
 * as they are touched so a long-press Jump still jumps high; everything else fires on press.
 */
class RoundButtonView(context: Context, host: OverlayHost, private val cfg: ButtonConfig) : ControlView(context, host) {

    override val title get() = resolveLabel()

    private var pointerId = -1
    private val listener: () -> Unit = { invalidate() }

    private val radius get() = cfg.radiusDp * dp * cfg.placement.scale
    private val pad get() = 4f * dp

    override fun desiredSize(): Pair<Int, Int> {
        val s = ((radius + pad) * 2f).toInt()
        return s to s
    }

    override fun onAttachedToWindow() {
        super.onAttachedToWindow()
        Bridge.listeners.add(listener)
    }

    override fun onDetachedFromWindow() {
        Bridge.listeners.remove(listener)
        super.onDetachedFromWindow()
    }

    override fun handleTouch(ev: MotionEvent): Boolean {
        when (ev.actionMasked) {
            MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> {
                if (pointerId != -1) return true
                pointerId = ev.getPointerId(ev.actionIndex)
                press()
                invalidate()
            }
            MotionEvent.ACTION_UP, MotionEvent.ACTION_CANCEL -> release()
            MotionEvent.ACTION_POINTER_UP -> if (ev.getPointerId(ev.actionIndex) == pointerId) release()
        }
        return true
    }

    private fun press() {
        when (cfg.kind) {
            ButtonKind.KEY -> Bridge.key(cfg.value, true)
            ButtonKind.GENERAL_ACTION -> Bridge.generalAction(cfg.value)
            ButtonKind.MAIN_COMMAND -> Bridge.mainCommand(cfg.value)
            ButtonKind.MOUNT -> Bridge.mountToggle()
            ButtonKind.ESCAPE -> Bridge.key(Layout.VK_ESCAPE, true)
            ButtonKind.EDIT_LAYOUT -> host.toggleEditMode()
            ButtonKind.HIDE -> host.hideOverlay()
        }
    }

    override fun release() {
        if (pointerId == -1) return
        pointerId = -1
        when (cfg.kind) {
            ButtonKind.KEY -> Bridge.key(cfg.value, false)
            ButtonKind.ESCAPE -> Bridge.key(Layout.VK_ESCAPE, false)
            else -> {}
        }
        invalidate()
    }

    private fun resolveLabel(): String {
        if (cfg.label.isNotEmpty()) return cfg.label
        return when (cfg.kind) {
            ButtonKind.KEY -> keyName(cfg.value)
            ButtonKind.GENERAL_ACTION -> Bridge.generalActionCatalog.firstOrNull { it.id == cfg.value }?.name ?: "GA ${cfg.value}"
            ButtonKind.MAIN_COMMAND -> Bridge.mainCommandCatalog.firstOrNull { it.id == cfg.value }?.name ?: "MC ${cfg.value}"
            ButtonKind.MOUNT -> if (Bridge.state?.mounted == true) "Dismount" else "Mount"
            ButtonKind.ESCAPE -> "Menu"
            ButtonKind.EDIT_LAYOUT -> "Edit"
            ButtonKind.HIDE -> "Hide"
        }
    }

    private fun iconId(): Int = when (cfg.kind) {
        ButtonKind.GENERAL_ACTION -> Bridge.generalActionCatalog.firstOrNull { it.id == cfg.value }?.icon ?: 0
        ButtonKind.MAIN_COMMAND -> Bridge.mainCommandCatalog.firstOrNull { it.id == cfg.value }?.icon ?: 0
        ButtonKind.MOUNT -> {
            val id = if (Bridge.state?.mounted == true) GA_DISMOUNT else GA_MOUNT_ROULETTE
            Bridge.generalActionCatalog.firstOrNull { it.id == id }?.icon ?: 0
        }
        else -> 0
    }

    override fun onDraw(canvas: Canvas) {
        val cx = width / 2f
        val cy = height / 2f
        val icon = Bridge.icon(iconId())
        val ga = if (cfg.kind == ButtonKind.GENERAL_ACTION) Bridge.state?.generalActions?.get(cfg.value) else null
        Painter.roundButton(
            canvas, cx, cy, radius, Color.argb(180, 26, 26, 36), host.opacity, icon,
            if (icon == null) shortLabel(resolveLabel()) else null, pointerId != -1,
            ga?.cd ?: 0f, ga?.total ?: 0f, host.density,
        )
        drawEditFrame(canvas)
    }

    companion object {
        const val GA_MOUNT_ROULETTE = 9
        const val GA_DISMOUNT = 23

        fun shortLabel(s: String) = if (s.length <= 8) s else s.substring(0, 7) + "."

        fun keyName(vk: Int): String = when (vk) {
            0x20 -> "Jump"
            0x1B -> "Esc"
            0x09 -> "Tab"
            0x0D -> "Enter"
            0x60 -> "Num0"
            in 0x61..0x69 -> "Num${vk - 0x60}"
            in 0x30..0x39 -> (vk - 0x30).toString()
            in 0x41..0x5A -> ('A' + (vk - 0x41)).toString()
            in 0x70..0x7B -> "F${vk - 0x70 + 1}"
            else -> "VK $vk"
        }
    }
}
