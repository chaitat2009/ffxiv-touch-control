# Touch Control — gacha-style on-screen controls for FFXIV

Mobile-game controls for Final Fantasy XIV: a virtual joystick, a skill wheel with your real hotbar icons and
cooldowns, quick buttons (jump, sprint, mount, target, interact) and a menu bar — built for playing FFXIV
through an Android PC emulator (GameHub / GameNative / Winlator) with a touchscreen, and also usable on Windows
touch devices.

It comes in two parts that talk over `localhost`:

| Part | Runs where | Does what |
| --- | --- | --- |
| **Dalamud plugin** (`latest.zip`) | inside the game (XIVLauncher / Dalamud) | executes hotbar slots, holds movement keys, rotates the camera, streams hotbar icons + cooldowns; also draws a fallback in-game overlay |
| **Android companion app** (`TouchControlCompanion.apk`) | on the Android device, over the emulator | draws the controls with native multi-touch and sends your input to the plugin |

Why two parts? An emulator hands the Windows game a single mouse pointer, so anything drawn *inside* the game can
only ever see one finger. The Android app sits above the emulator where every finger is its own touch, and the
plugin does the game-side work that only code inside the game can do.

## Install

### 1. Plugin (in game)

`/xlsettings` → **Experimental** → **Custom Plugin Repositories** → paste, click **+**, **Save and Close**:

```
https://raw.githubusercontent.com/chaitat2009/ffxiv-touch-control/main/repo.json
```

Then `/xlplugins` → search **Touch Control** → **Install**. `/touch` opens its settings; the **Phone app** tab
shows whether the bridge is listening (default port 47800, loopback only).

### 2. Companion app (on the Android device)

1. Download `TouchControlCompanion.apk` from the [latest release](https://github.com/chaitat2009/ffxiv-touch-control/releases/latest)
   and install it (allow installing from unknown sources; it is a debug-signed build).
2. Open it, tap **Grant overlay permission** ("Display over other apps") and allow it.
3. Leave host `127.0.0.1` / port `47800` when the game runs on the same device, tap **Start**.
4. Switch to the game. Once you are logged in the status in the notification changes to *Connected* and the
   skill buttons fill with your hotbar icons.
5. **Edit layout** (settings, notification, or the ✎ button in the menu bar) lets you drag every control;
   tap **Done** when finished. Sizes, hotbars, slot counts and button actions are in the app's settings.

The in-game overlay hides itself automatically while the phone app is connected (toggle in the Phone app tab).

## Controls

| Control | Behaviour |
| --- | --- |
| Joystick (bottom-left) | Drag to move: 8 directions via W/A/S/D. Floating base, dead zone. |
| Skill wheel (bottom-right) | Hotbar 1: big centre button + ring. Real icons, cooldown wedge and timer. Tap executes the slot, so combos, macros, items, mounts and emotes all work. |
| Skill row (bottom) | Hotbar 2 as a plain row. Add more wheels/rows in settings, any hotbar 1–10. |
| Quick buttons | Jump (held while touched), Sprint, Mount ⇄ Dismount, Target Forward, Interact (Numpad 0), Auto-run (R). Any key, General Action or Main Command can be added. |
| Menu bar (top) | ≡ Escape/main menu, Character, Inventory, Actions & Traits, Journal, Map, Duty Finder, Timers, ✎ edit layout, ◌ hide. |
| Camera | Drag on the game with another finger — the emulator turns it into a mouse drag, which FFXIV uses to orbit the camera. If your emulator's touch mode does not drag, enable the **Camera pad** in the app: a zone that rotates the camera through the plugin. |

Recommended game setting: **Character Configuration → Control Settings → Movement Settings → Legacy**, so a
diagonal joystick push moves relative to the camera like a mobile game (Standard makes A/D turn in place).

## How it works

* **Bridge** ([BridgeServer.cs](TouchControl/Bridge/BridgeServer.cs), [Bridge.kt](android/app/src/main/java/dev/touchcontrol/companion/Bridge.kt)):
  TCP on `127.0.0.1:47800`, one JSON object per line. Phone → plugin: `move` (stick vector, 30 Hz), `slot`,
  `key` down/up, `ga` (General Action), `mc` (Main Command), `mount`, `cam`, `icon`. Plugin → phone: `state`
  (10 Hz: hotbar slots with icon id, cooldown remaining/total, keybind; mounted/combat flags), `catalog`
  (names + icons of General Actions and Main Commands for the button editor), `icon` (PNG, base64, rendered from
  the game's own textures so no internet is needed). Every message is executed on the game's framework thread.
* **Movement**: the plugin holds W/A/S/D with Win32 `SendInput` (Dalamud's `IKeyState` refuses to press keys).
  A watchdog releases them if the phone stops sending for 400 ms or disconnects, so a dropped connection never
  leaves the character running.
* **Multi-touch on Android**: each control is its own overlay window with `FLAG_SPLIT_TOUCH`, so fingers on
  different controls are independent and touches outside any control fall straight through to the game.
* **In-game overlay** (fallback, Windows): the plugin also draws ImGui controls, with `WM_POINTER` multi-touch
  on real Windows touchscreens. It is single-finger inside an emulator, which is why the app exists.

## Building

**Plugin**: .NET 10 SDK + a Dalamud installation (`%APPDATA%\XIVLauncher\addon\Hooks\dev` after the first
Dalamud launch, or set `DALAMUD_HOME`). `dotnet build -c Release` → `TouchControl/bin/Release/TouchControl/`.

**App**: `cd android && gradle assembleDebug` (JDK 17, Android SDK 35). Or just push: GitHub Actions builds
both, publishes a release with `latest.zip` + `TouchControlCompanion.apk`, and regenerates `repo.json`.

## Layout of the code

```
TouchControl/                     Dalamud plugin
  Plugin.cs                       service wiring, /touch command, draw + framework hooks
  Configuration.cs                persisted settings, default layout
  Bridge/BridgeServer.cs          TCP server for the phone app: commands, state stream, icon PNGs
  Bridge/BridgeMessages.cs        wire format
  Input/KeySender.cs              SendInput / PostMessage key delivery with stuck-key protection
  Input/MovementController.cs     stick vector → 8-way WASD
  Input/TouchInput.cs             WM_POINTER multi-touch for the in-game overlay on Windows
  Game/GameActions.cs             hotbar read/execute, cooldowns, general actions, main commands, camera
  Game/SheetCache.cs              names + icons for GeneralAction / MainCommand rows
  UI/                             in-game ImGui overlay + settings window
android/app/src/main/java/dev/touchcontrol/companion/
  OverlayService.kt               foreground service, one overlay window per control, edit mode, notification
  Bridge.kt                       TCP client, state/catalog/icon parsing, reconnect
  Layout.kt                       control configs, defaults, persistence
  MainActivity.kt                 settings screen
  views/                          Joystick, SkillWheel, RoundButton, MenuBar, CameraPad, Pill
```

## Known limitations

* Movement is 8-directional (keyboard emulation); true analog walking would need a hook on the game's
  movement input.
* The app is debug-signed; Android will warn on install. Updates install over the old version.
* Camera rotation through the app's camera pad or the in-game camera pad writes the camera yaw/pitch directly
  and is marked experimental.
