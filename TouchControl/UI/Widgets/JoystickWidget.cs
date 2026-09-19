using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TouchControl.Input;

namespace TouchControl.UI.Widgets;

/// <summary>
/// Virtual left stick. The touch zone is a square window; the drawn base either sits in the middle or, in
/// floating mode, appears wherever the finger lands. Knob offset / base radius is fed to the movement controller.
/// The finger that lands on the zone owns the stick until it lifts, independent of any other finger.
/// </summary>
public sealed class JoystickWidget(MovementController movement)
{
    private static readonly int RegionKey = TouchInput.Key("joystick");

    private bool active;
    private Vector2 origin;
    private Vector2 knob;

    public void Draw(JoystickConfig cfg, bool editMode, float globalScale)
    {
        var s = globalScale * cfg.Placement.Scale;
        var zone = cfg.ZoneRadius * s;
        var baseR = cfg.BaseRadius * s;
        var knobR = cfg.KnobRadius * s;

        var size = new Vector2(zone * 2f, zone * 2f);
        var center = Overlay.ToScreen(cfg.Placement.Center);
        var topLeft = center - size / 2f;

        if (!Overlay.BeginControl("TouchControl##joystick", topLeft, size))
        {
            ImGui.End();
            Release();
            return;
        }

        try
        {
            if (Overlay.EditHandle("joystick", cfg.Placement, topLeft, size, "Joystick", editMode))
            {
                Release();
                DrawStick(cfg, center, center, baseR, knobR, false);
                return;
            }

            Overlay.Touch.AddRegion(RegionKey, topLeft, topLeft + size);
            var owner = Overlay.Touch.Owner(RegionKey);

            if (owner != null && owner.Pressed)
            {
                active = true;
                if (cfg.Floating)
                {
                    // Keep the whole base (plus the knob overhang) inside the window so it never gets clipped.
                    var margin = baseR + knobR;
                    var min = topLeft + new Vector2(margin, margin);
                    var max = topLeft + size - new Vector2(margin, margin);
                    origin = max.X > min.X && max.Y > min.Y ? Vector2.Clamp(owner.Pos, min, max) : center;
                }
                else
                {
                    origin = center;
                }
            }

            if (active && owner != null && owner.Down)
            {
                var delta = owner.Pos - origin;
                var len = delta.Length();
                if (len > baseR) delta *= baseR / len;

                knob = origin + delta;
                movement.Update(delta / baseR, cfg);
            }
            else if (active)
            {
                Release();
            }

            var baseCenter = active ? origin : center;
            DrawStick(cfg, baseCenter, active ? knob : baseCenter, baseR, knobR, active);
        }
        finally
        {
            ImGui.End();
        }
    }

    /// <summary>Called whenever the joystick is not being drawn, so movement never sticks.</summary>
    public void Release()
    {
        active = false;
        movement.Stop();
    }

    private static void DrawStick(JoystickConfig cfg, Vector2 baseCenter, Vector2 knobCenter, float baseR, float knobR, bool engaged)
    {
        var dl = ImGui.GetWindowDrawList();

        dl.AddCircleFilled(baseCenter, baseR, Overlay.Col(cfg.BaseColor), 64);
        dl.AddCircle(baseCenter, baseR, Overlay.Col(cfg.RingColor), 64, engaged ? 2.5f : 1.5f);
        dl.AddCircle(baseCenter, baseR * cfg.DeadZone, Overlay.Col(cfg.RingColor with { W = cfg.RingColor.W * 0.4f }), 32, 1f);

        // Four direction ticks so the stick reads as a d-pad at a glance.
        var tick = Overlay.Col(cfg.RingColor with { W = cfg.RingColor.W * 0.8f });
        var inner = baseR * 0.72f;
        var arrow = baseR * 0.09f;
        for (var i = 0; i < 4; i++)
        {
            var a = i * MathF.PI / 2f;
            var dir = new Vector2(MathF.Sin(a), -MathF.Cos(a));
            var side = new Vector2(dir.Y, -dir.X);
            var tip = baseCenter + dir * (inner + arrow);
            dl.AddTriangleFilled(tip, baseCenter + dir * inner + side * arrow, baseCenter + dir * inner - side * arrow, tick);
        }

        dl.AddCircleFilled(knobCenter, knobR, Overlay.Col(cfg.KnobColor), 48);
        dl.AddCircle(knobCenter, knobR, Overlay.Col(0f, 0f, 0f, 0.35f), 48, 1.5f);
    }
}
