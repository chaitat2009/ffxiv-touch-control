using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;

namespace TouchControl.UI;

/// <summary>
/// Shared drawing plumbing for every on-screen control: borderless ImGui windows that only capture input inside
/// their own rectangle (so the game UI underneath stays clickable), circular icon buttons, and the edit-mode
/// drag / wheel-resize handle.
/// </summary>
public static class Overlay
{
    public const ImGuiWindowFlags ControlFlags =
        ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav |
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoBringToFrontOnFocus |
        ImGuiWindowFlags.NoDocking;

    /// <summary>Alpha multiplier applied to every colour drawn this frame (Configuration.Opacity).</summary>
    public static float Opacity = 1f;

    public static Vector2 DisplaySize => ImGui.GetIO().DisplaySize;

    public static uint Col(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c with { W = c.W * Opacity });

    public static uint Col(float r, float g, float b, float a) => Col(new Vector4(r, g, b, a));

    public static Vector2 ToScreen(Vector2 normalized) => normalized * DisplaySize;

    /// <summary>
    /// Opens a borderless window at the given screen rectangle. Always pair with <see cref="ImGui.End"/>.
    /// </summary>
    public static bool BeginControl(string id, Vector2 topLeft, Vector2 size)
    {
        ImGui.SetNextWindowPos(topLeft, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, Vector2.One);
        var open = ImGui.Begin(id, ControlFlags);
        ImGui.PopStyleVar(3);
        return open;
    }

    /// <summary>
    /// Edit-mode handle covering the whole current window. Dragging moves the placement, the mouse wheel rescales it.
    /// Returns true when the caller should skip its normal interaction because edit mode is on.
    /// </summary>
    public static bool EditHandle(Placement placement, Vector2 topLeft, Vector2 size, string label, bool editMode, bool allowScale = true)
    {
        if (!editMode) return false;

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(topLeft, topLeft + size, Col(0.2f, 0.6f, 1f, 0.12f), 6f);
        dl.AddRect(topLeft, topLeft + size, Col(0.3f, 0.7f, 1f, 0.9f), 6f, ImDrawFlags.RoundCornersAll, 2f);

        var textSize = ImGui.CalcTextSize(label);
        var textPos = new Vector2(topLeft.X + (size.X - textSize.X) / 2f, topLeft.Y + 4f);
        dl.AddRectFilled(textPos - new Vector2(4, 2), textPos + textSize + new Vector2(4, 2), Col(0f, 0f, 0f, 0.7f), 3f);
        dl.AddText(textPos, Col(1f, 1f, 1f, 1f), label);

        ImGui.SetCursorScreenPos(topLeft);
        ImGui.InvisibleButton("##edit", size);

        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta = ImGui.GetIO().MouseDelta / DisplaySize;
            placement.Center = Vector2.Clamp(placement.Center + delta, Vector2.Zero, Vector2.One);
        }

        if (allowScale && ImGui.IsItemHovered())
        {
            var wheel = ImGui.GetIO().MouseWheel;
            if (wheel != 0)
            {
                placement.Scale = Math.Clamp(placement.Scale + wheel * 0.05f, 0.4f, 3f);
                Plugin.Config.Save();
            }
        }

        if (ImGui.IsItemDeactivated()) Plugin.Config.Save();

        return true;
    }

    public static IDalamudTextureWrap? Icon(uint iconId)
    {
        if (iconId == 0) return null;
        return Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
    }

    public readonly struct ButtonState
    {
        public readonly bool Hovered;
        public readonly bool Held;
        public readonly bool Pressed;
        public readonly bool Released;

        public ButtonState(bool hovered, bool held, bool pressed, bool released)
        {
            Hovered = hovered;
            Held = held;
            Pressed = pressed;
            Released = released;
        }
    }

    /// <summary>
    /// A round touch button. Executes on press (IsItemActivated) rather than release, because that is what
    /// mobile games do and it makes the controls feel snappy. Text is drawn inside when there is no icon.
    /// </summary>
    public static ButtonState CircleButton(string id, Vector2 center, float radius, uint fill, IDalamudTextureWrap? icon, string label, bool interactive = true)
    {
        var dl = ImGui.GetWindowDrawList();
        var diameter = radius * 2f;

        ImGui.SetCursorScreenPos(center - new Vector2(radius, radius));
        ImGui.InvisibleButton(id, new Vector2(diameter, diameter));

        var hovered = interactive && ImGui.IsItemHovered();
        var held = interactive && ImGui.IsItemActive();
        var pressed = interactive && ImGui.IsItemActivated();
        var released = interactive && ImGui.IsItemDeactivated();

        // Slight press feedback: shrink and brighten.
        var drawRadius = held ? radius * 0.92f : radius;

        dl.AddCircleFilled(center, drawRadius, fill, 48);

        if (icon != null)
        {
            var half = new Vector2(drawRadius, drawRadius) * 0.92f;
            dl.AddImageRounded(icon.Handle, center - half, center + half, Vector2.Zero, Vector2.One,
                Col(1f, 1f, 1f, 1f), drawRadius * 0.92f, ImDrawFlags.RoundCornersAll);
        }
        else if (!string.IsNullOrEmpty(label))
        {
            var textSize = ImGui.CalcTextSize(label);
            dl.AddText(center - textSize / 2f, Col(1f, 1f, 1f, 1f), label);
        }

        var ring = held ? Col(1f, 1f, 1f, 0.95f) : hovered ? Col(1f, 1f, 1f, 0.7f) : Col(1f, 1f, 1f, 0.35f);
        dl.AddCircle(center, drawRadius, ring, 48, held ? 3f : 1.5f);

        return new ButtonState(hovered, held, pressed, released);
    }

    /// <summary>Draw a FontAwesome glyph centred on a point.</summary>
    public static void IconGlyph(Vector2 center, FontAwesomeIcon glyph, uint color)
    {
        var text = glyph.ToIconString();
        ImGui.PushFont(UiBuilder.IconFont);
        var size = ImGui.CalcTextSize(text);
        ImGui.GetWindowDrawList().AddText(center - size / 2f, color, text);
        ImGui.PopFont();
    }

    /// <summary>Dark pie wedge + seconds text for a running cooldown.</summary>
    public static void CooldownOverlay(Vector2 center, float radius, float remaining, float total)
    {
        if (total <= 0 || remaining <= 0) return;

        var dl = ImGui.GetWindowDrawList();
        var fraction = Math.Clamp(remaining / total, 0f, 1f);

        const float top = -MathF.PI / 2f;
        var sweep = fraction * MathF.PI * 2f;

        dl.PathLineTo(center);
        dl.PathArcTo(center, radius, top, top + sweep, 40);
        dl.PathFillConvex(Col(0f, 0f, 0f, 0.6f));

        var text = remaining >= 10f ? MathF.Ceiling(remaining).ToString("0") : remaining.ToString("0.0");
        var size = ImGui.CalcTextSize(text);
        var pos = center - size / 2f;
        dl.AddText(pos + new Vector2(1, 1), Col(0f, 0f, 0f, 1f), text);
        dl.AddText(pos, Col(1f, 1f, 1f, 1f), text);
    }
}
