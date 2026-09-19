using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;

namespace TouchControl;

/// <summary>How synthesized key presses are delivered to the game.</summary>
public enum KeyBackend
{
    /// <summary>user32 SendInput - behaves exactly like a physical keyboard. Needs the game window to be focused.</summary>
    SendInput = 0,

    /// <summary>PostMessage WM_KEYDOWN/WM_KEYUP straight to the game window. Works unfocused, but bypasses the OS key state.</summary>
    PostMessage = 1,
}

/// <summary>What a quick-action button does when pressed.</summary>
public enum ButtonKind
{
    /// <summary>Hold a keyboard key for as long as the button is touched.</summary>
    Key = 0,

    /// <summary>Use a GeneralAction row (Sprint, Jump, Teleport, ...).</summary>
    GeneralAction = 1,

    /// <summary>Execute a MainCommand row (opens Character, Inventory, Map, ...).</summary>
    MainCommand = 2,

    /// <summary>Mount Roulette when on foot, Dismount when mounted.</summary>
    MountToggle = 3,

    /// <summary>Opens the settings window of this plugin.</summary>
    OpenSettings = 4,

    /// <summary>Hides the overlay until re-enabled from the settings window or /touch on.</summary>
    HideOverlay = 5,
}

/// <summary>A screen-anchored control. Positions are normalized (0..1) so layouts survive resolution changes.</summary>
[Serializable]
public class Placement
{
    public Vector2 Center { get; set; } = new(0.5f, 0.5f);
    public float Scale { get; set; } = 1f;
}

[Serializable]
public class JoystickConfig
{
    public bool Enabled { get; set; } = true;
    public Placement Placement { get; set; } = new() { Center = new Vector2(0.14f, 0.72f) };

    /// <summary>Radius of the touch zone (the invisible window that starts a drag).</summary>
    public float ZoneRadius { get; set; } = 150f;

    /// <summary>Radius of the drawn joystick base and the drag range of the knob.</summary>
    public float BaseRadius { get; set; } = 90f;
    public float KnobRadius { get; set; } = 36f;

    /// <summary>Fraction of BaseRadius the knob must travel before any key is pressed.</summary>
    public float DeadZone { get; set; } = 0.18f;

    /// <summary>Re-centre the base wherever the finger first lands inside the zone.</summary>
    public bool Floating { get; set; } = true;

    public int ForwardKey { get; set; } = (int)VirtualKey.W;
    public int BackKey { get; set; } = (int)VirtualKey.S;
    public int LeftKey { get; set; } = (int)VirtualKey.A;
    public int RightKey { get; set; } = (int)VirtualKey.D;

    public Vector4 BaseColor { get; set; } = new(0.08f, 0.08f, 0.1f, 0.55f);
    public Vector4 RingColor { get; set; } = new(1f, 1f, 1f, 0.55f);
    public Vector4 KnobColor { get; set; } = new(0.95f, 0.95f, 1f, 0.85f);
}

[Serializable]
public class SkillGroupConfig
{
    public bool Enabled { get; set; } = true;

    /// <summary>Hotbar index 0..9 (hotbar 1..10 in the game UI).</summary>
    public int Hotbar { get; set; }

    /// <summary>First hotbar slot (0..11) mirrored by this group.</summary>
    public int FirstSlot { get; set; }

    /// <summary>Number of slots shown, 1..12.</summary>
    public int SlotCount { get; set; } = 8;

    public Placement Placement { get; set; } = new() { Center = new Vector2(0.86f, 0.72f) };

    /// <summary>Draw the first slot as a big button in the middle of the arc (the "main attack" of gacha games).</summary>
    public bool CenterFirstSlot { get; set; } = true;
    public float CenterRadius { get; set; } = 58f;

    public float ButtonRadius { get; set; } = 38f;

    /// <summary>Distance from the centre to the first ring of buttons.</summary>
    public float RingRadius { get; set; } = 130f;
    public float RingSpacing { get; set; } = 82f;

    /// <summary>Buttons per ring before spilling to the next ring outward.</summary>
    public int RingCapacity { get; set; } = 6;

    /// <summary>Arc the ring buttons are spread over, in degrees. 0 = up, clockwise. Default covers the top-left quadrant so the group can sit in the bottom-right corner.</summary>
    public float ArcStartDeg { get; set; } = 180f;
    public float ArcEndDeg { get; set; } = 360f;

    /// <summary>When true the buttons are laid out in a horizontal row instead of rings.</summary>
    public bool RowLayout { get; set; } = false;
    public float RowSpacing { get; set; } = 12f;

    public bool ShowCooldown { get; set; } = true;
    public bool ShowKeybind { get; set; } = false;
    public bool HideEmptySlots { get; set; } = true;
}

[Serializable]
public class ActionButtonConfig
{
    public bool Enabled { get; set; } = true;
    public ButtonKind Kind { get; set; } = ButtonKind.Key;

    /// <summary>VirtualKey code, GeneralAction row id or MainCommand row id depending on Kind.</summary>
    public int Value { get; set; }

    /// <summary>Optional label override. Empty = resolve from the game sheets / key name.</summary>
    public string Label { get; set; } = string.Empty;

    public Placement Placement { get; set; } = new();
    public float Radius { get; set; } = 34f;
    public Vector4 Color { get; set; } = new(0.1f, 0.1f, 0.14f, 0.7f);
}

[Serializable]
public class MenuBarConfig
{
    public bool Enabled { get; set; } = true;
    public Placement Placement { get; set; } = new() { Center = new Vector2(0.5f, 0.045f) };
    public float ButtonRadius { get; set; } = 22f;
    public float Spacing { get; set; } = 12f;

    /// <summary>MainCommand row ids shown left to right. Defaults: Character, Inventory, Actions and Traits, Journal, Map, Duty Finder, Timers.</summary>
    public List<int> MainCommands { get; set; } = [2, 10, 3, 4, 16, 33, 5];

    public bool ShowEscapeButton { get; set; } = true;
    public bool ShowSettingsButton { get; set; } = true;
    public bool ShowHideButton { get; set; } = true;
}

[Serializable]
public class CameraPadConfig
{
    /// <summary>Off by default: FFXIV already rotates the camera on a plain left-drag, which is what a touch drag produces.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Normalized rectangle (x, y, w, h) of the drag zone.</summary>
    public Vector4 Rect { get; set; } = new(0.55f, 0.15f, 0.42f, 0.42f);

    /// <summary>
    /// While a finger is busy on the joystick or a button, a second finger dragged on the world rotates the camera.
    /// Windows never turns that second contact into mouse input, so the game cannot do it by itself.
    /// </summary>
    public bool SecondFingerRotates { get; set; } = true;

    /// <summary>Radians of yaw per pixel dragged (camera pad and second-finger rotation).</summary>
    public float Sensitivity { get; set; } = 0.006f;
    public bool InvertY { get; set; } = false;
    public bool ShowOutline { get; set; } = true;
}

[Serializable]
public class BridgeConfig
{
    /// <summary>Accept connections from the Android companion app.</summary>
    public bool Enabled { get; set; } = true;

    public int Port { get; set; } = 47800;

    /// <summary>Listen on every interface instead of loopback only. Needed when the phone is a different device.</summary>
    public bool AllowRemote { get; set; } = false;

    /// <summary>Do not draw the in-game overlay while a phone is connected; the phone is the control surface then.</summary>
    public bool HideOverlayWhileConnected { get; set; } = true;
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    /// <summary>When true, controls stop acting and can be dragged / wheel-resized instead.</summary>
    public bool EditMode { get; set; } = false;

    public float GlobalScale { get; set; } = 1f;
    public float Opacity { get; set; } = 0.9f;

    public KeyBackend KeyBackend { get; set; } = KeyBackend.SendInput;

    /// <summary>Hide the overlay while a game text field has keyboard focus (chat, search boxes).</summary>
    public bool HideWhileTyping { get; set; } = true;

    /// <summary>While the overlay is hidden, keep a small translucent eye button on screen that brings it back.</summary>
    public bool ShowRestoreButton { get; set; } = true;
    public Placement RestoreButton { get; set; } = new() { Center = new Vector2(0.975f, 0.045f) };
    public float RestoreButtonRadius { get; set; } = 18f;

    public JoystickConfig Joystick { get; set; } = new();
    public List<SkillGroupConfig> SkillGroups { get; set; } = [];
    public List<ActionButtonConfig> Buttons { get; set; } = [];
    public MenuBarConfig MenuBar { get; set; } = new();
    public CameraPadConfig CameraPad { get; set; } = new();
    public BridgeConfig Bridge { get; set; } = new();

    /// <summary>Set once the default layout has been generated, so an intentionally emptied list stays empty.</summary>
    public bool LayoutInitialized { get; set; } = false;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);

    /// <summary>Fill in the gacha-style default layout. Called for a fresh config and by "Reset layout".</summary>
    public void ResetLayout()
    {
        Joystick = new JoystickConfig();
        MenuBar = new MenuBarConfig();
        CameraPad = new CameraPadConfig();

        SkillGroups =
        [
            // Main combat wheel: hotbar 1, big centre button + ring, bottom-right.
            new SkillGroupConfig
            {
                Hotbar = 0, FirstSlot = 0, SlotCount = 8,
                Placement = new Placement { Center = new Vector2(0.88f, 0.74f) },
            },
            // Secondary row: hotbar 2, six small buttons along the bottom.
            new SkillGroupConfig
            {
                Hotbar = 1, FirstSlot = 0, SlotCount = 6, CenterFirstSlot = false,
                ButtonRadius = 30f, RowLayout = true,
                Placement = new Placement { Center = new Vector2(0.55f, 0.92f) },
            },
        ];

        Buttons =
        [
            new ActionButtonConfig { Kind = ButtonKind.Key, Value = (int)VirtualKey.SPACE, Label = "Jump", Radius = 40f, Placement = new Placement { Center = new Vector2(0.70f, 0.78f) } },
            new ActionButtonConfig { Kind = ButtonKind.GeneralAction, Value = 4, Placement = new Placement { Center = new Vector2(0.28f, 0.86f) } },   // Sprint
            new ActionButtonConfig { Kind = ButtonKind.MountToggle, Placement = new Placement { Center = new Vector2(0.34f, 0.74f) } },
            new ActionButtonConfig { Kind = ButtonKind.GeneralAction, Value = 16, Placement = new Placement { Center = new Vector2(0.95f, 0.42f) } },  // Target Forward
            new ActionButtonConfig { Kind = ButtonKind.Key, Value = (int)VirtualKey.NUMPAD0, Label = "Interact", Placement = new Placement { Center = new Vector2(0.95f, 0.30f) } },
            new ActionButtonConfig { Kind = ButtonKind.Key, Value = (int)VirtualKey.R, Label = "Auto-run", Radius = 28f, Placement = new Placement { Center = new Vector2(0.14f, 0.50f) } },
        ];

        LayoutInitialized = true;
    }
}
