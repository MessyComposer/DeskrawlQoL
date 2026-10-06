using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace DeskrawlQoL.Core;

/// <summary>
/// The game's localized texts (SimpleLocalization). Abilities and status effects use their asset name
/// as the key for their display name (AbilityData.sq() = Localize(name), description = Localize(name + ".Desc")).
///
/// The localization class and its methods have obfuscated names that change between game builds
/// ("pb" in 1.0.0), so both are found at runtime by shape, never referenced by name at compile time.
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
            // The static class in SimpleLocalization's namespace with exactly one static bool HasKey(string)
            // and one static string Get(string).
            var candidates = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "Assembly-CSharp-firstpass")
                .SelectMany(LoadableTypes)
                .Where(t => t.Namespace == "Assets.SimpleLocalization.Scripts")
                .Select(t => (t, has: Static(t, typeof(bool)), get: Static(t, typeof(string))))
                .Where(c => c.has.Length == 1 && c.get.Length == 1)
                .ToList();
            if (candidates.Count != 1)
            {
                Plugin.L.LogWarning($"Localization lookup not found ({candidates.Count} candidates); using internal names.");
                return;
            }
            _hasKey = (Func<string, bool>)Delegate.CreateDelegate(typeof(Func<string, bool>), candidates[0].has[0]);
            _get = (Func<string, string>)Delegate.CreateDelegate(typeof(Func<string, string>), candidates[0].get[0]);
        }
        catch (Exception e) { Plugin.L.LogWarning($"Localization lookup unavailable, using internal names: {e.Message}"); }
    }

    private static Type[] LoadableTypes(Assembly a)
    {
        try { return a.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
    }

    private static MethodInfo[] Static(Type t, Type ret) =>
        t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == ret && OneStringParam(m)).ToArray();

    private static bool OneStringParam(MethodInfo m)
    {
        var p = m.GetParameters();
        return p.Length == 1 && p[0].ParameterType == typeof(string);
    }
}
