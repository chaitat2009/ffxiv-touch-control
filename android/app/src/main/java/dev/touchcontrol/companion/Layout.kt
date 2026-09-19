package dev.touchcontrol.companion

import android.content.Context
import org.json.JSONArray
import org.json.JSONObject

/** Screen-anchored position as fractions of the screen, so a layout survives rotation and resolution changes. */
class Placement(var cx: Float, var cy: Float, var scale: Float = 1f) {
    fun toJson() = JSONObject().put("cx", cx.toDouble()).put("cy", cy.toDouble()).put("scale", scale.toDouble())

    companion object {
        fun fromJson(o: JSONObject?, dx: Float, dy: Float) = Placement(
            o?.optDouble("cx", dx.toDouble())?.toFloat() ?: dx,
            o?.optDouble("cy", dy.toDouble())?.toFloat() ?: dy,
            o?.optDouble("scale", 1.0)?.toFloat() ?: 1f,
        )
    }
}

class JoystickConfig {
    var enabled = true
    var placement = Placement(0.14f, 0.70f)
    var zoneRadiusDp = 120f
    var baseRadiusDp = 70f
    var knobRadiusDp = 28f
    var deadZone = 0.18f
    var floating = true

    fun toJson() = JSONObject()
        .put("enabled", enabled).put("placement", placement.toJson())
        .put("zone", zoneRadiusDp.toDouble()).put("base", baseRadiusDp.toDouble()).put("knob", knobRadiusDp.toDouble())
        .put("dead", deadZone.toDouble()).put("floating", floating)

    companion object {
        fun fromJson(o: JSONObject?): JoystickConfig {
            val c = JoystickConfig()
            if (o == null) return c
            c.enabled = o.optBoolean("enabled", true)
            c.placement = Placement.fromJson(o.optJSONObject("placement"), 0.14f, 0.70f)
            c.zoneRadiusDp = o.optDouble("zone", 120.0).toFloat()
            c.baseRadiusDp = o.optDouble("base", 70.0).toFloat()
            c.knobRadiusDp = o.optDouble("knob", 28.0).toFloat()
            c.deadZone = o.optDouble("dead", 0.18).toFloat()
            c.floating = o.optBoolean("floating", true)
            return c
        }
    }
}

/** A run of hotbar slots drawn as a wheel (big centre button + rings) or a plain row. */
class WheelConfig(var id: String) {
    var enabled = true
    var hotbar = 0
    var firstSlot = 0
    var count = 8
    var placement = Placement(0.86f, 0.70f)
    var centerFirst = true
    var centerRadiusDp = 44f
    var buttonRadiusDp = 28f
    var ringRadiusDp = 96f
    var ringSpacingDp = 62f
    var ringCapacity = 6
    var arcStartDeg = 180f
    var arcEndDeg = 360f
    var row = false
    var rowSpacingDp = 10f
    var hideEmpty = true

    fun toJson() = JSONObject()
        .put("id", id).put("enabled", enabled).put("hotbar", hotbar).put("first", firstSlot).put("count", count)
        .put("placement", placement.toJson()).put("centerFirst", centerFirst)
        .put("centerR", centerRadiusDp.toDouble()).put("buttonR", buttonRadiusDp.toDouble())
        .put("ringR", ringRadiusDp.toDouble()).put("ringSpacing", ringSpacingDp.toDouble()).put("ringCap", ringCapacity)
        .put("arcStart", arcStartDeg.toDouble()).put("arcEnd", arcEndDeg.toDouble())
        .put("row", row).put("rowSpacing", rowSpacingDp.toDouble()).put("hideEmpty", hideEmpty)

    companion object {
        fun fromJson(o: JSONObject): WheelConfig {
            val c = WheelConfig(o.optString("id", "wheel"))
            c.enabled = o.optBoolean("enabled", true)
            c.hotbar = o.optInt("hotbar", 0)
            c.firstSlot = o.optInt("first", 0)
            c.count = o.optInt("count", 8)
            c.placement = Placement.fromJson(o.optJSONObject("placement"), 0.86f, 0.70f)
            c.centerFirst = o.optBoolean("centerFirst", true)
            c.centerRadiusDp = o.optDouble("centerR", 44.0).toFloat()
            c.buttonRadiusDp = o.optDouble("buttonR", 28.0).toFloat()
            c.ringRadiusDp = o.optDouble("ringR", 96.0).toFloat()
            c.ringSpacingDp = o.optDouble("ringSpacing", 62.0).toFloat()
            c.ringCapacity = o.optInt("ringCap", 6)
            c.arcStartDeg = o.optDouble("arcStart", 180.0).toFloat()
            c.arcEndDeg = o.optDouble("arcEnd", 360.0).toFloat()
            c.row = o.optBoolean("row", false)
            c.rowSpacingDp = o.optDouble("rowSpacing", 10.0).toFloat()
            c.hideEmpty = o.optBoolean("hideEmpty", true)
            return c
        }
    }
}

enum class ButtonKind { KEY, GENERAL_ACTION, MAIN_COMMAND, MOUNT, ESCAPE, EDIT_LAYOUT, HIDE }

class ButtonConfig(var id: String) {
    var enabled = true
    var kind = ButtonKind.KEY
    var value = 0
    var label = ""
    var placement = Placement(0.5f, 0.5f)
    var radiusDp = 26f

    fun toJson() = JSONObject()
        .put("id", id).put("enabled", enabled).put("kind", kind.name).put("value", value).put("label", label)
        .put("placement", placement.toJson()).put("radius", radiusDp.toDouble())

    companion object {
        fun fromJson(o: JSONObject): ButtonConfig {
            val c = ButtonConfig(o.optString("id", "button"))
            c.enabled = o.optBoolean("enabled", true)
            c.kind = runCatching { ButtonKind.valueOf(o.optString("kind", "KEY")) }.getOrDefault(ButtonKind.KEY)
            c.value = o.optInt("value", 0)
            c.label = o.optString("label", "")
            c.placement = Placement.fromJson(o.optJSONObject("placement"), 0.5f, 0.5f)
            c.radiusDp = o.optDouble("radius", 26.0).toFloat()
            return c
        }
    }
}

class MenuBarConfig {
    var enabled = true
    var placement = Placement(0.5f, 0.05f)
    var radiusDp = 17f
    var spacingDp = 10f
    /** MainCommand row ids: Character, Inventory, Actions & Traits, Journal, Map, Duty Finder, Timers. */
    var commands = mutableListOf(2, 10, 3, 4, 16, 33, 5)

    fun toJson() = JSONObject()
        .put("enabled", enabled).put("placement", placement.toJson())
        .put("radius", radiusDp.toDouble()).put("spacing", spacingDp.toDouble()).put("commands", JSONArray(commands))

    companion object {
        fun fromJson(o: JSONObject?): MenuBarConfig {
            val c = MenuBarConfig()
            if (o == null) return c
            c.enabled = o.optBoolean("enabled", true)
            c.placement = Placement.fromJson(o.optJSONObject("placement"), 0.5f, 0.05f)
            c.radiusDp = o.optDouble("radius", 17.0).toFloat()
            c.spacingDp = o.optDouble("spacing", 10.0).toFloat()
            val arr = o.optJSONArray("commands")
            if (arr != null) {
                c.commands = MutableList(arr.length()) { arr.getInt(it) }
            }
            return c
        }
    }
}

class CameraPadConfig {
    /** A drag zone that rotates the camera through the plugin, for emulators whose touch-to-mouse mapping does not drag. */
    var enabled = false
    var x = 0.55f
    var y = 0.15f
    var w = 0.40f
    var h = 0.40f
    var gain = 1f

    fun toJson() = JSONObject().put("enabled", enabled).put("x", x.toDouble()).put("y", y.toDouble())
        .put("w", w.toDouble()).put("h", h.toDouble()).put("gain", gain.toDouble())

    companion object {
        fun fromJson(o: JSONObject?): CameraPadConfig {
            val c = CameraPadConfig()
            if (o == null) return c
            c.enabled = o.optBoolean("enabled", false)
            c.x = o.optDouble("x", 0.55).toFloat()
            c.y = o.optDouble("y", 0.15).toFloat()
            c.w = o.optDouble("w", 0.40).toFloat()
            c.h = o.optDouble("h", 0.40).toFloat()
            c.gain = o.optDouble("gain", 1.0).toFloat()
            return c
        }
    }
}

class Layout {
    var globalScale = 1f
    var opacity = 0.9f
    var joystick = JoystickConfig()
    var wheels = mutableListOf<WheelConfig>()
    var buttons = mutableListOf<ButtonConfig>()
    var menuBar = MenuBarConfig()
    var cameraPad = CameraPadConfig()

    fun toJson(): JSONObject = JSONObject()
        .put("globalScale", globalScale.toDouble()).put("opacity", opacity.toDouble())
        .put("joystick", joystick.toJson())
        .put("wheels", JSONArray(wheels.map { it.toJson() }))
        .put("buttons", JSONArray(buttons.map { it.toJson() }))
        .put("menuBar", menuBar.toJson())
        .put("cameraPad", cameraPad.toJson())

    companion object {
        const val VK_SPACE = 0x20
        const val VK_ESCAPE = 0x1B
        const val VK_NUMPAD0 = 0x60
        const val VK_R = 0x52
        const val GA_SPRINT = 4
        const val GA_TARGET_FORWARD = 16

        fun defaults(): Layout {
            val l = Layout()
            l.wheels.add(WheelConfig("wheel1").apply { hotbar = 0; count = 8 })
            l.wheels.add(WheelConfig("wheel2").apply {
                hotbar = 1; count = 6; centerFirst = false; row = true; buttonRadiusDp = 24f
                placement = Placement(0.55f, 0.92f)
            })
            l.buttons.add(ButtonConfig("jump").apply { kind = ButtonKind.KEY; value = VK_SPACE; label = "Jump"; radiusDp = 30f; placement = Placement(0.70f, 0.76f) })
            l.buttons.add(ButtonConfig("sprint").apply { kind = ButtonKind.GENERAL_ACTION; value = GA_SPRINT; placement = Placement(0.28f, 0.86f) })
            l.buttons.add(ButtonConfig("mount").apply { kind = ButtonKind.MOUNT; placement = Placement(0.34f, 0.72f) })
            l.buttons.add(ButtonConfig("target").apply { kind = ButtonKind.GENERAL_ACTION; value = GA_TARGET_FORWARD; placement = Placement(0.95f, 0.40f) })
            l.buttons.add(ButtonConfig("interact").apply { kind = ButtonKind.KEY; value = VK_NUMPAD0; label = "Interact"; placement = Placement(0.95f, 0.27f) })
            l.buttons.add(ButtonConfig("autorun").apply { kind = ButtonKind.KEY; value = VK_R; label = "Auto-run"; radiusDp = 22f; placement = Placement(0.14f, 0.46f) })
            return l
        }

        fun fromJson(o: JSONObject): Layout {
            val l = Layout()
            l.globalScale = o.optDouble("globalScale", 1.0).toFloat()
            l.opacity = o.optDouble("opacity", 0.9).toFloat()
            l.joystick = JoystickConfig.fromJson(o.optJSONObject("joystick"))
            o.optJSONArray("wheels")?.let { arr -> for (i in 0 until arr.length()) l.wheels.add(WheelConfig.fromJson(arr.getJSONObject(i))) }
            o.optJSONArray("buttons")?.let { arr -> for (i in 0 until arr.length()) l.buttons.add(ButtonConfig.fromJson(arr.getJSONObject(i))) }
            l.menuBar = MenuBarConfig.fromJson(o.optJSONObject("menuBar"))
            l.cameraPad = CameraPadConfig.fromJson(o.optJSONObject("cameraPad"))
            return l
        }
    }
}

/** Persistence for the layout and connection settings. */
object Prefs {
    private const val FILE = "touchcontrol"

    fun layout(ctx: Context): Layout {
        val json = ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).getString("layout", null) ?: return Layout.defaults()
        return runCatching { Layout.fromJson(JSONObject(json)) }.getOrElse { Layout.defaults() }
    }

    fun saveLayout(ctx: Context, layout: Layout) {
        ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit().putString("layout", layout.toJson().toString()).apply()
    }

    fun resetLayout(ctx: Context) {
        ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit().remove("layout").apply()
    }

    fun host(ctx: Context): String = ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).getString("host", "127.0.0.1") ?: "127.0.0.1"
    fun port(ctx: Context): Int = ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).getInt("port", 47800)

    fun saveConnection(ctx: Context, host: String, port: Int) {
        ctx.getSharedPreferences(FILE, Context.MODE_PRIVATE).edit().putString("host", host).putInt("port", port).apply()
    }
}
