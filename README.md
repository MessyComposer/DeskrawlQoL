# Deskrawl QoL

An unofficial quality-of-life mod for [Deskrawl](https://store.steampowered.com/app/4623570/) featuring a live DPS meter (with per-source breakdown) and an automated gold/XP per hour tracker. Client-side: it doesn't change game values, saves or online data.

## Features

Press **`Alt+Q`** in game to open the **settings panel**. There you can show or hide each view, switch their options, change the font size and reset the metrics. Drag any panel by its title row to move it.

### DPS meter

A live overlay that shows your real damage output, including the indirect damage the training dummy misses: DoTs, procs, minions and on-hit effects.

- **Encounter DPS**, **last 5 s DPS** (with peak), **total damage**, **duration** and **hit count**.
- **Per-source breakdown**: DPS, share of total and crit rate for each ability, tagged `[status]` (DoTs and debuffs), `[talent]`, `[item]` or `[minion]`. Thorns damage gets its own **Thorns** row. It only appears in real fights, since the training dummy never hits you.
- Sources use their in-game names (e.g. "Heavy Attack"), in the game's current language. Variants that share a name (like a combo's hits) are combined into one row. Turn off "In-game source names" to see internal names instead (e.g. `WarriorHeavyAttack2`), one row per variant.
- Counts the HP actually removed from enemies, after mitigation and shields, without overkill.
- An encounter ends after 8 s without damage, and the next hit starts a new one. Set `CombatTimeoutSeconds = 0` for long training-dummy sessions and reset manually.

**Known limitation:** kills from the *Execute* talent remove HP without going through the game's normal damage path, so the meter doesn't count them.

### Incoming damage

What's hurting you, to help decide which defences to invest in.

- **Damage taken per second** (encounter and last 5 s, with peak), total taken, hits and **avoided** hits (dodged, or while invulnerable).
- **Smallest and biggest hit.**
- **Prevented by defences**: how much of the raw incoming damage your armor, resistances and shields stopped.
- **Crit vs normal hits**: share of damage, hit count, biggest hit and prevented %. A large crit share points at crit-damage reduction.
- **By damage type** (Physical, Fire, Cold, Lightning, Poison, Arcane): damage per second, share and prevented %. A large share with a low prevented % points at that resistance.

### Gold / XP per hour

A small panel with **gold/hour**, **XP/hour**, gold and XP gained this session, and an estimate of the **time to your next level** at the current rate.

- Gold counts income only. Spending doesn't lower it. Selling items and offline rewards count as income, so start a new session after collecting offline rewards for a clean rate.
- XP is counted correctly across level-ups. At the level cap, it counts paragon XP and shows the time to your next **paragon level** instead.
- Rates use real time since the session started, so time spent in menus or paused counts too.

### Boss HP numbers

Shows the boss's HP as numbers on the boss health bar, e.g. `12.3M / 45.6M (27%)`. The game's bar otherwise only has a fill. The format is configurable (`[BossHp] Format`, placeholders `{current}`, `{max}`, `{percent}`).

## Install

1. Download `DeskrawlQoL-vX.Y.Z.zip` from the releases page.
2. In Steam, right-click Deskrawl → **Manage → Browse local files**.
3. Extract the zip directly into the Deskrawl root directory (the folder containing `Deskrawl.exe`) so that `winhttp.dll` sits right next to it.
5. Start the game. **The first launch takes a minute or two** while BepInEx prepares itself. Later launches are normal speed.

The zip includes [BepInEx](https://github.com/BepInEx/BepInEx) 6.0.0-be.788 (the mod loader), preconfigured for Deskrawl.

> **Already have BepInEx?** Use `DeskrawlQoL-vX.Y.Z-plugin-only.zip`, and make sure `BepInEx\config\BepInEx.cfg` has `UnityLogListening = false` under `[Logging]`. Without that setting, BepInEx crashes this Unity version (Unity 6000.3) even with no mods installed.

### Update

Extract the plugin-only zip over your install, or just replace `BepInEx\plugins\DeskrawlQoL.dll`.

### Uninstall

Delete `winhttp.dll` from the game folder. That alone disables all mods. To remove everything, also delete `BepInEx\`, `dotnet\`, `doorstop_config.ini`, `.doorstop_version`, `changelog.txt`, `DeskrawlQoL-README.md` and `DeskrawlQoL-LICENSE.txt`.

## Configuration

Most settings are in the in-game settings panel (`Alt+Q`). Everything is also in `BepInEx\config\deskrawl.qol.cfg`, which is created after the first launch. Edit the file while the game is closed.

- `[General]` holds the settings panel hotkey, the font size and the settings panel position.
- `[Features]` turns each feature on or off completely (takes effect on the next launch).
- `[DpsMeter]`, `[DpsMeter.Overlay]`, `[IncomingDamage]`, `[GoldXp]` and `[BossHp]` hold each view's options and panel positions, plus extras not in the panel, like combat timeouts and the boss HP text format.
- **Optional direct hotkeys:** `[DpsMeter]` and `[GoldXp]` have unbound hotkeys for showing or resetting their views without opening the settings panel. They use the format `Alt+P`, `Ctrl+Shift+M` or `Alt+1`.
- **Pick hotkeys the game doesn't use.** The game ignores Ctrl/Shift/Alt, so `Alt+R` would also trigger its R action (return to town). On startup the mod lists the game's keys in `BepInEx\LogOutput.log` (`Game keys: ...`) and warns about any conflicting hotkey. Keys in use: WASD, arrow keys, C, E, I, J, L, M, R, T, 1, 2, Space, Enter, Shift, Esc, plus your boss key.

## Troubleshooting

- **The game closes right after start (brief white window):** BepInEx is crashing. Check that `UnityLogListening = false` is set in `BepInEx\config\BepInEx.cfg`.
- **No overlay:** open the settings panel with `Alt+Q` and check the view is turned on. Then check `BepInEx\LogOutput.log` for lines starting with `Deskrawl QoL`.
- **After a game update:** if the meter stops working or the game crashes, set the feature to `false` under `[Features]` (or delete `winhttp.dll`) until a mod update is out. Please include `BepInEx\LogOutput.log` in bug reports. Setting `LogDamageSummary = true` adds damage totals to the log.

## Compatibility and disclaimer

Tested with Deskrawl 1.0.1 (Unity 6000.3.6f1). This is an unofficial fan mod, not affiliated with or endorsed by First Day Games. It doesn't edit game values or saves, but use it at your own risk.

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
  Core/GameKeys.cs       logs the game's key bindings, warns on hotkey conflicts
  Core/Settings*.cs      settings panel (Alt+Q), its controls, and shared settings like font size
  Core/GameText.cs       the game's localized texts (in-game names)
  Features/<Name>/       one folder per feature
packaging/               BepInEx.cfg shipped in releases, third-party licenses
scripts/                 dev.ps1, package.ps1
```

### Adding a feature

1. Create `Features/<Name>/<Name>Feature.cs` implementing `IFeature`: `Bind` (config), `Install` (hooks; return `false` if it can't work on this build), and optionally `Update`/`OnGUI`. Give it a `Title`, a `DrawSettings` section for the settings panel (its show/hide toggle, options and a reset button) and `Reset` for "Reset all metrics". Use `Settings.FontSize` for panel text. Prefer settings-panel controls over hotkeys; if you add a hotkey, bind it with `Hotkey.Bind` and leave it unbound by default.
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
