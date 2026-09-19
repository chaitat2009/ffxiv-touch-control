using System.Collections.Generic;
using System.Numerics;

namespace TouchControl.Input;

/// <summary>
/// Turns the analog joystick vector into the 8-way WASD key set the game understands.
/// FFXIV has no analog keyboard movement, so a 45-degree sector per direction is the best we can do;
/// with the "Legacy" movement type the diagonals become camera-relative just like a gacha game stick.
/// Only touches the keys it pressed itself, so a held Jump button elsewhere is left alone.
/// </summary>
public sealed class MovementController(KeySender keys)
{
    // sin(22.5 deg): an axis is engaged once the stick is within 67.5 deg of it, giving eight equal sectors.
    private const float SectorThreshold = 0.38268343f;

    private readonly HashSet<int> current = [];
    private readonly HashSet<int> wanted = [];

    public bool IsMoving => current.Count > 0;

    /// <summary>
    /// <paramref name="stick"/> is the knob offset in screen space (y down) divided by the base radius, so |stick| is 0..1.
    /// </summary>
    public void Update(Vector2 stick, JoystickConfig cfg)
    {
        wanted.Clear();

        var magnitude = stick.Length();
        if (magnitude >= cfg.DeadZone)
        {
            var dir = stick / magnitude;
            var right = dir.X;
            var forward = -dir.Y;

            if (forward >= SectorThreshold) wanted.Add(cfg.ForwardKey);
            else if (forward <= -SectorThreshold) wanted.Add(cfg.BackKey);

            if (right >= SectorThreshold) wanted.Add(cfg.RightKey);
            else if (right <= -SectorThreshold) wanted.Add(cfg.LeftKey);
        }

        Apply();
    }

    public void Stop()
    {
        if (current.Count == 0) return;
        wanted.Clear();
        Apply();
    }

    private void Apply()
    {
        // Release first so W->S never has both held for a frame.
        foreach (var vk in new List<int>(current))
        {
            if (!wanted.Contains(vk))
            {
                keys.Up(vk);
                current.Remove(vk);
            }
        }

        foreach (var vk in wanted)
        {
            if (current.Add(vk)) keys.Down(vk);
        }
    }
}
