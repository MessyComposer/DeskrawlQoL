using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace DeskrawlQoL.Core;

/// <summary>
/// Parses "Ctrl+Shift+Alt+Key" strings and matches them against IMGUI key events (call from OnGUI).
/// IMGUI events are used because the game runs Unity's new Input System, where UnityEngine.Input is unavailable.
/// </summary>
internal static class Hotkey
{
    public const string FormatHelp = "Format: [Ctrl+][Shift+][Alt+]Key, e.g. Alt+G or Ctrl+Shift+M. Keys are Unity KeyCode names (A-Z, 0-9, F1-F12, Home, ...). " +
        "Avoid keys the game uses (the game ignores modifiers, so Alt+R would also trigger its R action); conflicts are logged at startup.";

    /// <summary>Every mod hotkey, so <see cref="GameKeys"/> can check them against the game's bindings.</summary>
    public static readonly List<(string action, ConfigEntry<string> entry)> Registered = new();

    private static readonly Dictionary<string, (KeyCode key, bool ctrl, bool shift, bool alt)> Cache = new();

    /// <summary>Binds a hotkey config entry and registers it for conflict checks.</summary>
    public static ConfigEntry<string> Bind(ConfigFile config, string section, string key, string defaultValue, string action)
    {
        var entry = config.Bind(section, key, defaultValue, $"{action}. {FormatHelp}");
        Registered.Add((action, entry));
        return entry;
    }

    public static bool TryParse(string spec, out KeyCode key, out bool ctrl, out bool shift, out bool alt)
    {
        var h = Parse(spec);
        (key, ctrl, shift, alt) = h;
        return h.key != KeyCode.None;
    }

    private static (KeyCode key, bool ctrl, bool shift, bool alt) Parse(string spec)
    {
        if (Cache.TryGetValue(spec, out var c)) return c;
        var r = (key: KeyCode.None, ctrl: false, shift: false, alt: false);
        foreach (var raw in spec.Split('+'))
        {
            string part = raw.Trim();
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": r.ctrl = true; break;
                case "shift": r.shift = true; break;
                case "alt": r.alt = true; break;
                default:
                    if (part.Length == 1 && char.IsDigit(part[0])) part = "Alpha" + part;
                    if (!Enum.TryParse(part, true, out r.key))
                        Plugin.L.LogWarning($"Unknown key '{part}' in hotkey '{spec}'.");
                    break;
            }
        }
        return Cache[spec] = r;
    }

    /// <summary>True if <paramref name="e"/> is a KeyDown for exactly this combination.</summary>
    public static bool Pressed(Event e, string spec)
    {
        if (e.type != EventType.KeyDown) return false;
        var h = Parse(spec);
        return h.key != KeyCode.None && e.keyCode == h.key
            && e.control == h.ctrl && e.shift == h.shift && e.alt == h.alt;
    }
}
