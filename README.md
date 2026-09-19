# Touch Control — gacha-style on-screen controls for FFXIV

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin (XIVLauncher) that draws a mobile-game control
overlay on top of Final Fantasy XIV, for touchscreens, Windows handhelds and tablets:

| Control | What it does |
| --- | --- |
| **Virtual joystick** (bottom-left) | Drag to move. Floating base, dead zone, eight directions. Sends W/A/S/D like a real keyboard. |
| **Skill wheel** (bottom-right) | Mirrors hotbar slots as round icon buttons: big "main attack" in the middle, the rest fanned out on rings. Shows cooldowns. Tapping executes the hotbar slot through the game, so macros, combos, items, mounts and emotes all work. |
| **Secondary row** (bottom) | A second hotbar as a plain row of buttons. Add as many wheels/rows as you like. |
| **Quick buttons** | Jump (held while touched), Sprint, Mount / Dismount, Target, Interact, Auto-run. Fully configurable: any key, any General Action, any Main Command. |
| **Menu bar** (top) | Back/Escape, Character, Inventory, Actions & Traits, Journal, Map, Duty Finder, Timers, plugin settings, hide overlay. |
| **Camera pad** (optional) | An invisible drag zone that orbits the camera. Off by default because a plain touch-drag on the world already does that in FFXIV. |

Everything can be dragged and mouse-wheel-resized in **edit mode**; positions are stored as screen fractions so the
layout survives resolution changes.

Commands: `/touch` (settings), `/touch on|off`, `/touch edit`, `/touch reset`.

## Installing in game (custom repository)

1. In game type `/xlsettings`, open the **Experimental** tab.
2. Under **Custom Plugin Repositories** paste this URL into the empty row, click **+**, tick it, then **Save and Close**:

   ```
   https://raw.githubusercontent.com/chaitat2009/ffxiv-touch-control/main/repo.json
   ```

3. `/xlplugins` → search for **Touch Control** → **Install**.
4. `/touch` opens the settings, `/touch edit` lets you arrange the controls.

Every push to `main` is built by GitHub Actions against the current Dalamud release, published as a GitHub
Release (`latest.zip`) and `repo.json` is regenerated from the plugin manifest, so the in-game installer
always sees the newest version.

## Recommended game settings

* **Character Configuration → Control Settings → Movement Settings → Legacy.** With Legacy movement a diagonal
  joystick push moves the character relative to the camera, exactly like a gacha game stick. With Standard
  movement A/D turn in place instead (you can point the joystick's Left/Right at your strafe keys Q/E if you
  prefer that).
* Camera: just drag on the world. FFXIV rotates the camera on left-drag, which is what a finger drag produces.

## Building

Requirements: .NET 10 SDK and a Dalamud installation. XIVLauncher puts one in
`%APPDATA%\XIVLauncher\addon\Hooks\dev` the first time you launch the game with Dalamud enabled; the
`Dalamud.NET.Sdk` picks it up from there automatically. To build against a different Dalamud copy set the
`DALAMUD_HOME` environment variable to its folder.

```bash
dotnet build -c Release
```

The output lands in `TouchControl/bin/Release/TouchControl/` (`bin/x64/Release/...` when built from the
solution in Visual Studio) together with `TouchControl.json` and a `latest.zip` produced by DalamudPackager.

## Installing (dev plugin)

1. In game, `/xlsettings` → **Experimental** → **Dev Plugin Locations** → add the full path to
   `TouchControl/bin/Release/TouchControl/TouchControl.dll` (or the Debug path) → Save.
2. `/xlplugins` → **Dev Tools** → **Installed Dev Plugins** → enable *Touch Control*.
3. `/touch` opens the settings; `/touch edit` lets you arrange the controls.


## Multi-touch

Windows turns only the *primary* touch contact into mouse input, so anything driven by the mouse (the game, and
ImGui) is single-finger by construction. Every contact does produce its own `WM_POINTER*` message stream,
though. The plugin subclasses the game window (`SetWindowSubclass`), reads those messages and drives the overlay
from them ([TouchInput.cs](TouchControl/Input/TouchInput.cs)):

* a touch that lands on an overlay control is swallowed before `DefWindowProc`, so no mouse is synthesized and
  the game never sees it; that control owns the finger until it lifts;
* a touch anywhere else passes through untouched, so tapping game UI and dragging the camera keep working;
* while a finger holds a control, a second finger dragged on the world rotates the camera (the game would get
  no input for it otherwise);
* the real mouse is folded in as one more pointer, so everything still works without a touchscreen.

Result: hold the joystick with one thumb and hit skills with the other, like a phone game.

## How input reaches the game

Dalamud's `IKeyState` intentionally refuses to *press* keys, so movement and key buttons are delivered with
Win32 `SendInput` (default; identical to a physical keyboard, needs the game window focused) or, if you prefer,
`PostMessage` straight to the game window (works even when the window is not focused). Dalamud only blocks
keyboard messages from reaching the game while a text field is focused, so holding a control does not swallow
the synthesized keys. Every key the plugin presses is tracked and released the moment the overlay is not drawn
(cutscene, gpose, UI hidden, `/touch off`, plugin unload).

Hotbar buttons, General Actions (Sprint, Mount Roulette, Dismount, Target Forward…) and Main Commands are
executed directly through the game's own functions (`RaptureHotbarModule.ExecuteSlotById`,
`ActionManager.UseAction`, `UIModule.ExecuteMainCommand`) from the framework thread.

## Layout of the code

```
TouchControl/
  Plugin.cs                 service wiring, /touch command, draw + framework hooks
  Configuration.cs          all persisted settings, default gacha layout
  Input/KeySender.cs        SendInput / PostMessage key delivery with stuck-key protection
  Input/MovementController.cs   analog stick vector → 8-way WASD
  Game/GameActions.cs       hotbar read/execute, cooldowns, general actions, main commands, camera
  Game/SheetCache.cs        names + icons for GeneralAction / MainCommand rows
  UI/Overlay.cs             borderless control windows, round icon buttons, edit-mode handle, cooldown pie
  UI/OverlayRenderer.cs     draws every control, releases keys when the overlay is hidden
  UI/ConfigWindow.cs        settings window
  UI/Widgets/               Joystick, SkillGroup, ActionButton, MenuBar, CameraPad
```

## Known limitations

* While a finger is on an overlay control, a second finger cannot click the game's *own* UI (hotbars, windows):
  Windows only synthesizes mouse input for the first contact. Overlay controls and camera rotation do work with
  the second finger.
* Movement is 8-directional (keyboard emulation). True analog walking would need a hook on the game's
  movement input, which is deliberately avoided in this first version.
* The camera pad writes the camera yaw/pitch directly and is marked experimental.
* Icons for a hotbar slot come from the game's own slot data, so they update with combos and job changes, but
  charge counts are not drawn yet.
