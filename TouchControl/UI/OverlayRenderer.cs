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

        if (!Plugin.ClientState.IsLoggedIn || (cfg.HideWhileTyping && ImGui.GetIO().WantTextInput))
        {
            ReleaseEverything();
            return;
        }

        updatesWithoutDraw = 0;

        // With a phone connected the phone is the control surface; drawing the same controls in-game only clutters.
        if (cfg.Bridge.HideOverlayWhileConnected && Plugin.Instance?.Bridge.ClientCount > 0)
        {
            joystick.Release();
            touch.ClearRegions();
            return;
        }

        if (!cfg.Enabled)
        {
            // Release keys but keep the pointer regions alive: the restore button registers its own each frame.
            joystick.Release();
            keys.ReleaseAll();
            if (cfg.ShowRestoreButton) DrawRestoreButton(cfg);
            else touch.ClearRegions();
            return;
        }

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
    /// The one control that survives hiding: a faint eye in the corner that turns the overlay back on, so a
    /// touchscreen user is never stuck having to type /touch on. Draggable in edit mode like everything else.
    /// </summary>
    private void DrawRestoreButton(Configuration cfg)
    {
        var radius = cfg.RestoreButtonRadius * cfg.GlobalScale * ImGuiHelpers.GlobalScale * cfg.RestoreButton.Scale;
        var size = new Vector2((radius + 4f) * 2f);
        var center = Overlay.ToScreen(cfg.RestoreButton.Center);
        var topLeft = center - size / 2f;

        Overlay.Opacity = Math.Max(0.35f, cfg.Opacity * 0.6f);

        if (!Overlay.BeginControl("TouchControl##restore", topLeft, size))
        {
            ImGui.End();
            return;
        }

        touch.BeginFrame();
        try
        {
            var edit = Overlay.EditHandle("restore", cfg.RestoreButton, topLeft, size, "Show", cfg.EditMode);
            var state = Overlay.CircleButton(TouchInput.Key("restore"), center, radius, Overlay.Col(0.08f, 0.08f, 0.1f, 0.6f), null, string.Empty, !edit);
            Overlay.IconGlyph(center, Dalamud.Interface.FontAwesomeIcon.Eye, Overlay.Col(1f, 1f, 1f, 0.9f));

            if (state.Pressed && !edit)
            {
                cfg.Enabled = true;
                cfg.Save();
            }
        }
        finally
        {
            ImGui.End();
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
