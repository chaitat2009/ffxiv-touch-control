using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using TouchControl.Game;
using TouchControl.Input;
using TouchControl.UI.Widgets;

namespace TouchControl.UI;

/// <summary>
/// Draws every control each frame and makes sure no synthetic key stays pressed when the overlay is not drawn
/// (cutscene, gpose, UI hidden, plugin disabled, logged out).
/// </summary>
public sealed class OverlayRenderer
{
    private readonly GameActions game;
    private readonly KeySender keys;
    private readonly TouchInput touch;
    private readonly JoystickWidget joystick;
    private readonly SkillGroupWidget skills;
    private readonly ActionButtonWidget buttons;
    private readonly MenuBarWidget menuBar;
    private readonly CameraPadWidget cameraPad;

    private int updatesWithoutDraw;

    public OverlayRenderer(GameActions game, KeySender keys, TouchInput touch, SheetCache sheets, Action openSettings, Action hideOverlay)
    {
        this.game = game;
        this.keys = keys;
        this.touch = touch;
        var movement = new MovementController(keys);

        joystick = new JoystickWidget(movement);
        skills = new SkillGroupWidget(game);
        buttons = new ActionButtonWidget(game, keys, sheets, openSettings, hideOverlay);
        menuBar = new MenuBarWidget(game, keys, sheets, openSettings, hideOverlay);
        cameraPad = new CameraPadWidget(game);

        Overlay.Touch = touch;
    }

    public ActionButtonWidget ButtonWidget => buttons;

    /// <summary>UiBuilder.Draw handler.</summary>
    public void Draw()
    {
        var cfg = Plugin.Config;

        if (!cfg.Enabled || !Plugin.ClientState.IsLoggedIn || (cfg.HideWhileTyping && ImGui.GetIO().WantTextInput))
        {
            ReleaseEverything();
            return;
        }

        updatesWithoutDraw = 0;
        Overlay.Opacity = cfg.Opacity;

        var scale = cfg.GlobalScale * ImGuiHelpers.GlobalScale;
        var edit = cfg.EditMode;

        touch.BeginFrame();
        try
        {
            if (cfg.Joystick.Enabled) joystick.Draw(cfg.Joystick, edit, scale);
            else joystick.Release();

            for (var i = 0; i < cfg.SkillGroups.Count; i++)
            {
                var group = cfg.SkillGroups[i];
                if (group.Enabled) skills.Draw(i, group, edit, scale);
            }

            for (var i = 0; i < cfg.Buttons.Count; i++)
            {
                var button = cfg.Buttons[i];
                if (button.Enabled) buttons.Draw(i, button, edit, scale);
            }

            if (cfg.MenuBar.Enabled) menuBar.Draw(cfg.MenuBar, edit, scale);
            if (cfg.CameraPad.Enabled) cameraPad.Draw(cfg.CameraPad, edit);

            if (!edit && cfg.CameraPad.SecondFingerRotates) SecondFingerCamera(cfg.CameraPad);
        }
        finally
        {
            touch.EndFrame();
        }
    }

    /// <summary>
    /// Windows synthesizes mouse input only for the primary contact, so a second finger dragged across the world
    /// does nothing on its own. Rotate the camera for it ourselves. Skipped while the primary finger is itself on
    /// the world (the game is already handling that drag natively) so two fingers never rotate twice.
    /// </summary>
    private void SecondFingerCamera(CameraPadConfig cfg)
    {
        foreach (var p in touch.Pointers)
        {
            if (!p.IsMouse && p.Primary && p.Owner == 0 && p.Down) return;
        }

        foreach (var p in touch.Pointers)
        {
            if (p.IsMouse || p.Primary || p.Owner != 0 || !p.Down || p.Delta == Vector2.Zero) continue;

            var yaw = p.Delta.X * cfg.Sensitivity;
            var pitch = -p.Delta.Y * cfg.Sensitivity * (cfg.InvertY ? -1f : 1f);
            game.RotateCamera(yaw, pitch);
        }
    }

    /// <summary>
    /// IFramework.Update handler. Dalamud stops calling Draw while the UI is hidden, so this is where a stuck
    /// "W" gets released. A couple of frames of slack avoids releasing on a single skipped present.
    /// </summary>
    public void OnFrameworkUpdate()
    {
        if (++updatesWithoutDraw >= 3)
        {
            updatesWithoutDraw = 3;
            ReleaseEverything();
        }
    }

    public void ReleaseEverything()
    {
        joystick.Release();
        keys.ReleaseAll();
        touch.ClearRegions();
    }
}
