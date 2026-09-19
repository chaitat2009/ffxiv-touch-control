using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TouchControl.Input;

/// <summary>
/// Delivers synthetic keyboard input to the game. Dalamud's IKeyState deliberately refuses to *press* keys,
/// so we go through Win32 instead: either SendInput (indistinguishable from a real keyboard, requires focus)
/// or PostMessage straight to the game window (works unfocused, bypasses GetAsyncKeyState).
/// Tracks every key it holds so nothing stays stuck when the plugin unloads or the overlay disappears.
/// </summary>
public sealed class KeySender : IDisposable
{
    private readonly HashSet<int> held = [];
    private nint gameWindow;

    public KeyBackend Backend { get; set; } = KeyBackend.SendInput;

    public bool IsHeld(int vk) => held.Contains(vk);

    public void Down(int vk)
    {
        if (vk <= 0 || !held.Add(vk)) return;
        Send(vk, up: false);
    }

    public void Up(int vk)
    {
        if (vk <= 0 || !held.Remove(vk)) return;
        Send(vk, up: true);
    }

    /// <summary>Full press: down now, up immediately after. The game sees a one-frame tap.</summary>
    public void Tap(int vk)
    {
        if (vk <= 0) return;
        Send(vk, up: false);
        Send(vk, up: true);
        held.Remove(vk);
    }

    /// <summary>Make the set of held keys equal to <paramref name="wanted"/>, releasing and pressing only what changed.</summary>
    public void SetHeld(IReadOnlySet<int> wanted)
    {
        // Copy - Up() mutates the set we are iterating.
        foreach (var vk in new List<int>(held))
        {
            if (!wanted.Contains(vk)) Up(vk);
        }

        foreach (var vk in wanted) Down(vk);
    }

    public void ReleaseAll()
    {
        foreach (var vk in new List<int>(held)) Up(vk);
    }

    public void Dispose() => ReleaseAll();

    private void Send(int vk, bool up)
    {
        try
        {
            if (Backend == KeyBackend.PostMessage) PostKey(vk, up);
            else SendInputKey(vk, up);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to send key {Vk} (up={Up})", vk, up);
        }
    }

    // ---- SendInput ------------------------------------------------------------------------------------

    private static void SendInputKey(int vk, bool up)
    {
        var scan = (ushort)MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
        var flags = 0u;
        if (up) flags |= KEYEVENTF_KEYUP;
        if (IsExtendedKey(vk)) flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = scan,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = 0,
                },
            },
        };

        var sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if (sent != 1)
            Plugin.Log.Warning("SendInput rejected key {Vk}: error {Err}", vk, Marshal.GetLastWin32Error());
    }

    // ---- PostMessage ----------------------------------------------------------------------------------

    private void PostKey(int vk, bool up)
    {
        var hwnd = GetGameWindow();
        if (hwnd == 0)
        {
            Plugin.Log.Warning("Game window handle not found; falling back to SendInput");
            SendInputKey(vk, up);
            return;
        }

        var scan = MapVirtualKey((uint)vk, MAPVK_VK_TO_VSC);
        uint lParam = 1 | (scan << 16);
        if (IsExtendedKey(vk)) lParam |= 1u << 24;
        if (up) lParam |= (1u << 30) | (1u << 31);

        PostMessage(hwnd, up ? WM_KEYUP : WM_KEYDOWN, (nuint)vk, unchecked((nint)(int)lParam));
    }

    private nint GetGameWindow()
    {
        if (gameWindow != 0 && IsWindow(gameWindow)) return gameWindow;

        gameWindow = Process.GetCurrentProcess().MainWindowHandle;
        if (gameWindow != 0) return gameWindow;

        // MainWindowHandle can be 0 when the game has no title bar (borderless); walk our own top-level windows.
        var pid = (uint)Environment.ProcessId;
        nint found = 0;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var owner);
            if (owner != pid) return true;

            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() == "FFXIVGAME")
            {
                found = hwnd;
                return false;
            }

            return true;
        }, 0);

        gameWindow = found;
        return gameWindow;
    }

    /// <summary>Keys whose scan code needs the 0xE0 extended prefix (arrows, nav cluster, right-hand modifiers, numpad divide).</summary>
    private static bool IsExtendedKey(int vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true, // PgUp PgDn End Home
        0x25 or 0x26 or 0x27 or 0x28 => true, // arrows
        0x2D or 0x2E or 0x2C => true,         // Insert Delete PrintScreen
        0x6F => true,                         // numpad divide
        0x90 => true,                         // NumLock
        0xA3 or 0xA5 => true,                 // RControl RMenu
        0x5B or 0x5C => true,                 // LWin RWin
        _ => false,
    };

    // ---- P/Invoke -------------------------------------------------------------------------------------

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MAPVK_VK_TO_VSC = 0;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint msg, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);
}
