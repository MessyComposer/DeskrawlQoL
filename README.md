# Deskrawl QoL

Quality-of-life mod for [Deskrawl](https://store.steampowered.com/app/4623570/). Client-side: it doesn't change game values, saves or online data.

## Features

### DPS meter

A live, draggable overlay that shows your real damage output, including the indirect damage the training dummy misses: DoTs, procs, minions and on-hit effects.

- **Encounter DPS**, **last 5 s DPS** (with peak), **total damage**, **duration** and **hit count**.
- **Per-source breakdown**: DPS, share of total and crit rate for each ability, tagged `[status]` (DoTs and debuffs), `[talent]`, `[item]` or `[minion]`.
- Counts the HP actually removed from enemies, after mitigation and shields, without overkill.
- An encounter ends after 8 s without damage, and the next hit starts a new one. Set `CombatTimeoutSeconds = 0` for long training-dummy sessions and reset manually.

| Hotkey | Action |
|---|---|
| `Alt+O` | Show/hide the overlay |
| `Alt+X` | Reset the encounter |
| `Alt+F` | Show/hide the breakdown |

Drag the panel by its title row to move it.

**Known limitation:** kills from the *Execute* talent remove HP without going through the game's normal damage path, so the meter doesn't count them.

### Auto next stage

Adds an **Auto next stage** toggle next to the game's auto-replay toggles: on the stage-complete screen (above auto-replay) and in the combat settings (below auto-rerun). Both control the same setting. When it's on and there is a next stage, the screen counts down (same delay as auto-replay) and moves on to the next stage, just like clicking **Next Stage**.

- Takes priority over auto-replay while a next stage exists. On the last unlocked stage, auto-replay works as usual, so turning on both means "push forward, then farm the furthest stage".
- Turning it off during the countdown hands the screen back to auto-replay.
- The toggle state is remembered (`[AutoNextStage] Enabled`).

### Gold / XP per hour

A small draggable panel with **gold/hour**, **XP/hour**, gold and XP gained this session, and an estimate of the **time to your next level** at the current rate.

- Gold counts income only. Spending doesn't lower it. Selling items and offline rewards count as income, so press reset after collecting offline rewards for a clean rate.
- XP is counted correctly across level-ups.
- Rates use real time since the session started, so time spent in menus or paused counts too.

| Hotkey | Action |
|---|---|
| `Alt+G` | Show/hide the panel |
| `Alt+N` | Start a new session (reset) |

## Install

1. Download `DeskrawlQoL-vX.Y.Z.zip` from the releases page.
2. In Steam, right-click Deskrawl → **Manage → Browse local files**.
3. Extract the zip into that folder so `winhttp.dll` sits next to `Deskrawl.exe`.
4. Start the game. **The first launch takes a minute or two** while BepInEx prepares itself. Later launches are normal speed.

The zip includes [BepInEx](https://github.com/BepInEx/BepInEx) 6.0.0-be.788 (the mod loader), preconfigured for Deskrawl.

> **Already have BepInEx?** Use `DeskrawlQoL-vX.Y.Z-plugin-only.zip`, and make sure `BepInEx\config\BepInEx.cfg` has `UnityLogListening = false` under `[Logging]`. Without that setting, BepInEx crashes this Unity version (Unity 6000.3) even with no mods installed.

### Update

Extract the plugin-only zip over your install, or just replace `BepInEx\plugins\DeskrawlQoL.dll`.

### Uninstall

Delete `winhttp.dll` from the game folder. That alone disables all mods. To remove everything, also delete `BepInEx\`, `dotnet\`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`, `DeskrawlQoL-README.md` and `DeskrawlQoL-LICENSE.txt`.

## Configuration

Settings are in `BepInEx\config\deskrawl.qol.cfg`, which is created after the first launch. Edit it while the game is closed.

- `[Features]` turns each feature on or off.
- `[DpsMeter]` holds hotkeys, combat timeout and rolling window. Hotkeys use the format `Alt+O`, `Ctrl+Shift+M` or `Alt+1`.
- **Pick hotkeys the game doesn't use.** The game ignores Ctrl/Shift/Alt, so `Alt+R` would also trigger its R action (return to town). On startup the mod lists the game's keys in `BepInEx\LogOutput.log` (`Game keys: ...`) and warns about any conflicting hotkey. Keys in use: WASD, arrow keys, C, E, I, J, L, M, R, T, 1, 2, Space, Enter, Shift, Esc, plus your boss key.
- `[DpsMeter.Overlay]` holds visibility, font size, position and breakdown rows.
- `[AutoNextStage]` holds the toggle state and its label text.
- `[GoldXp]` holds the gold/XP panel's hotkeys, visibility, font size and position.

## Troubleshooting

- **The game closes right after start (brief white window):** BepInEx is crashing. Check that `UnityLogListening = false` is set in `BepInEx\config\BepInEx.cfg`.
- **No overlay:** press `Alt+O`. Then check `BepInEx\LogOutput.log` for lines starting with `Deskrawl QoL`.
- **After a game update:** if the meter stops working or the game crashes, set the feature to `false` under `[Features]` (or delete `winhttp.dll`) until a mod update is out. Please include `BepInEx\LogOutput.log` in bug reports. Setting `LogDamageSummary = true` adds damage totals to the log.

## Compatibility and disclaimer

Tested with the Deskrawl build of 2026-10-03 (Unity 6000.3.6f1). This is an unofficial fan mod, not affiliated with or endorsed by First Day Games. It doesn't edit game values or saves, but use it at your own risk.

---

## Development

The repo is separate from the game. Builds copy the plugin DLL into the game's `BepInEx\plugins`.

### Requirements

- .NET SDK 8 or newer. The plugin targets `net6.0`, which BepInEx's bundled runtime uses.
- Deskrawl with BepInEx installed (the release zip is the easiest way), launched once. The first launch generates `BepInEx\interop\`, which the project compiles against. Those assemblies are derived from the game, so they're never committed.

### Game location

The build looks for the game in this order:

1. A `GameDir.props.user` file in the repo root (gitignored, per machine):
   ```xml
   <Project><PropertyGroup><GameDir>D:\SteamLibrary\steamapps\common\Deskrawl</GameDir></PropertyGroup></Project>
   ```
2. The `DESKRAWL_DIR` environment variable.
3. `C:\Program Files (x86)\Steam\steamapps\common\Deskrawl`.

### Dev loop

```powershell
.\scripts\dev.ps1            # close game → build → deploy → launch via Steam → follow the log
.\scripts\dev.ps1 -NoLaunch  # build + deploy only
.\scripts\dev.ps1 -All       # follow the full BepInEx log
```

`dotnet build src/DeskrawlQoL -c Release` also deploys. Pass `-p:DeployToGame=false` to skip deploying. The DLL is locked while the game runs, so close the game before building (`dev.ps1` does this for you).

### Releasing

1. Bump `<Version>` in `src/DeskrawlQoL/DeskrawlQoL.csproj` and `Plugin.Version`, and update `CHANGELOG.md`.
2. Run `.\scripts\package.ps1`. It writes the full and plugin-only zips to `dist\`, using a pinned, hash-checked BepInEx download plus `packaging\BepInEx.cfg`.
3. Before publishing, test the full zip on a clean game folder: remove the files listed under Uninstall, extract the zip, launch. Package *before* wiping, because building needs `BepInEx\interop\`. The clean install regenerates it on its first launch.

### Layout

```
src/DeskrawlQoL/
  Plugin.cs              entry point, feature registration
  Core/Il2CppHooks.cs    hooking primitives (read the notes below)
  Core/FrameHost.cs      per-frame Update/OnGUI for features
  Core/ClickThrough.cs   keeps the window solid under our panels
  Core/Hotkey.cs, Ui.cs  hotkey parsing, IMGUI helpers
  Core/GameKeys.cs      logs the game's key bindings, warns on hotkey conflicts
  Features/<Name>/       one folder per feature
packaging/               BepInEx.cfg shipped in releases, third-party licenses
scripts/                 dev.ps1, package.ps1
```

### Adding a feature

1. Create `Features/<Name>/<Name>Feature.cs` implementing `IFeature`: `Bind` (config), `Install` (hooks; return `false` if it can't work on this build), and optionally `Update`/`OnGUI`. Bind hotkeys with `Hotkey.Bind`.
2. Add it to the array in `Plugin.Load`. It automatically gets an on/off switch under `[Features]`.

### Hooking on this game: read before changing anything

Deskrawl runs on Unity 6000.3 (IL2CPP metadata v39). Much of the usual BepInEx toolkit **crashes the game** on this version:

| Doesn't work | Use instead |
|---|---|
| Harmony patches (`Harmony.Patch`, `[HarmonyPatch]`) | `Il2CppHooks.HookVirtual` / `HookMethodInfo` |
| `ClassInjector.RegisterTypeInIl2Cpp`, custom MonoBehaviours, `AddComponent<MyType>` | `FrameHost` (feature `Update`/`OnGUI`) |
| Dobby / `INativeDetour` inline hooks | same as above |
| `DelegateSupport` / converting delegates to Il2Cpp (for example `Application.logMessageReceived`) | avoid it; it installs the crashing injection hooks ([Il2CppInterop#283](https://github.com/BepInEx/Il2CppInterop/issues/283)) |
| `GUI.DrawTexture` (stripped from the build) | `GUI.Label(rect, "", Ui.Fill(color))` |
| `UnityEngine.Input` (the game uses the new Input System) | `Hotkey.Pressed(Event.current, ...)` in `OnGUI` |
| Clickable IMGUI panels. The game window is click-through wherever the game has no UI | call `ClickThrough.Block(rect)` every Repaint for anything the player should click or drag |
| Swallowing a key press (the game reads keys through the Input System and ignores modifiers, so `Alt+R` also fires its R action) | default hotkeys to keys the game doesn't use, and bind them with `Hotkey.Bind` so the startup conflict check covers them |

- **`HookVirtual`** swaps the method's vtable slot in its class and all subclasses. Calls through the vtable are intercepted, but direct calls such as `base.X()` or non-virtual calls are not. Vtables are filled lazily, so the slot is patched in the background once the game first uses the class.
- **`HookMethodInfo`** swaps `MethodInfo.methodPointer`. Good for Unity messages (`Update`, `OnGUI`, `Awake`) that Unity invokes through it. It does not intercept direct calls from other game code.
- Hook delegates use the raw IL2CPP signature: `ret fn(IntPtr self, args..., IntPtr methodInfo)`. Use `byte` for `bool`, the underlying integer type for enums, and `IntPtr` for objects. Always call the original and wrap your own code in try/catch: an exception escaping into native code kills the game.
- **Game method and field names are obfuscated** (e.g. `bko`, `lrb`) and can change in any game update. Find methods by signature (`Il2CppHooks.FindBySignature`) and keep names you can't avoid in config with a fallback (see `HealthFieldName`).

To explore game code, dump it with [Cpp2IL](https://github.com/SamboyCoding/Cpp2IL) 2022.1.0-pre-release.21 or newer:

```powershell
Cpp2IL --game-path "<GameDir>" --output-as diffable-cs --output-to dump   # class/method signatures
Cpp2IL --game-path "<GameDir>" --output-as isil --output-to isil          # method bodies (disassembly)
```

### After a game update

1. Launch once so BepInEx regenerates `interop\`, then rebuild. Compile errors mean a type the mod uses was renamed or removed.
2. Check the log for `failed to install`. Re-dump with Cpp2IL and update signatures or field names.
3. If the game crashes, narrow it down with `[Features] <Name> = false` and `[Debug] DisableFrameHost = true`.
