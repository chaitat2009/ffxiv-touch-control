using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using TouchControl.Game;
using TouchControl.UI.Widgets;

namespace TouchControl.UI;

public sealed class ConfigWindow : Window
{
    private readonly SheetCache sheets;
    private readonly (VirtualKey Key, string Name)[] keyNames;

    public ConfigWindow(SheetCache sheets) : base("Touch Control Settings##TouchControlConfig")
    {
        this.sheets = sheets;

        keyNames = Enum.GetValues<VirtualKey>()
            .Distinct()
            .Select(k => (k, ActionButtonWidget.KeyName((int)k)))
            .OrderBy(t => t.Item2)
            .ToArray();

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private static Configuration Cfg => Plugin.Config;

    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("##tabs");
        if (!tabs.Success) return;

        using (var tab = ImRaii.TabItem("General"))
        {
            if (tab.Success) DrawGeneral();
        }

        using (var tab = ImRaii.TabItem("Joystick"))
        {
            if (tab.Success) DrawJoystick();
        }

        using (var tab = ImRaii.TabItem("Skill wheels"))
        {
            if (tab.Success) DrawSkillGroups();
        }

        using (var tab = ImRaii.TabItem("Buttons"))
        {
            if (tab.Success) DrawButtons();
        }

        using (var tab = ImRaii.TabItem("Menu bar"))
        {
            if (tab.Success) DrawMenuBar();
        }

        using (var tab = ImRaii.TabItem("Camera pad"))
        {
            if (tab.Success) DrawCameraPad();
        }

        using (var tab = ImRaii.TabItem("Help"))
        {
            if (tab.Success) DrawHelp();
        }
    }

    // ---- tabs -----------------------------------------------------------------------------------------

    private void DrawGeneral()
    {
        Bool("Overlay enabled", Cfg.Enabled, v => Cfg.Enabled = v);
        Bool("Edit mode (drag controls, mouse-wheel to resize)", Cfg.EditMode, v => Cfg.EditMode = v);

        ImGui.Separator();
        Float("Global scale", Cfg.GlobalScale, 0.4f, 3f, v => Cfg.GlobalScale = v);
        Float("Opacity", Cfg.Opacity, 0.1f, 1f, v => Cfg.Opacity = v);
        Bool("Hide while typing in chat", Cfg.HideWhileTyping, v => Cfg.HideWhileTyping = v);

        ImGui.Separator();
        ImGui.Text("Key delivery");
        var backend = (int)Cfg.KeyBackend;
        if (ImGui.RadioButton("SendInput (default, needs game focus)", backend == 0)) SetBackend(KeyBackend.SendInput);
        if (ImGui.RadioButton("PostMessage (works unfocused)", backend == 1)) SetBackend(KeyBackend.PostMessage);

        ImGui.Separator();
        if (ImGui.Button("Reset layout to defaults"))
        {
            Cfg.ResetLayout();
            Cfg.Save();
        }

        ImGui.SameLine();
        if (ImGui.Button("Release all keys")) Plugin.Instance?.Overlay.ReleaseEverything();

        ImGui.Spacing();
        ImGui.TextWrapped("Tip: set Character Configuration > Control Settings > Movement Settings to \"Legacy\" so diagonal joystick input moves relative to the camera like a mobile game.");
    }

    private void SetBackend(KeyBackend backend)
    {
        Cfg.KeyBackend = backend;
        Plugin.Instance?.Keys.ReleaseAll();
        if (Plugin.Instance != null) Plugin.Instance.Keys.Backend = backend;
        Cfg.Save();
    }

    private void DrawJoystick()
    {
        var j = Cfg.Joystick;
        Bool("Enabled", j.Enabled, v => j.Enabled = v);
        Bool("Floating (base appears where you touch)", j.Floating, v => j.Floating = v);

        ImGui.Separator();
        Float("Touch zone radius", j.ZoneRadius, 60f, 400f, v => j.ZoneRadius = v);
        Float("Base radius", j.BaseRadius, 30f, 250f, v => j.BaseRadius = v);
        Float("Knob radius", j.KnobRadius, 10f, 120f, v => j.KnobRadius = v);
        Float("Dead zone", j.DeadZone, 0f, 0.6f, v => j.DeadZone = v);
        Float("Scale", j.Placement.Scale, 0.4f, 3f, v => j.Placement.Scale = v);

        ImGui.Separator();
        ImGui.Text("Keys sent");
        KeyPicker("Forward", j.ForwardKey, v => j.ForwardKey = v);
        KeyPicker("Back", j.BackKey, v => j.BackKey = v);
        KeyPicker("Left", j.LeftKey, v => j.LeftKey = v);
        KeyPicker("Right", j.RightKey, v => j.RightKey = v);
        ImGui.TextDisabled("Standard movement: set Left/Right to your strafe keys (Q/E) if you prefer strafing over turning.");

        ImGui.Separator();
        Color("Base colour", j.BaseColor, v => j.BaseColor = v);
        Color("Ring colour", j.RingColor, v => j.RingColor = v);
        Color("Knob colour", j.KnobColor, v => j.KnobColor = v);
    }

    private void DrawSkillGroups()
    {
        if (ImGui.Button("Add skill wheel"))
        {
            Cfg.SkillGroups.Add(new SkillGroupConfig { Hotbar = Math.Min(9, Cfg.SkillGroups.Count) });
            Cfg.Save();
        }

        var remove = -1;
        for (var i = 0; i < Cfg.SkillGroups.Count; i++)
        {
            var g = Cfg.SkillGroups[i];
            using var id = ImRaii.PushId(i);

            if (!ImGui.CollapsingHeader($"Wheel {i + 1}: hotbar {g.Hotbar + 1}, slots {g.FirstSlot + 1}-{g.FirstSlot + g.SlotCount}###group"))
                continue;

            Bool("Enabled", g.Enabled, v => g.Enabled = v);
            Int("Hotbar", g.Hotbar + 1, 1, 10, v => g.Hotbar = v - 1);
            Int("First slot", g.FirstSlot + 1, 1, 12, v => g.FirstSlot = v - 1);
            Int("Slot count", g.SlotCount, 1, 12, v => g.SlotCount = v);
            Float("Scale", g.Placement.Scale, 0.4f, 3f, v => g.Placement.Scale = v);

            ImGui.Separator();
            Bool("Row layout", g.RowLayout, v => g.RowLayout = v);
            if (g.RowLayout)
            {
                Float("Row spacing", g.RowSpacing, 0f, 60f, v => g.RowSpacing = v);
            }
            else
            {
                Bool("Big centre button for first slot", g.CenterFirstSlot, v => g.CenterFirstSlot = v);
                if (g.CenterFirstSlot) Float("Centre radius", g.CenterRadius, 20f, 120f, v => g.CenterRadius = v);
                Float("Ring radius", g.RingRadius, 40f, 400f, v => g.RingRadius = v);
                Float("Ring spacing", g.RingSpacing, 30f, 200f, v => g.RingSpacing = v);
                Int("Buttons per ring", g.RingCapacity, 1, 12, v => g.RingCapacity = v);
                Float("Arc start (deg, 0 = up, clockwise)", g.ArcStartDeg, -360f, 360f, v => g.ArcStartDeg = v);
                Float("Arc end (deg)", g.ArcEndDeg, -360f, 720f, v => g.ArcEndDeg = v);
            }

            Float("Button radius", g.ButtonRadius, 14f, 90f, v => g.ButtonRadius = v);

            ImGui.Separator();
            Bool("Show cooldowns", g.ShowCooldown, v => g.ShowCooldown = v);
            Bool("Show keybind hints", g.ShowKeybind, v => g.ShowKeybind = v);
            Bool("Hide empty slots", g.HideEmptySlots, v => g.HideEmptySlots = v);

            if (ImGui.Button("Remove this wheel")) remove = i;
        }

        if (remove >= 0)
        {
            Cfg.SkillGroups.RemoveAt(remove);
            Cfg.Save();
        }
    }

    private void DrawButtons()
    {
        if (ImGui.Button("Add button"))
        {
            Cfg.Buttons.Add(new ActionButtonConfig { Kind = ButtonKind.Key, Value = (int)VirtualKey.SPACE, Placement = new Placement { Center = new Vector2(0.5f, 0.5f) } });
            Cfg.Save();
        }

        var remove = -1;
        for (var i = 0; i < Cfg.Buttons.Count; i++)
        {
            var b = Cfg.Buttons[i];
            using var id = ImRaii.PushId(i);

            var header = $"{i + 1}. {DescribeButton(b)}###button";
            if (!ImGui.CollapsingHeader(header)) continue;

            Bool("Enabled", b.Enabled, v => b.Enabled = v);

            using (var combo = ImRaii.Combo("Action", b.Kind.ToString()))
            {
                if (combo.Success)
                {
                    foreach (var kind in Enum.GetValues<ButtonKind>())
                    {
                        if (ImGui.Selectable(kind.ToString(), kind == b.Kind))
                        {
                            b.Kind = kind;
                            b.Value = kind switch
                            {
                                ButtonKind.Key => (int)VirtualKey.SPACE,
                                ButtonKind.GeneralAction => 4,
                                ButtonKind.MainCommand => 2,
                                _ => 0,
                            };
                            Cfg.Save();
                        }
                    }
                }
            }

            switch (b.Kind)
            {
                case ButtonKind.Key:
                    KeyPicker("Key", b.Value, v => b.Value = v);
                    break;
                case ButtonKind.GeneralAction:
                    SheetPicker("General action", sheets.GeneralActions, b.Value, v => b.Value = v);
                    break;
                case ButtonKind.MainCommand:
                    SheetPicker("Main command", sheets.MainCommands, b.Value, v => b.Value = v);
                    break;
            }

            var label = b.Label;
            if (ImGui.InputText("Label override", ref label, 32))
            {
                b.Label = label;
                Cfg.Save();
            }

            Float("Radius", b.Radius, 14f, 90f, v => b.Radius = v);
            Float("Scale", b.Placement.Scale, 0.4f, 3f, v => b.Placement.Scale = v);
            Color("Colour", b.Color, v => b.Color = v);

            if (ImGui.Button("Remove this button")) remove = i;
        }

        if (remove >= 0)
        {
            Cfg.Buttons.RemoveAt(remove);
            Cfg.Save();
        }
    }

    private string DescribeButton(ActionButtonConfig b)
    {
        if (!string.IsNullOrEmpty(b.Label)) return b.Label;
        return b.Kind switch
        {
            ButtonKind.Key => $"Key {ActionButtonWidget.KeyName(b.Value)}",
            ButtonKind.GeneralAction => sheets.GeneralAction((uint)b.Value)?.Name ?? $"General action {b.Value}",
            ButtonKind.MainCommand => sheets.MainCommand((uint)b.Value)?.Name ?? $"Main command {b.Value}",
            ButtonKind.MountToggle => "Mount / Dismount",
            ButtonKind.OpenSettings => "Open settings",
            ButtonKind.HideOverlay => "Hide overlay",
            _ => b.Kind.ToString(),
        };
    }

    private void DrawMenuBar()
    {
        var m = Cfg.MenuBar;
        Bool("Enabled", m.Enabled, v => m.Enabled = v);
        Bool("Show back / escape button", m.ShowEscapeButton, v => m.ShowEscapeButton = v);
        Bool("Show settings button", m.ShowSettingsButton, v => m.ShowSettingsButton = v);
        Bool("Show hide-overlay button", m.ShowHideButton, v => m.ShowHideButton = v);
        Float("Button radius", m.ButtonRadius, 12f, 60f, v => m.ButtonRadius = v);
        Float("Spacing", m.Spacing, 0f, 60f, v => m.Spacing = v);
        Float("Scale", m.Placement.Scale, 0.4f, 3f, v => m.Placement.Scale = v);

        ImGui.Separator();
        ImGui.Text("Main menu commands");

        var remove = -1;
        var swap = (-1, -1);
        for (var i = 0; i < m.MainCommands.Count; i++)
        {
            using var id = ImRaii.PushId(i);
            var current = m.MainCommands[i];

            ImGui.SetNextItemWidth(220f);
            SheetPicker("##cmd", sheets.MainCommands, current, v => m.MainCommands[i] = v);

            ImGui.SameLine();
            if (ImGui.ArrowButton("##up", ImGuiDir.Up) && i > 0) swap = (i, i - 1);
            ImGui.SameLine();
            if (ImGui.ArrowButton("##down", ImGuiDir.Down) && i < m.MainCommands.Count - 1) swap = (i, i + 1);
            ImGui.SameLine();
            if (ImGui.Button("Remove")) remove = i;
        }

        if (swap.Item1 >= 0)
        {
            (m.MainCommands[swap.Item1], m.MainCommands[swap.Item2]) = (m.MainCommands[swap.Item2], m.MainCommands[swap.Item1]);
            Cfg.Save();
        }

        if (remove >= 0)
        {
            m.MainCommands.RemoveAt(remove);
            Cfg.Save();
        }

        if (ImGui.Button("Add command"))
        {
            m.MainCommands.Add(2);
            Cfg.Save();
        }
    }

    private void DrawCameraPad()
    {
        var c = Cfg.CameraPad;
        Bool("Enabled (experimental)", c.Enabled, v => c.Enabled = v);
        ImGui.TextWrapped("Normally you do not need this: dragging on the game world with a finger already orbits the camera. Enable it only if your touch layer does not produce a usable drag.");

        var rect = c.Rect;
        var changed = false;
        changed |= ImGui.SliderFloat("Left", ref rect.X, 0f, 1f);
        changed |= ImGui.SliderFloat("Top", ref rect.Y, 0f, 1f);
        changed |= ImGui.SliderFloat("Width", ref rect.Z, 0.05f, 1f);
        changed |= ImGui.SliderFloat("Height", ref rect.W, 0.05f, 1f);
        if (changed)
        {
            c.Rect = rect;
            Cfg.Save();
        }

        Float("Sensitivity", c.Sensitivity, 0.001f, 0.03f, v => c.Sensitivity = v, "%.4f");
        Bool("Invert vertical", c.InvertY, v => c.InvertY = v);
        Bool("Show faint outline", c.ShowOutline, v => c.ShowOutline = v);
    }

    private static void DrawHelp()
    {
        ImGui.TextWrapped("Commands");
        ImGui.BulletText("/touch            toggle this window");
        ImGui.BulletText("/touch on | off   show or hide the overlay");
        ImGui.BulletText("/touch edit       toggle edit mode");
        ImGui.BulletText("/touch reset      restore the default layout");

        ImGui.Separator();
        ImGui.TextWrapped("How it works");
        ImGui.BulletText("Joystick: sends W/A/S/D key presses while you drag, eight directions.");
        ImGui.BulletText("Skill wheels: mirror hotbar slots and execute them through the game, so cooldowns, macros and combos behave normally.");
        ImGui.BulletText("Buttons: keys are held while touched; Sprint, Mount and the like use the game action directly.");
        ImGui.BulletText("Menu bar: opens the main-menu windows; the leftmost button sends Escape.");
        ImGui.BulletText("Camera: drag on the world like you would with a mouse. The optional camera pad is only for setups where that fails.");

        ImGui.Separator();
        ImGui.TextWrapped("Edit mode lets you drag every control. Hover a control and scroll to resize it. Positions are stored as screen fractions so they survive resolution changes.");
    }

    // ---- widgets --------------------------------------------------------------------------------------

    private static void Bool(string label, bool value, Action<bool> set)
    {
        var v = value;
        if (ImGui.Checkbox(label, ref v))
        {
            set(v);
            Cfg.Save();
        }
    }

    private static void Float(string label, float value, float min, float max, Action<float> set, string format = "%.2f")
    {
        var v = value;
        if (ImGui.SliderFloat(label, ref v, min, max, format))
        {
            set(v);
            Cfg.Save();
        }
    }

    private static void Int(string label, int value, int min, int max, Action<int> set)
    {
        var v = value;
        if (ImGui.SliderInt(label, ref v, min, max))
        {
            set(v);
            Cfg.Save();
        }
    }

    private static void Color(string label, Vector4 value, Action<Vector4> set)
    {
        var v = value;
        if (ImGui.ColorEdit4(label, ref v, ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar))
        {
            set(v);
            Cfg.Save();
        }
    }

    private void KeyPicker(string label, int current, Action<int> set)
    {
        using var combo = ImRaii.Combo(label, ActionButtonWidget.KeyName(current));
        if (!combo.Success) return;

        foreach (var (key, name) in keyNames)
        {
            if (ImGui.Selectable(name, (int)key == current))
            {
                set((int)key);
                Cfg.Save();
            }
        }
    }

    private static void SheetPicker(string label, IReadOnlyList<SheetCache.Entry> entries, int current, Action<int> set)
    {
        var preview = entries.FirstOrDefault(e => e.RowId == (uint)current).Name ?? $"#{current}";
        using var combo = ImRaii.Combo(label, preview);
        if (!combo.Success) return;

        foreach (var e in entries)
        {
            if (ImGui.Selectable($"{e.Name}##{e.RowId}", e.RowId == (uint)current))
            {
                set((int)e.RowId);
                Cfg.Save();
            }
        }
    }
}
