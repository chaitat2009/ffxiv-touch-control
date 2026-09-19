using System.Numerics;
using Dalamud.Bindings.ImGui;
using TouchControl.Game;

namespace TouchControl.UI.Widgets;

/// <summary>
/// Optional invisible drag zone that rotates the camera, for setups where the game does not get a usable
/// left-drag from the touch layer. Off by default because a normal touch drag on the world already orbits
/// the camera in FFXIV.
/// </summary>
public sealed class CameraPadWidget(GameActions game)
{
    public void Draw(CameraPadConfig cfg, bool editMode)
    {
        var display = Overlay.DisplaySize;
        var topLeft = new Vector2(cfg.Rect.X, cfg.Rect.Y) * display;
        var size = new Vector2(cfg.Rect.Z, cfg.Rect.W) * display;
        if (size.X < 10 || size.Y < 10) return;

        if (!Overlay.BeginControl("TouchControl##camerapad", topLeft, size))
        {
            ImGui.End();
            return;
        }

        try
        {
            var dl = ImGui.GetWindowDrawList();

            if (editMode)
            {
                dl.AddRectFilled(topLeft, topLeft + size, Overlay.Col(1f, 0.6f, 0.2f, 0.12f), 6f);
                dl.AddRect(topLeft, topLeft + size, Overlay.Col(1f, 0.7f, 0.3f, 0.9f), 6f, ImDrawFlags.RoundCornersAll, 2f);
                var label = "Camera pad (resize in settings)";
                var textSize = ImGui.CalcTextSize(label);
                dl.AddText(topLeft + new Vector2((size.X - textSize.X) / 2f, 4f), Overlay.Col(1f, 1f, 1f, 1f), label);

                ImGui.SetCursorScreenPos(topLeft);
                ImGui.InvisibleButton("##edit", size);
                if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
                {
                    var delta = ImGui.GetIO().MouseDelta / display;
                    cfg.Rect = cfg.Rect with { X = cfg.Rect.X + delta.X, Y = cfg.Rect.Y + delta.Y };
                }

                if (ImGui.IsItemDeactivated()) Plugin.Config.Save();
                return;
            }

            if (cfg.ShowOutline)
                dl.AddRect(topLeft, topLeft + size, Overlay.Col(1f, 1f, 1f, 0.12f), 6f, ImDrawFlags.RoundCornersAll, 1f);

            ImGui.SetCursorScreenPos(topLeft);
            ImGui.InvisibleButton("##pad", size);

            if (ImGui.IsItemActive())
            {
                var delta = ImGui.GetIO().MouseDelta;
                if (delta != Vector2.Zero)
                {
                    var yaw = delta.X * cfg.Sensitivity;
                    var pitch = -delta.Y * cfg.Sensitivity * (cfg.InvertY ? -1f : 1f);
                    game.RotateCamera(yaw, pitch);
                }
            }
        }
        finally
        {
            ImGui.End();
        }
    }
}
