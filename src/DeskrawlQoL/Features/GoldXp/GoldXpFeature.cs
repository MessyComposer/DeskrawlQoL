using System;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using DeskrawlQoL.Core;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace DeskrawlQoL.Features.GoldXp;

/// <summary>
/// Gold/hour and XP/hour tracker. Polls the player's gold and XP (read-only; the values are
/// ACTk-obscured and decrypted through ACTk's own conversion operators) and sums the increases.
///
/// Game side (2026-10-03 build): gold is PlayerData.Gold on the live PlayerData singleton; XP is
/// Player.CurrentXP and level Character._level. On level-up AddXP (Player.fhf) subtracts the XP needed
/// for the level, given by the static Player.fhc(level), which we use to count gains across level-ups.
/// </summary>
internal sealed class GoldXpFeature : IFeature
{
    public string Id => "GoldXp";
    public string Title => "Gold / XP per hour";
    public string Description => "Gold/hour and XP/hour tracker overlay.";

    internal static ConfigEntry<string> ToggleKey, ResetKey;
    internal static ConfigEntry<bool> Visible;
    internal static ConfigEntry<float> PosX, PosY;

    public void Bind(ConfigFile c)
    {
        const string s = "GoldXp";
        ToggleKey = Hotkey.Bind(c, s, "ToggleOverlayKey", "", "Gold/XP tracker: show/hide the panel");
        ResetKey = Hotkey.Bind(c, s, "ResetKey", "", "Gold/XP tracker: start a new session");
        Visible = c.Bind(s, "Visible", true, "Panel visible.");
        PosX = c.Bind(s, "PosX", 20f, "Panel X position (drag the title bar to move).");
        PosY = c.Bind(s, "PosY", 80f, "Panel Y position.");
    }

    private static Func<int, long> _xpForLevel;
    private static Func<PlayerData> _getPlayerData;

    public bool Install()
    {
        // static long XpForLevel(int level): Player.fhc, the only public static long(int) on Player.
        var xpForLevel = typeof(Player).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(long) && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(int) }))
            .ToList();
        if (xpForLevel.Count == 1)
            _xpForLevel = (Func<int, long>)Delegate.CreateDelegate(typeof(Func<int, long>), xpForLevel[0]);
        else
            Plugin.L.LogWarning($"GoldXp: XP curve not found ({xpForLevel.Count} candidates); XP across level-ups won't be counted.");

        // The live PlayerData singleton (static PlayerData property on PlayerData).
        var instance = typeof(PlayerData).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(p => p.PropertyType == typeof(PlayerData) && p.GetGetMethod() != null);
        if (instance == null) { Plugin.L.LogWarning("GoldXp: PlayerData instance not found."); return false; }
        _getPlayerData = (Func<PlayerData>)Delegate.CreateDelegate(typeof(Func<PlayerData>), instance.GetGetMethod());
        return true;
    }

    // ---------- tracking ----------

    private static Player _player;
    private static float _nextPlayerSearch;
    private static bool _havePrev;
    private static long _prevGold, _prevXp;
    private static int _prevLevel;

    internal static float SessionStart = -1f;
    internal static long GoldGained, XpGained;
    internal static long CurrentXp, XpToLevel; // for time-to-level
    internal static int Level;

    public void Update()
    {
        if (!TryRead(out long gold, out long xp, out int level)) return;
        float now = Time.realtimeSinceStartup;
        if (SessionStart < 0f) SessionStart = now;

        if (_havePrev)
        {
            if (gold > _prevGold) GoldGained += gold - _prevGold; // spending is ignored
            XpGained += XpGain(_prevLevel, _prevXp, level, xp);
        }
        _prevGold = gold; _prevXp = xp; _prevLevel = level; _havePrev = true;

        Level = level;
        CurrentXp = xp;
        XpToLevel = _xpForLevel != null ? SafeXpFor(level) : 0;
    }

    public void OnGUI() => GoldXpOverlay.OnGUI();

    public void DrawSettings(SettingsGui g)
    {
        g.Toggle("Show panel", Visible);
        g.Button("New session", ResetSession);
    }

    void IFeature.Reset() => ResetSession();

    internal static void ResetSession()
    {
        SessionStart = Time.realtimeSinceStartup;
        GoldGained = XpGained = 0;
    }

    /// <summary>XP gained between two (level, xp) readings, including across level-ups.</summary>
    private static long XpGain(int prevLevel, long prevXp, int level, long xp)
    {
        if (level == prevLevel) return Math.Max(0, xp - prevXp);
        if (level < prevLevel || _xpForLevel == null) return 0; // new character / load: not a gain
        long gain = Math.Max(0, SafeXpFor(prevLevel) - prevXp);  // rest of the old level
        for (int l = prevLevel + 1; l < level && l < prevLevel + 100; l++) gain += SafeXpFor(l); // skipped levels
        return gain + xp;
    }

    private static long SafeXpFor(int level)
    {
        try { return Math.Max(0, _xpForLevel(level)); } catch { return 0; }
    }

    private static bool TryRead(out long gold, out long xp, out int level)
    {
        gold = xp = 0; level = 0;
        var data = _getPlayerData();
        if (data == null) { _havePrev = false; return false; }

        if (!PlayerUsable())
        {
            _player = null;
            if (Time.realtimeSinceStartup < _nextPlayerSearch) return false;
            _nextPlayerSearch = Time.realtimeSinceStartup + 2f;
            _player = Resources.FindObjectsOfTypeAll(Il2CppType.Of<Player>())
                .Select(o => o.Cast<Player>()).FirstOrDefault(p => p.gameObject.activeInHierarchy);
            if (_player == null) { _havePrev = false; return false; }
        }

        gold = data.Gold;          // ObscuredInt -> int (ACTk decrypt)
        xp = _player.CurrentXP;    // ObscuredLong -> long
        level = _player._level;    // ObscuredInt -> int
        return true;
    }

    /// <summary>The cached Player is still alive and in the scene (it's replaced when scenes reload).</summary>
    private static bool PlayerUsable()
    {
        try { return _player != null && !_player.WasCollected && _player.gameObject.activeInHierarchy; }
        catch { return false; } // destroyed native object
    }
}
