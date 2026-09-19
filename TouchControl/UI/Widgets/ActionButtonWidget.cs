using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using TouchControl.Game;
using TouchControl.Input;

namespace TouchControl.UI.Widgets;

/// <summary>
/// Free-floating round buttons: Jump, Sprint, Mount, Target, Interact, Auto-run and anything else the user adds.
/// Key buttons are held for as long as they are touched (so a long-press Jump still jumps high); everything
/// else fires on press.
/// </summary>
public sealed class ActionButtonWidget(GameActions game, KeySender keys, SheetCache sheets, Action openSettings, Action hideOverlay)
{
    private const float Padding = 4f;

    public void Draw(int index, ActionButtonConfig cfg, bool editMode, float globalScale)
    {
        var s = globalScale * cfg.Placement.Scale;
        var radius = cfg.Radius * s;

        var size = new Vector2((radius + Padding) * 2f);
        var center = Overlay.ToScreen(cfg.Placement.Center);
        var topLeft = center - size / 2f;

        if (!Overlay.BeginControl($"TouchControl##button{index}", topLeft, size))
        {
            ImGui.End();
            return;
        }

        try
        {
            Resolve(cfg, out var label, out var icon, out var glyph);

            var edit = Overlay.EditHandle(cfg.Placement, topLeft, size, label, editMode);
            var state = Overlay.CircleButton("##btn", center, radius, Overlay.Col(cfg.Color), icon, glyph == null ? label : string.Empty, interactive: !edit);

            if (glyph != null)
                Overlay.IconGlyph(center, glyph.Value, Overlay.Col(1f, 1f, 1f, 0.95f));

            if (cfg.Kind == ButtonKind.GeneralAction && cfg.Value > 0)
            {
                var pseudo = new GameActions.SlotInfo(true, false, 0, RaptureHotbarModule.HotbarSlotType.GeneralAction, (uint)cfg.Value, string.Empty);
                if (game.TryGetCooldown(pseudo, out var remaining, out var total))
                    Overlay.CooldownOverlay(center, radius * 0.92f, remaining, total);
            }

            if (edit) return;

            switch (cfg.Kind)
            {
                case ButtonKind.Key:
                    if (state.Pressed) keys.Down(cfg.Value);
                    if (state.Released) keys.Up(cfg.Value);
                    break;
                case ButtonKind.GeneralAction:
                    if (state.Pressed && cfg.Value > 0) game.UseGeneralAction((uint)cfg.Value);
                    break;
                case ButtonKind.MainCommand:
                    if (state.Pressed && cfg.Value > 0) game.ExecuteMainCommand((uint)cfg.Value);
                    break;
                case ButtonKind.MountToggle:
                    if (state.Pressed) game.ToggleMount();
                    break;
                case ButtonKind.OpenSettings:
                    if (state.Pressed) openSettings();
                    break;
                case ButtonKind.HideOverlay:
                    if (state.Pressed) hideOverlay();
                    break;
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    /// <summary>Work out what to draw on the button from its kind and value.</summary>
    public void Resolve(ActionButtonConfig cfg, out string label, out IDalamudTextureWrap? icon, out FontAwesomeIcon? glyph)
    {
        icon = null;
        glyph = null;
        label = cfg.Label;

        switch (cfg.Kind)
        {
            case ButtonKind.Key:
                if (string.IsNullOrEmpty(label)) label = KeyName(cfg.Value);
                break;

            case ButtonKind.GeneralAction:
            {
                var entry = sheets.GeneralAction((uint)cfg.Value);
                if (entry != null)
                {
                    icon = Overlay.Icon(entry.Value.IconId);
                    if (string.IsNullOrEmpty(label)) label = entry.Value.Name;
                }
                else if (string.IsNullOrEmpty(label)) label = $"GA {cfg.Value}";
                break;
            }

            case ButtonKind.MainCommand:
            {
                var entry = sheets.MainCommand((uint)cfg.Value);
                if (entry != null)
                {
                    icon = Overlay.Icon(entry.Value.IconId);
                    if (string.IsNullOrEmpty(label)) label = entry.Value.Name;
                }
                else if (string.IsNullOrEmpty(label)) label = $"MC {cfg.Value}";
                break;
            }

            case ButtonKind.MountToggle:
            {
                var id = game.IsMounted ? GameActions.GeneralActionDismount : GameActions.GeneralActionMountRoulette;
                var entry = sheets.GeneralAction(id);
                if (entry != null)
                {
                    icon = Overlay.Icon(entry.Value.IconId);
                    if (string.IsNullOrEmpty(label)) label = entry.Value.Name;
                }
                else if (string.IsNullOrEmpty(label)) label = "Mount";
                break;
            }

            case ButtonKind.OpenSettings:
                glyph = FontAwesomeIcon.Cog;
                if (string.IsNullOrEmpty(label)) label = "Settings";
                break;

            case ButtonKind.HideOverlay:
                glyph = FontAwesomeIcon.EyeSlash;
                if (string.IsNullOrEmpty(label)) label = "Hide";
                break;
        }
    }

    public static string KeyName(int vk)
    {
        var name = Enum.GetName((VirtualKey)vk);
        if (name == null) return $"VK {vk}";
        return name.Replace("KEY_", string.Empty).Replace("NUMPAD", "Num");
    }
}
