# Changelog

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
