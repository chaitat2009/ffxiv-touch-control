package dev.touchcontrol.companion

import android.Manifest
import android.content.Intent
import android.content.pm.PackageManager
import android.graphics.Typeface
import android.net.Uri
import android.os.Build
import android.os.Bundle
import android.provider.Settings
import android.text.InputType
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.widget.AdapterView
import android.widget.ArrayAdapter
import android.widget.Button
import android.widget.CheckBox
import android.widget.EditText
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.SeekBar
import android.widget.Spinner
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity

/**
 * Settings screen, built in code so the app has no layout resources to get wrong. Every change is written to
 * Prefs immediately and pushed to the running overlay service.
 */
class MainActivity : AppCompatActivity() {

    private lateinit var root: LinearLayout
    private lateinit var layout: Layout
    private lateinit var status: TextView
    private val refresh: () -> Unit = { updateStatus() }

    private val dp get() = resources.displayMetrics.density

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        layout = Prefs.layout(this)

        val scroll = ScrollView(this)
        root = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val p = (16 * dp).toInt()
            setPadding(p, p, p, p)
        }
        scroll.addView(root)
        setContentView(scroll)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) != PackageManager.PERMISSION_GRANTED
        ) {
            requestPermissions(arrayOf(Manifest.permission.POST_NOTIFICATIONS), 1)
        }

        build()
    }

    override fun onResume() {
        super.onResume()
        Bridge.listeners.add(refresh)
        updateStatus()
    }

    override fun onPause() {
        Bridge.listeners.remove(refresh)
        super.onPause()
    }

    // ---- persistence ------------------------------------------------------------------------------------

    private fun save() {
        Prefs.saveLayout(this, layout)
        if (OverlayService.running) OverlayService.start(this, OverlayService.ACTION_RELOAD)
    }

    private fun rebuildUi() {
        root.removeAllViews()
        build()
    }

    // ---- UI ---------------------------------------------------------------------------------------------

    private fun build() {
        header("Touch Control companion")
        status = text("")
        updateStatus()

        section("Connection")
        val host = EditText(this).apply { setText(Prefs.host(this@MainActivity)); hint = "Host (127.0.0.1 = same device)" }
        val port = EditText(this).apply { setText(Prefs.port(this@MainActivity).toString()); inputType = InputType.TYPE_CLASS_NUMBER; hint = "Port" }
        root.addView(host)
        root.addView(port)
        button("Save connection") {
            Prefs.saveConnection(this, host.text.toString().trim().ifEmpty { "127.0.0.1" }, port.text.toString().toIntOrNull() ?: 47800)
            if (OverlayService.running) OverlayService.start(this, OverlayService.ACTION_RELOAD)
            updateStatus()
        }
        text("Same device (emulator): keep 127.0.0.1. Make sure the plugin's Phone app tab shows the bridge listening on the same port.")

        section("Overlay")
        row {
            button("Grant overlay permission", it) {
                startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName")))
            }
        }
        row {
            button("Start", it) {
                if (!Settings.canDrawOverlays(this)) {
                    startActivity(Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION, Uri.parse("package:$packageName")))
                } else {
                    OverlayService.start(this)
                    updateStatus()
                }
            }
            button("Stop", it) { OverlayService.start(this, OverlayService.ACTION_STOP); updateStatus() }
            button("Edit layout", it) { OverlayService.start(this, OverlayService.ACTION_TOGGLE_EDIT) }
            button("Hide / show", it) { OverlayService.start(this, OverlayService.ACTION_TOGGLE_HIDE) }
        }
        text("Edit layout: drag any control to move it, tap Done when finished. The overlay keeps running in the background; use the notification to hide or stop it.")

        section("Size and look")
        seek("Overall scale", (layout.globalScale * 100).toInt(), 50, 200, "%") { layout.globalScale = it / 100f; save() }
        seek("Opacity", (layout.opacity * 100).toInt(), 20, 100, "%") { layout.opacity = it / 100f; save() }

        section("Joystick")
        check("Enabled", layout.joystick.enabled) { layout.joystick.enabled = it; save() }
        check("Floating (base appears where you touch)", layout.joystick.floating) { layout.joystick.floating = it; save() }
        seek("Size", layout.joystick.baseRadiusDp.toInt(), 40, 140, " dp") {
            layout.joystick.baseRadiusDp = it.toFloat()
            layout.joystick.zoneRadiusDp = it * 1.7f
            layout.joystick.knobRadiusDp = it * 0.4f
            save()
        }
        seek("Dead zone", (layout.joystick.deadZone * 100).toInt(), 0, 60, "%") { layout.joystick.deadZone = it / 100f; save() }

        section("Skill wheels")
        for ((index, w) in layout.wheels.withIndex()) wheelEditor(index, w)
        button("Add skill wheel") {
            layout.wheels.add(WheelConfig("wheel${System.currentTimeMillis()}").apply { hotbar = (layout.wheels.size).coerceAtMost(9) })
            save(); rebuildUi()
        }

        section("Quick buttons")
        for ((index, b) in layout.buttons.withIndex()) buttonEditor(index, b)
        button("Add button") {
            layout.buttons.add(ButtonConfig("button${System.currentTimeMillis()}").apply { kind = ButtonKind.KEY; value = Layout.VK_SPACE })
            save(); rebuildUi()
        }

        section("Menu bar")
        check("Enabled", layout.menuBar.enabled) { layout.menuBar.enabled = it; save() }
        for ((index, id) in layout.menuBar.commands.withIndex()) {
            row {
                catalogSpinner(it, Bridge.mainCommandCatalog, id, "Main command") { v -> layout.menuBar.commands[index] = v; save() }
                button("Remove", it) { layout.menuBar.commands.removeAt(index); save(); rebuildUi() }
            }
        }
        button("Add menu command") { layout.menuBar.commands.add(2); save(); rebuildUi() }

        section("Camera pad")
        check("Enabled (only if dragging on the game does not rotate the camera)", layout.cameraPad.enabled) { layout.cameraPad.enabled = it; save() }
        seek("Gain", (layout.cameraPad.gain * 100).toInt(), 25, 400, "%") { layout.cameraPad.gain = it / 100f; save() }

        section("Reset")
        button("Reset layout to defaults") {
            Prefs.resetLayout(this)
            layout = Layout.defaults()
            save(); rebuildUi()
        }
    }

    private fun wheelEditor(index: Int, w: WheelConfig) {
        val box = card()
        val title = TextView(this).apply { text = "Wheel ${index + 1}"; setTypeface(null, Typeface.BOLD) }
        box.addView(title)
        box.addView(CheckBox(this).apply { text = "Enabled"; isChecked = w.enabled; setOnCheckedChangeListener { _, v -> w.enabled = v; save() } })
        box.addView(CheckBox(this).apply { text = "Row instead of wheel"; isChecked = w.row; setOnCheckedChangeListener { _, v -> w.row = v; save() } })
        box.addView(CheckBox(this).apply { text = "Big centre button for first slot"; isChecked = w.centerFirst; setOnCheckedChangeListener { _, v -> w.centerFirst = v; save() } })
        box.addView(CheckBox(this).apply { text = "Hide empty slots"; isChecked = w.hideEmpty; setOnCheckedChangeListener { _, v -> w.hideEmpty = v; save() } })
        rowIn(box) {
            numberSpinner(it, "Hotbar", 1, 10, w.hotbar + 1) { v -> w.hotbar = v - 1; save() }
            numberSpinner(it, "First slot", 1, 12, w.firstSlot + 1) { v -> w.firstSlot = v - 1; save() }
            numberSpinner(it, "Slots", 1, 12, w.count) { v -> w.count = v; save() }
        }
        seekIn(box, "Button size", w.buttonRadiusDp.toInt(), 14, 60, " dp") { w.buttonRadiusDp = it.toFloat(); save() }
        seekIn(box, "Ring distance", w.ringRadiusDp.toInt(), 40, 220, " dp") { w.ringRadiusDp = it.toFloat(); save() }
        box.addView(Button(this).apply { text = "Remove wheel"; setOnClickListener { layout.wheels.removeAt(index); save(); rebuildUi() } })
    }

    private fun buttonEditor(index: Int, b: ButtonConfig) {
        val box = card()
        box.addView(TextView(this).apply { text = "Button ${index + 1}"; setTypeface(null, Typeface.BOLD) })
        box.addView(CheckBox(this).apply { text = "Enabled"; isChecked = b.enabled; setOnCheckedChangeListener { _, v -> b.enabled = v; save() } })

        val kinds = ButtonKind.values().map { it.name.lowercase().replace('_', ' ') }
        rowIn(box) { r ->
            val kind = Spinner(this)
            kind.adapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, kinds)
            kind.setSelection(b.kind.ordinal)
            kind.onItemSelectedListener = selected { pos ->
                val k = ButtonKind.values()[pos]
                if (k != b.kind) {
                    b.kind = k
                    b.value = when (k) {
                        ButtonKind.KEY -> Layout.VK_SPACE
                        ButtonKind.GENERAL_ACTION -> Layout.GA_SPRINT
                        ButtonKind.MAIN_COMMAND -> 2
                        else -> 0
                    }
                    save(); rebuildUi()
                }
            }
            r.addView(kind, weight())
        }

        rowIn(box) { r ->
            when (b.kind) {
                ButtonKind.KEY -> keySpinner(r, b.value) { v -> b.value = v; save() }
                ButtonKind.GENERAL_ACTION -> catalogSpinner(r, Bridge.generalActionCatalog, b.value, "General action") { v -> b.value = v; save() }
                ButtonKind.MAIN_COMMAND -> catalogSpinner(r, Bridge.mainCommandCatalog, b.value, "Main command") { v -> b.value = v; save() }
                else -> r.addView(TextView(this).apply { text = "No value needed" })
            }
        }

        val label = EditText(this).apply { setText(b.label); hint = "Label (optional)" }
        label.setOnFocusChangeListener { _, has -> if (!has && label.text.toString() != b.label) { b.label = label.text.toString(); save() } }
        box.addView(label)
        seekIn(box, "Size", b.radiusDp.toInt(), 14, 60, " dp") { b.radiusDp = it.toFloat(); save() }
        box.addView(Button(this).apply { text = "Remove button"; setOnClickListener { layout.buttons.removeAt(index); save(); rebuildUi() } })
    }

    // ---- widgets ----------------------------------------------------------------------------------------

    private fun updateStatus() {
        if (!::status.isInitialized) return
        val overlay = if (OverlayService.running) "running" else "stopped"
        val plugin = when {
            !OverlayService.running -> "not connected (start the overlay)"
            Bridge.connected -> "connected"
            else -> "connecting to ${Bridge.host}:${Bridge.port}..."
        }
        val perm = if (Settings.canDrawOverlays(this)) "granted" else "MISSING"
        status.text = "Overlay: $overlay\nGame plugin: $plugin\nDraw-over-apps permission: $perm"
    }

    private fun header(t: String) {
        root.addView(TextView(this).apply { text = t; textSize = 22f; setTypeface(null, Typeface.BOLD) })
    }

    private fun section(t: String) {
        root.addView(TextView(this).apply {
            text = t; textSize = 17f; setTypeface(null, Typeface.BOLD)
            setPadding(0, (18 * dp).toInt(), 0, (6 * dp).toInt())
        })
    }

    private fun text(t: String): TextView {
        val v = TextView(this).apply { text = t }
        root.addView(v)
        return v
    }

    private fun card(): LinearLayout {
        val box = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            val p = (10 * dp).toInt()
            setPadding(p, p, p, p)
            setBackgroundColor(0x14808080)
        }
        val lp = LinearLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.WRAP_CONTENT)
        lp.bottomMargin = (8 * dp).toInt()
        root.addView(box, lp)
        return box
    }

    private fun weight() = LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WRAP_CONTENT, 1f)

    private fun row(fill: (LinearLayout) -> Unit) = rowIn(root, fill)

    private fun rowIn(parent: LinearLayout, fill: (LinearLayout) -> Unit) {
        val r = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
        fill(r)
        parent.addView(r)
    }

    private fun button(t: String, parent: LinearLayout = root, onClick: () -> Unit) {
        val b = Button(this).apply { text = t; setOnClickListener { onClick() } }
        if (parent === root) parent.addView(b) else parent.addView(b, weight())
    }

    private fun check(t: String, value: Boolean, onChange: (Boolean) -> Unit) {
        root.addView(CheckBox(this).apply { text = t; isChecked = value; setOnCheckedChangeListener { _, v -> onChange(v) } })
    }

    private fun seek(t: String, value: Int, min: Int, max: Int, unit: String, onChange: (Int) -> Unit) = seekIn(root, t, value, min, max, unit, onChange)

    private fun seekIn(parent: LinearLayout, t: String, value: Int, min: Int, max: Int, unit: String, onChange: (Int) -> Unit) {
        val label = TextView(this).apply { text = "$t: $value$unit" }
        val bar = SeekBar(this).apply {
            this.max = max - min
            progress = (value - min).coerceIn(0, max - min)
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(sb: SeekBar, p: Int, fromUser: Boolean) { label.text = "$t: ${p + min}$unit" }
                override fun onStartTrackingTouch(sb: SeekBar) {}
                override fun onStopTrackingTouch(sb: SeekBar) { onChange(sb.progress + min) }
            })
        }
        parent.addView(label)
        parent.addView(bar)
    }

    private fun selected(onPos: (Int) -> Unit) = object : AdapterView.OnItemSelectedListener {
        var first = true
        override fun onItemSelected(parent: AdapterView<*>?, view: View?, position: Int, id: Long) {
            if (first) { first = false; return }
            onPos(position)
        }
        override fun onNothingSelected(parent: AdapterView<*>?) {}
    }

    private fun numberSpinner(parent: LinearLayout, label: String, min: Int, max: Int, value: Int, onChange: (Int) -> Unit) {
        val items = (min..max).map { "$label $it" }
        val s = Spinner(this)
        s.adapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, items)
        s.setSelection((value - min).coerceIn(0, items.size - 1))
        s.onItemSelectedListener = selected { onChange(it + min) }
        parent.addView(s, weight())
    }

    private fun catalogSpinner(parent: LinearLayout, catalog: List<CatalogEntry>, value: Int, what: String, onChange: (Int) -> Unit) {
        if (catalog.isEmpty()) {
            parent.addView(TextView(this).apply { text = "$what #$value (connect to the game to pick by name)" }, weight())
            return
        }
        val s = Spinner(this)
        s.adapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, catalog.map { it.name })
        val idx = catalog.indexOfFirst { it.id == value }
        if (idx >= 0) s.setSelection(idx)
        s.onItemSelectedListener = selected { onChange(catalog[it].id) }
        parent.addView(s, weight())
    }

    private fun keySpinner(parent: LinearLayout, value: Int, onChange: (Int) -> Unit) {
        val keys = keyList()
        val s = Spinner(this)
        s.adapter = ArrayAdapter(this, android.R.layout.simple_spinner_dropdown_item, keys.map { it.first })
        val idx = keys.indexOfFirst { it.second == value }
        if (idx >= 0) s.setSelection(idx)
        s.onItemSelectedListener = selected { onChange(keys[it].second) }
        parent.addView(s, weight())
    }

    private fun keyList(): List<Pair<String, Int>> {
        val list = ArrayList<Pair<String, Int>>()
        list.add("Space" to 0x20); list.add("Escape" to 0x1B); list.add("Tab" to 0x09); list.add("Enter" to 0x0D)
        list.add("Shift" to 0x10); list.add("Ctrl" to 0x11); list.add("Alt" to 0x12)
        list.add("Up" to 0x26); list.add("Down" to 0x28); list.add("Left" to 0x25); list.add("Right" to 0x27)
        for (i in 0..9) list.add("$i" to (0x30 + i))
        for (c in 'A'..'Z') list.add("$c" to (0x41 + (c - 'A')))
        for (i in 0..9) list.add("Numpad $i" to (0x60 + i))
        for (i in 1..12) list.add("F$i" to (0x70 + i - 1))
        list.add("- (minus)" to 0xBD); list.add("= (equals)" to 0xBB)
        return list
    }
}
