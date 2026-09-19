using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using TouchControl.Game;
using TouchControl.Input;

namespace TouchControl.UI.Widgets;

/// <summary>
/// The strip of small round buttons along the top edge: back/escape, the main-menu windows (Character, Inventory,
/// Map, ...), then the plugin settings and hide toggles. Mirrors the top bar of mobile action RPGs.
/// </summary>
public sealed class MenuBarWidget(GameActions game, KeySender keys, SheetCache sheets, Action openSettings, Action hideOverlay)
{
    private const float Padding = 4f;

    public void Draw(MenuBarConfig cfg, bool editMode, float globalScale)
    {
        var s = globalScale * cfg.Placement.Scale;
        var radius = cfg.ButtonRadius * s;
        var step = radius * 2f + cfg.Spacing * s;

        var buttonCount = cfg.MainCommands.Count
                          + (cfg.ShowEscapeButton ? 1 : 0)
                          + (cfg.ShowSettingsButton ? 1 : 0)
                          + (cfg.ShowHideButton ? 1 : 0);
        if (buttonCount == 0) return;

        var size = new Vector2(step * buttonCount - cfg.Spacing * s + Padding * 2f, radius * 2f + Padding * 2f);
        var center = Overlay.ToScreen(cfg.Placement.Center);
        var topLeft = center - size / 2f;

        if (!Overlay.BeginControl("TouchControl##menubar", topLeft, size))
        {
            ImGui.End();
            return;
        }

        try
        {
            var edit = Overlay.EditHandle(cfg.Placement, topLeft, size, "Menu bar", editMode);
            var fill = Overlay.Col(0.08f, 0.08f, 0.1f, 0.7f);
            var glyphColor = Overlay.Col(1f, 1f, 1f, 0.95f);

            var x = topLeft.X + Padding + radius;
            var y = center.Y;

            if (cfg.ShowEscapeButton)
            {
                var state = Overlay.CircleButton("##esc", new Vector2(x, y), radius, fill, null, string.Empty, !edit);
                Overlay.IconGlyph(new Vector2(x, y), FontAwesomeIcon.Bars, glyphColor);
                if (state.Pressed && !edit) keys.Tap((int)VirtualKey.ESCAPE);
                x += step;
            }

            for (var i = 0; i < cfg.MainCommands.Count; i++)
            {
                var id = (uint)cfg.MainCommands[i];
                var entry = sheets.MainCommand(id);
                var icon = entry != null ? Overlay.Icon(entry.Value.IconId) : null;
                var label = entry?.Name ?? $"MC {id}";

                var state = Overlay.CircleButton($"##mc{i}", new Vector2(x, y), radius, fill, icon, icon == null ? Abbrev(label) : string.Empty, !edit);
                if (!edit && state.Hovered) ImGui.SetTooltip(label);
                if (state.Pressed && !edit) game.ExecuteMainCommand(id);
                x += step;
            }

            if (cfg.ShowSettingsButton)
            {
                var state = Overlay.CircleButton("##cfg", new Vector2(x, y), radius, fill, null, string.Empty, !edit);
                Overlay.IconGlyph(new Vector2(x, y), FontAwesomeIcon.Cog, glyphColor);
                if (state.Pressed && !edit) openSettings();
                x += step;
            }

            if (cfg.ShowHideButton)
            {
                var state = Overlay.CircleButton("##hide", new Vector2(x, y), radius, fill, null, string.Empty, !edit);
                Overlay.IconGlyph(new Vector2(x, y), FontAwesomeIcon.EyeSlash, glyphColor);
                if (state.Pressed && !edit) hideOverlay();
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    private static string Abbrev(string name) => name.Length <= 3 ? name : name[..3];
}
