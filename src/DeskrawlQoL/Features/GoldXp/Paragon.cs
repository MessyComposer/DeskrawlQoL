using System;
using System.Linq;
using System.Reflection;
using CodeStage.AntiCheat.ObscuredTypes;

namespace DeskrawlQoL.Features.GoldXp;

/// <summary>
/// Read-only access to the paragon system that takes over XP at the level cap.
///
/// Game side (1.0.1): at max level Player.AddXP sends all XP to a static paragon class ("hf"), which keeps
/// the unlocked flag, paragon level and paragon XP as the only static ObscuredBool / ObscuredInt /
/// ObscuredLong fields, and computes the XP needed for the next paragon level in a static long().
/// The class and its members have obfuscated names that change between game builds, so the class is
/// found by shape (the static class with a parameterless method returning SavedParagon) and the members
/// by type.
/// </summary>
internal static class Paragon
{
    private static PropertyInfo _unlocked, _level, _xp;
    private static MethodInfo[] _longGetters; // XP getter and XP-needed getter, told apart at runtime
    private static MethodInfo _needed;
    private static bool _initialized;

    public static bool Available => Init();

    private static bool _logged;

    /// <summary>
    /// Paragon state, or false if paragon isn't active (or can't be read). <paramref name="atLevelCap"/>
    /// counts as active even if the game's unlocked flag says otherwise.
    /// </summary>
    public static bool TryRead(bool atLevelCap, out int level, out long xp, out long needed)
    {
        level = 0; xp = needed = 0;
        if (!Init()) return false;
        try
        {
            bool unlocked = (ObscuredBool)_unlocked.GetValue(null);
            if (!unlocked && !atLevelCap) return false;
            level = (ObscuredInt)_level.GetValue(null);
            xp = (ObscuredLong)_xp.GetValue(null);

            if (_needed == null)
            {
                // One long() returns the stored XP, the other the XP needed. Decide once they differ.
                long stored = xp;
                var values = _longGetters.Select(m => (long)m.Invoke(null, null)).ToArray();
                var others = _longGetters.Where((m, i) => values[i] != stored).ToArray();
                if (others.Length != 1) return false;
                _needed = others[0];
            }
            needed = (long)_needed.Invoke(null, null);
            if (!_logged)
            {
                _logged = true;
                Plugin.L.LogInfo($"GoldXp: paragon level {level}, {xp}/{needed} XP (unlocked flag: {unlocked}, at level cap: {atLevelCap}).");
            }
            return needed > 0;
        }
        catch { return false; }
    }

    private static bool Init()
    {
        if (_initialized) return _unlocked != null;
        _initialized = true;
        try
        {
            var type = typeof(SavedParagon).Assembly.GetTypes().FirstOrDefault(t => t.IsAbstract && t.IsSealed
                && t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Any(m => m.ReturnType == typeof(SavedParagon) && m.GetParameters().Length == 0));
            if (type == null) return Fail("paragon class not found");

            var props = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            PropertyInfo Single(Type t)
            {
                var p = props.Where(x => x.PropertyType == t && x.GetGetMethod(true) != null).ToArray();
                return p.Length == 1 ? p[0] : null;
            }
            _unlocked = Single(typeof(ObscuredBool));
            _level = Single(typeof(ObscuredInt));
            _xp = Single(typeof(ObscuredLong));
            _longGetters = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.ReturnType == typeof(long) && m.GetParameters().Length == 0).ToArray();
            if (_unlocked == null || _level == null || _xp == null || _longGetters.Length != 2)
            {
                _unlocked = null;
                return Fail($"paragon members not found ({_longGetters.Length} XP getters)");
            }
            return true;
        }
        catch (Exception e) { return Fail(e.Message); }
    }

    private static bool Fail(string why)
    {
        Plugin.L.LogWarning($"GoldXp: {why}; time to next paragon level won't be shown.");
        return false;
    }
}
