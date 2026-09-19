using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Bindings.ImGui;

namespace TouchControl.Input;

/// <summary>
/// Multi-touch for the overlay.
///
/// Windows turns only the *primary* touch contact into mouse input, so anything driven by the mouse (the game,
/// ImGui) is single-finger by construction. Every contact does however produce its own WM_POINTER* message
/// stream. We subclass the game window, read those messages, and drive the overlay controls from them:
///
///  * a touch that lands on one of our controls is swallowed (never reaches DefWindowProc, so no mouse is
///    synthesized and the game never sees it) and that control owns the contact until it lifts;
///  * a touch that lands anywhere else is passed through untouched, so tapping game UI and camera-dragging on
///    the world keep working exactly as before;
///  * the ordinary mouse (ImGui's pointer) is exposed as one more pointer so the overlay still works with a
///    mouse, on Windows without touch, or if the subclass could not be installed.
///
/// Controls register hit regions while they draw; the WndProc uses the previous frame's regions to decide
/// ownership at WM_POINTERDOWN time. Everything runs on the game's main thread (message pump and Present),
/// the lock is just belt and braces.
/// </summary>
public sealed class TouchInput : IDisposable
{
    public const uint MouseId = 0xFFFF_FFFF;

    public sealed class Pointer
    {
        public uint Id;
        public Vector2 Pos;
        public Vector2 Delta;
        public bool Primary;
        public bool IsMouse;
        public bool Down;
        public bool Pressed;
        public bool Released;

        /// <summary>Region key that owns this contact, 0 when the touch went to the game.</summary>
        public int Owner;

        internal Vector2 LastFramePos;
        internal bool Seen;

        /// <summary>Set once a frame has observed Released, so EndFrame only retires contacts the controls saw lift.</summary>
        internal bool ReleaseReported;
    }

    private readonly struct Region(int key, Vector2 min, Vector2 max, bool circle, Vector2 center, float radius)
    {
        public readonly int Key = key;
        public readonly Vector2 Min = min;
        public readonly Vector2 Max = max;
        public readonly bool Circle = circle;
        public readonly Vector2 Center = center;
        public readonly float Radius = radius;

        public bool Contains(Vector2 p)
        {
            if (p.X < Min.X || p.Y < Min.Y || p.X > Max.X || p.Y > Max.Y) return false;
            return !Circle || Vector2.DistanceSquared(p, Center) <= Radius * Radius;
        }
    }

    private readonly object gate = new();
    private readonly Dictionary<uint, Pointer> pointers = [];
    private readonly List<Pointer> frame = [];
    private readonly List<Region> building = [];
    private Region[] regions = [];
    private readonly Pointer mouse = new() { Id = MouseId, IsMouse = true, Primary = true };

    private SubclassProc? proc;
    private nint hwnd;

    public bool Installed { get; private set; }
    public string? InstallError { get; private set; }

    /// <summary>Pointers alive during the current frame (snapshot taken in <see cref="BeginFrame"/>).</summary>
    public IReadOnlyList<Pointer> Pointers => frame;

    /// <summary>Number of live touch contacts (excluding the mouse). Handy for the settings window.</summary>
    public int TouchCount
    {
        get
        {
            lock (gate) return pointers.Count;
        }
    }

    // ---- install ----------------------------------------------------------------------------------------

    /// <summary>Must run on the thread that owns the game window (the framework thread).</summary>
    public void TryInstall(nint gameWindow)
    {
        if (Installed || gameWindow == 0) return;

        try
        {
            proc = WndProc;
            if (!SetWindowSubclass(gameWindow, proc, SubclassId, 0))
            {
                InstallError = $"SetWindowSubclass failed (error {Marshal.GetLastWin32Error()})";
                Plugin.Log.Warning("Touch input unavailable: {Err}", InstallError);
                proc = null;
                return;
            }

            hwnd = gameWindow;
            Installed = true;
            Plugin.Log.Information("Multi-touch input installed on window 0x{Hwnd:X}", gameWindow);
        }
        catch (Exception ex)
        {
            InstallError = ex.Message;
            Plugin.Log.Error(ex, "Touch input install failed");
        }
    }

    public void Dispose()
    {
        if (!Installed || proc == null) return;

        // The subclass must be removed from the window's own thread; if we are already there this runs inline.
        try
        {
            Plugin.Framework.RunOnFrameworkThread(() =>
            {
                RemoveWindowSubclass(hwnd, proc, SubclassId);
            }).Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to remove touch subclass");
        }

        Installed = false;
        proc = null;
    }

    // ---- WndProc ----------------------------------------------------------------------------------------

    private nint WndProc(nint hWnd, uint msg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData)
    {
        try
        {
            switch (msg)
            {
                case WM_POINTERDOWN:
                case WM_POINTERUPDATE:
                case WM_POINTERUP:
                case WM_POINTERCAPTURECHANGED:
                case WM_POINTERLEAVE:
                    if (HandlePointer(hWnd, msg, wParam, lParam)) return 0;
                    break;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Touch WndProc failed");
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    /// <summary>Returns true when the message belongs to a contact we own and must not reach the game.</summary>
    private bool HandlePointer(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        var id = (uint)(wParam & 0xFFFF);
        var flags = (uint)((wParam >> 16) & 0xFFFF);

        // Only real touch / pen contacts. If mouse-in-pointer is ever enabled, the mouse keeps its normal path.
        if (!GetPointerType(id, out var type) || type == PT_MOUSE) return false;

        var pt = new POINT { X = (short)(lParam & 0xFFFF), Y = (short)((lParam >> 16) & 0xFFFF) };
        ScreenToClient(hWnd, ref pt);
        var pos = new Vector2(pt.X, pt.Y);

        lock (gate)
        {
            switch (msg)
            {
                case WM_POINTERDOWN:
                {
                    var owner = HitTest(pos);
                    var p = new Pointer
                    {
                        Id = id,
                        Pos = pos,
                        LastFramePos = pos,
                        Primary = (flags & POINTER_MESSAGE_FLAG_PRIMARY) != 0,
                        Down = true,
                        Pressed = true,
                        Owner = owner,
                    };
                    pointers[id] = p;
                    return owner != 0;
                }

                case WM_POINTERUPDATE:
                {
                    if (!pointers.TryGetValue(id, out var p)) return false;
                    p.Pos = pos;
                    return p.Owner != 0;
                }

                default: // UP, CAPTURECHANGED, LEAVE
                {
                    if (!pointers.TryGetValue(id, out var p)) return false;
                    p.Pos = pos;
                    p.Down = false;
                    p.Released = true;
                    return p.Owner != 0;
                }
            }
        }
    }

    private int HitTest(Vector2 pos)
    {
        // Later regions are drawn on top, so they win.
        var regs = regions;
        for (var i = regs.Length - 1; i >= 0; i--)
        {
            if (regs[i].Contains(pos)) return regs[i].Key;
        }

        return 0;
    }

    // ---- per-frame API (draw thread) --------------------------------------------------------------------

    /// <summary>Snapshot the pointer table for this frame and fold in ImGui's mouse as a pointer.</summary>
    public void BeginFrame()
    {
        frame.Clear();

        lock (gate)
        {
            foreach (var p in pointers.Values)
            {
                // Pressed is a one-frame flag: only the first frame that sees a contact reports it.
                if (p.Seen) p.Pressed = false;
                if (p.Released) p.ReleaseReported = true;

                p.Delta = p.Pos - p.LastFramePos;
                p.LastFramePos = p.Pos;
                p.Seen = true;
                frame.Add(p);
            }
        }

        var mousePos = ImGui.GetMousePos();
        var valid = mousePos.X > -1e30f && mousePos.Y > -1e30f;
        var down = valid && ImGui.IsMouseDown(ImGuiMouseButton.Left);
        var clicked = valid && ImGui.IsMouseClicked(ImGuiMouseButton.Left);
        var released = ImGui.IsMouseReleased(ImGuiMouseButton.Left);

        mouse.Pos = valid ? mousePos : mouse.Pos;
        mouse.Delta = ImGui.GetIO().MouseDelta;
        mouse.Pressed = clicked;
        mouse.Released = released && mouse.Down;
        mouse.Down = down;
        if (clicked) mouse.Owner = HitTest(mousePos);
        else if (!down && !mouse.Released) mouse.Owner = 0;

        frame.Add(mouse);
    }

    /// <summary>Publish this frame's hit regions and retire lifted contacts.</summary>
    public void EndFrame()
    {
        lock (gate)
        {
            regions = building.ToArray();
            building.Clear();

            var dead = new List<uint>();
            foreach (var (id, p) in pointers)
            {
                // A release that arrived after BeginFrame stays one more frame so the owning control sees it.
                if (p.ReleaseReported) dead.Add(id);
            }

            foreach (var id in dead) pointers.Remove(id);
        }

        if (mouse.Released) mouse.Owner = 0;
    }

    /// <summary>Drop every region so no future touch is swallowed (overlay hidden or disabled).</summary>
    public void ClearRegions()
    {
        lock (gate)
        {
            building.Clear();
            regions = [];
        }
    }

    public void AddRegion(int key, Vector2 min, Vector2 max)
        => building.Add(new Region(key, min, max, false, default, 0));

    public void AddCircleRegion(int key, Vector2 center, float radius)
        => building.Add(new Region(key, center - new Vector2(radius), center + new Vector2(radius), true, center, radius));

    /// <summary>The first pointer currently owned by <paramref name="key"/>, if any.</summary>
    public Pointer? Owner(int key)
    {
        foreach (var p in frame)
        {
            if (p.Owner == key) return p;
        }

        return null;
    }

    /// <summary>Stable (per-build) hash for control ids; region keys must be non-zero.</summary>
    public static int Key(string id)
    {
        unchecked
        {
            var h = 2166136261u;
            foreach (var c in id) h = (h ^ c) * 16777619u;
            var k = (int)h;
            return k == 0 ? 1 : k;
        }
    }

    public static int Key(string id, int index)
    {
        unchecked
        {
            var k = Key(id) * 31 + index + 1;
            return k == 0 ? 1 : k;
        }
    }

    // ---- P/Invoke ---------------------------------------------------------------------------------------

    private const nuint SubclassId = 0x7075_6E63; // "punc"

    private const uint WM_POINTERUPDATE = 0x0245;
    private const uint WM_POINTERDOWN = 0x0246;
    private const uint WM_POINTERUP = 0x0247;
    private const uint WM_POINTERLEAVE = 0x024A;
    private const uint WM_POINTERCAPTURECHANGED = 0x024C;

    private const uint POINTER_MESSAGE_FLAG_PRIMARY = 0x2000;
    private const uint PT_MOUSE = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProc(nint hWnd, uint msg, nuint wParam, nint lParam, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, nuint dwRefData);

    [DllImport("comctl32.dll")]
    private static extern bool RemoveWindowSubclass(nint hWnd, SubclassProc pfnSubclass, nuint uIdSubclass);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern bool GetPointerType(uint pointerId, out uint pointerType);
}
