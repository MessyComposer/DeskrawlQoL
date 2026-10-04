using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DeskrawlQoL.Core;

/// <summary>
/// The game's localized texts (SimpleLocalization, class Assets.SimpleLocalization.Scripts.pb in the
/// 2026-10-03 build). Abilities and status effects use their asset name as the key for their display
/// name (AbilityData.sq() = Localize(name), description = Localize(name + ".Desc")).
/// The lookup methods are found by signature, since their names are obfuscated.
/// </summary>
internal static class GameText
{
    private static Func<string, bool> _hasKey;
    private static Func<string, string> _get;
    private static bool _initialized;

    /// <summary>Localized text for <paramref name="key"/> in the current language, or null if there is none.</summary>
    public static string Localize(string key)
    {
        if (!_initialized) Init();
        if (_hasKey == null || string.IsNullOrEmpty(key)) return null;
        try
        {
            if (!_hasKey(key)) return null;
            string text = _get(key);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return Regex.Replace(text, "<[^>]+>", "").Trim(); // drop rich-text tags
        }
        catch { return null; } // missing key in every language throws in the game's lookup
    }

    private static void Init()
    {
        _initialized = true;
        try
        {
            var t = typeof(Assets.SimpleLocalization.Scripts.pb);
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.Static);
            // static bool HasKey(string) and static string Get(string): each the only one of its signature.
            var has = methods.Where(m => m.ReturnType == typeof(bool) && OneStringParam(m)).ToList();
            var get = methods.Where(m => m.ReturnType == typeof(string) && OneStringParam(m)).ToList();
            if (has.Count != 1 || get.Count != 1)
            {
                Plugin.L.LogWarning($"Localization lookup not found ({has.Count}/{get.Count} candidates); using internal names.");
                return;
            }
            _hasKey = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), has[0]);
            _get = (Func<string, string>)Delegate.CreateDelegate(typeof(Func<string, string>), get[0]);
        }
        catch (Exception e) { Plugin.L.LogWarning($"Localization lookup unavailable, using internal names: {e.Message}"); }
    }

    private static bool OneStringParam(MethodInfo m)
    {
        var p = m.GetParameters();
        return p.Length == 1 && p[0].ParameterType == typeof(string);
    }
}
