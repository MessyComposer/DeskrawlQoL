using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DeskrawlQoL.Core;

/// <summary>
/// Warns when a mod hotkey uses a key the game also reacts to.
///
/// The game reads the keyboard through Unity's Input System and ignores modifiers, so pressing
/// Alt+R for a mod hotkey also fires the game's plain-R action, and we can't swallow it. Instead we
/// collect the game's actual bindings at runtime (many live in scene data, not code) and log a
/// warning for each conflicting mod hotkey.
/// </summary>
internal static class GameKeys
{
    // Checked a few times because some systems only exist once a save/scene is loaded.
    private static readonly float[] CheckTimes = { 5f, 30f, 120f };
    private static int _nextCheck;
    private static readonly Dictionary<string, string> Known = new(); // normalized key -> game action
    private static readonly HashSet<string> Warned = new();
    private static (string key, bool ctrl, bool shift, bool alt)? _bossKey;

    /// <summary>Called every frame by FrameHost.</summary>
    public static void Update()
    {
        if (_nextCheck >= CheckTimes.Length || Time.realtimeSinceStartup < CheckTimes[_nextCheck]) return;
        _nextCheck++;
        int before = Known.Count;
        Collect();
        if (Known.Count != before)
            Plugin.L.LogInfo("Game keys: " + string.Join(", ", Known.OrderBy(k => k.Key).Select(k => $"{k.Key} = {k.Value}"))
                + (_bossKey is { } b ? $", boss key = {Combo(b)}" : ""));
        CheckConflicts();
    }

    private static void CheckConflicts()
    {
        foreach (var (action, entry) in Hotkey.Registered)
        {
            if (!Hotkey.TryParse(entry.Value, out var key, out bool ctrl, out bool shift, out bool alt)) continue;
            string k = Normalize(key.ToString());
            string clash = Known.TryGetValue(k, out var gameAction) ? gameAction
                : _bossKey is { } b && b.key == k && b.ctrl == ctrl && b.shift == shift && b.alt == alt ? "boss key" : null;
            if (clash == null || !Warned.Add($"{entry.Value}|{clash}")) continue;
            Plugin.L.LogWarning($"Hotkey {entry.Value} ({action}) uses {key}, which the game also uses for \"{clash}\". " +
                $"The game ignores Ctrl/Shift/Alt, so that action will trigger too. Change [{entry.Definition.Section}] {entry.Definition.Key} in deskrawl.qol.cfg.");
        }
    }

    private static void Collect()
    {
        // Input action assets (movement, abilities...). ToJson is the simplest way to walk every binding.
        Try("input actions", () =>
        {
            foreach (var asset in Find<InputActionAsset>())
            {
                string json = new InputActionAsset(asset).ToJson();
                foreach (Match m in Regex.Matches(json, @"""path"":\s*""<Keyboard>/(\w+)""[^}]*?""action"":\s*""([^""]+)"""))
                    Add(m.Groups[1].Value, m.Groups[2].Value);
            }
        });

        // Keys stored as serialized fields on game components.
        Try("town portal", () =>
        {
            foreach (var p in Find<TownPortalSystem>())
                AddKey(Marshal.ReadInt32(p, Il2CppHooks.FieldOffset<TownPortalSystem>("TeleportToTownKey", 0x30)), "Return to town");
        });
        Try("loot", () =>
        {
            foreach (var p in Find<LootManager>())
                AddKey(Marshal.ReadInt32(p, Il2CppHooks.FieldOffset<LootManager>("CollectAllKey", 0x60)), "Collect all loot");
        });
        Try("panel toggles", () =>
        {
            foreach (var p in Find<UIInputHandler>())
            {
                // PanelToggle[] { Key Key; PanelBase Panel; bool TownOnly } -> 0x18-byte elements after the array header.
                IntPtr arr = Marshal.ReadIntPtr(p, Il2CppHooks.FieldOffset<UIInputHandler>("Toggles", 0x20));
                if (arr == IntPtr.Zero) continue;
                long len = Marshal.ReadInt64(arr, 0x18);
                for (int i = 0; i < len && i < 64; i++)
                {
                    IntPtr el = arr + 0x20 + i * 0x18;
                    IntPtr panel = Marshal.ReadIntPtr(el, 8);
                    AddKey(Marshal.ReadInt32(el), "Toggle " + (panel != IntPtr.Zero ? Il2CppHooks.ObjName(panel) : "panel"));
                }
            }
        });
        // Hard-coded in InventoryUI/EquipmentUI/StorageUI and UIInputHandler.
        AddKey((int)Key.L, "Inventory/equipment/storage panels");
        AddKey((int)Key.Escape, "Close UI");

        // Boss key (user-configurable in game, respects modifiers).
        Try("boss key", () =>
        {
            foreach (var p in Find<BossKeyManager>())
            {
                char c = (char)Marshal.ReadInt16(p, Il2CppHooks.FieldOffset<BossKeyManager>("triggerKey", 0x20));
                if (c == 0) continue;
                _bossKey = (Normalize(c.ToString()),
                    Marshal.ReadByte(p, Il2CppHooks.FieldOffset<BossKeyManager>("requireCtrl", 0x22)) != 0,
                    Marshal.ReadByte(p, Il2CppHooks.FieldOffset<BossKeyManager>("requireShift", 0x23)) != 0,
                    Marshal.ReadByte(p, Il2CppHooks.FieldOffset<BossKeyManager>("requireAlt", 0x24)) != 0);
            }
        });
    }

    private static void Try(string what, Action read)
    {
        try { read(); }
        catch (Exception e) { Plugin.L.LogWarning($"Couldn't read game key bindings ({what}): {e.Message}"); }
    }

    private static IEnumerable<IntPtr> Find<T>() =>
        Resources.FindObjectsOfTypeAll(Il2CppType.Of<T>()).Select(o => o.Pointer).ToList();

    private static void AddKey(int key, string action)
    {
        if (key > 0) Add(((Key)key).ToString(), action);
    }

    private static void Add(string key, string action)
    {
        string k = Normalize(key);
        if (!Known.ContainsKey(k)) Known[k] = action;
    }

    /// <summary>Common name for a key across Input System Key, binding paths and legacy KeyCode.</summary>
    private static string Normalize(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.StartsWith("alpha")) n = "digit" + n.Substring(5); // KeyCode.Alpha1
        if (n.Length == 1 && char.IsDigit(n[0])) n = "digit" + n; // binding path <Keyboard>/1
        return n == "return" ? "enter" : n;
    }

    private static string Combo((string key, bool ctrl, bool shift, bool alt) b) =>
        (b.ctrl ? "Ctrl+" : "") + (b.shift ? "Shift+" : "") + (b.alt ? "Alt+" : "") + b.key.ToUpperInvariant();
}
