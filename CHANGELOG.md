# Changelog

## 1.5.0

- New: **Auto next tier**. A toggle on the mythic rift completion screen that moves on to the next tier after the auto-run countdown. The game's own "Next Stage" auto-run mode only reruns the same tier in mythic rifts.
- New: **Auto-run countdown** setting (0-8 s, 0 = instant) for the game's auto-run on the stage-complete and mythic rift completion screens. Works with both the "Rerun" and "Next Stage" modes; with "Next Stage" and 0 s, you go straight to the next stage.

## 1.4.0

- New: **Keep menus open**. Menus you opened (inventory, talents, paragon...) stay open when the stage changes or you die. Esc still closes them.

## 1.3.2

- Gold / XP: at the level cap, XP/hour counts paragon XP, and the panel shows the time to your next paragon level ("Paragon N in").

## 1.3.1

- Gold / XP: the time-to-level row showed your current level instead of the next one.
- Gold / XP: switching characters no longer counts the other character's level and XP as a gain.
- Durations are shown as hours and days (`2h 18m`, `3d 4h`) instead of minutes like `138:14`.
- Incoming damage: the panel's background now fits its content, with padding at the bottom.

## 1.3.0

- New: **Settings panel** (`Alt+Q`). Show or hide each view, switch their options, change the font size and reset metrics, all in one place.
- New: **Incoming damage** panel: damage taken per second, smallest and biggest hit, avoided hits, how much your defences prevented, crit vs normal hits, and a breakdown by damage type.
- The DPS meter and gold/XP hotkeys are now unbound by default; the settings panel replaces them. If you'd set them in your config, they keep working.
- One font size for all panels, set in the settings panel. The old per-panel `FontSize` settings are no longer used.

## 1.2.1

- Fixed for game version 1.0.1: the DPS meter only showed Thorns.
- Removed: **Auto next stage**. Game 1.0.1 has its own auto-run mode with a Next Stage option.

## 1.2.0

- New: **Boss HP numbers**. The boss health bar shows current / max HP and percent, e.g. `12.3M / 45.6M (27%)`. The format is configurable.
- New: **Gold / XP per hour** panel with session totals and time to next level (`Alt+G` to show/hide, `Alt+N` to reset).
- DPS meter: thorns damage now shows as its own **Thorns** row in the breakdown instead of "Other (no source)". Other damage without a source still shows as "Other (no source)".
- DPS meter: sources now use their in-game names (e.g. "Heavy Attack" instead of "WarriorHeavyAttack2"), and variants with the same name share one row. Set `[DpsMeter.Overlay] UseDisplayNames = false` to keep the internal names.

## 1.1.0

- New: **Auto next stage**. A toggle next to the game's auto-replay (on the stage-complete screen and in combat settings) that moves on to the next stage automatically after the auto-replay countdown. On your furthest stage, auto-replay takes over as usual.

## 1.0.0

- First release.
- DPS meter: live overlay with encounter DPS, rolling DPS and peak, total damage, duration, and per-source breakdown (abilities, status effects, talents, items, minions).
- Hotkeys: Alt+O toggles the overlay, Alt+X resets, Alt+F toggles the breakdown. All are configurable.
