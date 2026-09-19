using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using TouchControl.Game;
using TouchControl.Input;

namespace TouchControl.UI.Widgets;

/// <summary>
/// Mirrors a run of hotbar slots as round icon buttons: optionally one big "main attack" button in the middle
/// with the rest fanned out on rings around it (gacha style), or a plain row. Tapping a button executes the
/// hotbar slot through the game, so macros, items, emotes and mounts all work exactly like a hotbar click.
/// </summary>
public sealed class SkillGroupWidget(GameActions game)
{
    private const float Padding = 6f;

    private readonly List<(Vector2 Offset, float Radius)> layout = [];

    public void Draw(int index, SkillGroupConfig cfg, bool editMode, float globalScale)
    {
        var s = globalScale * cfg.Placement.Scale;
        var count = Math.Clamp(cfg.SlotCount, 1, 12);

        ComputeLayout(cfg, count, s);

        // Bounding box of every button, relative to the group centre.
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var (offset, radius) in layout)
        {
            min = Vector2.Min(min, offset - new Vector2(radius));
            max = Vector2.Max(max, offset + new Vector2(radius));
        }

        min -= new Vector2(Padding);
        max += new Vector2(Padding);

        var center = Overlay.ToScreen(cfg.Placement.Center);
        var topLeft = center + min;
        var size = max - min;

        if (!Overlay.BeginControl($"TouchControl##skills{index}", topLeft, size))
        {
            ImGui.End();
            return;
        }

        try
        {
            var edit = Overlay.EditHandle($"skills{index}", cfg.Placement, topLeft, size, $"Hotbar {cfg.Hotbar + 1}", editMode);

            for (var i = 0; i < count; i++)
            {
                var slotIndex = cfg.FirstSlot + i;
                if (slotIndex > 15) break;

                var slot = game.ReadSlot(cfg.Hotbar, slotIndex);
                var (offset, radius) = layout[i];
                var pos = center + offset;

                if (!slot.Valid || (slot.Empty && cfg.HideEmptySlots && !edit))
                    continue;

                var icon = slot.Empty ? null : Overlay.Icon(slot.IconId);
                var label = slot.Empty ? $"{slotIndex + 1}" : string.Empty;
                var fill = Overlay.Col(0.08f, 0.08f, 0.1f, 0.65f);

                var state = Overlay.CircleButton(TouchInput.Key($"skills{index}", i), pos, radius, fill, icon, label, interactive: !edit && !slot.Empty);

                if (state.Pressed)
                    game.ExecuteSlot(cfg.Hotbar, slotIndex);

                if (cfg.ShowCooldown && game.TryGetCooldown(slot, out var remaining, out var total))
                    Overlay.CooldownOverlay(pos, radius * 0.92f, remaining, total);

                if (cfg.ShowKeybind && !string.IsNullOrEmpty(slot.Keybind))
                {
                    var textPos = pos + new Vector2(-radius * 0.7f, -radius * 0.95f);
                    var dl = ImGui.GetWindowDrawList();
                    dl.AddText(textPos + new Vector2(1, 1), Overlay.Col(0f, 0f, 0f, 1f), slot.Keybind);
                    dl.AddText(textPos, Overlay.Col(1f, 1f, 1f, 0.9f), slot.Keybind);
                }
            }
        }
        finally
        {
            ImGui.End();
        }
    }

    private void ComputeLayout(SkillGroupConfig cfg, int count, float s)
    {
        layout.Clear();

        var buttonR = cfg.ButtonRadius * s;

        if (cfg.RowLayout)
        {
            var step = buttonR * 2f + cfg.RowSpacing * s;
            for (var i = 0; i < count; i++)
            {
                var x = (i - (count - 1) / 2f) * step;
                layout.Add((new Vector2(x, 0f), buttonR));
            }

            return;
        }

        var ringStart = 0;
        if (cfg.CenterFirstSlot)
        {
            layout.Add((Vector2.Zero, cfg.CenterRadius * s));
            ringStart = 1;
        }

        var onRings = count - ringStart;
        var capacity = Math.Max(1, cfg.RingCapacity);
        var startRad = cfg.ArcStartDeg * MathF.PI / 180f;
        var endRad = cfg.ArcEndDeg * MathF.PI / 180f;
        var fullCircle = MathF.Abs(MathF.Abs(endRad - startRad) - MathF.PI * 2f) < 0.01f;

        for (var i = 0; i < onRings; i++)
        {
            var ring = i / capacity;
            var indexInRing = i % capacity;
            var inThisRing = Math.Min(capacity, onRings - ring * capacity);

            // Spread evenly along the arc. On a full circle the last button must not land on the first.
            float t;
            if (inThisRing == 1) t = 0.5f;
            else if (fullCircle) t = (float)indexInRing / inThisRing;
            else t = (float)indexInRing / (inThisRing - 1);

            var angle = startRad + (endRad - startRad) * t;
            var radius = (cfg.RingRadius + ring * cfg.RingSpacing) * s;

            // 0 = up, clockwise.
            var offset = new Vector2(MathF.Sin(angle), -MathF.Cos(angle)) * radius;
            layout.Add((offset, buttonR));
        }
    }
}
