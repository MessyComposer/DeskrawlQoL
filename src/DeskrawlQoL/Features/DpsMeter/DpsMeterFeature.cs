using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using DeskrawlQoL.Core;
using UnityEngine;

namespace DeskrawlQoL.Features.DpsMeter;

/// <summary>
/// Live DPS meter. Measures the HP actually removed from enemies on every hit (after mitigation,
/// shields and overkill), attributed to the ability / status effect / talent / item that caused it,
/// so indirect damage (DoTs, procs, minions) is counted too.
/// </summary>
internal sealed class DpsMeterFeature : IFeature
{
    public string Id => "DpsMeter";
    public string Description => "Live DPS meter overlay with per-source breakdown.";

    internal static ConfigEntry<string> ToggleKey, ResetKey, BreakdownKey, HealthField;
    internal static ConfigEntry<float> CombatTimeout, RollingWindow, PosX, PosY;
    internal static ConfigEntry<int> FontSize, MaxRows;
    internal static ConfigEntry<bool> Visible, ShowBreakdown, DebugLog, DisplayNames;

    public void Bind(ConfigFile c)
    {
        const string s = "DpsMeter", o = "DpsMeter.Overlay";
        ToggleKey = Hotkey.Bind(c, s, "ToggleOverlayKey", "Alt+O", "DPS meter: show/hide the overlay");
        ResetKey = Hotkey.Bind(c, s, "ResetKey", "Alt+X", "DPS meter: reset the encounter");
        BreakdownKey = Hotkey.Bind(c, s, "ToggleBreakdownKey", "Alt+F", "DPS meter: show/hide the per-source breakdown");
        CombatTimeout = c.Bind(s, "CombatTimeoutSeconds", 8f,
            "Seconds without dealing damage before an encounter ends; the next hit starts a new one. 0 = only reset manually.");
        RollingWindow = c.Bind(s, "RollingWindowSeconds", 5f, "Window for the 'last N seconds' DPS number.");
        DebugLog = c.Bind(s, "LogDamageSummary", false,
            "Write a damage summary to BepInEx/LogOutput.log every 20 seconds of combat. Useful for bug reports.");
        HealthField = c.Bind(s, "HealthFieldName", "<lrb>k__BackingField",
            "Advanced: obfuscated name of the enemy current-HP field. Falls back to offset 0x118 if not found.");

        Visible = c.Bind(o, "Visible", true, "Overlay visible.");
        ShowBreakdown = c.Bind(o, "ShowBreakdown", true, "Show the per-source breakdown.");
        DisplayNames = c.Bind(o, "UseDisplayNames", true,
            "Show sources by their in-game name (e.g. 'Heavy Attack'). false = internal asset names (e.g. 'WarriorHeavyAttack2'), " +
            "which tell apart variants that share a display name. Sources without a translation always use the internal name.");
        MaxRows = c.Bind(o, "BreakdownRows", 10, "Max number of sources listed in the breakdown.");
        FontSize = c.Bind(o, "FontSize", 14, "Overlay font size.");
        PosX = c.Bind(o, "PosX", 20f, "Overlay X position (drag the title bar to move).");
        PosY = c.Bind(o, "PosY", 200f, "Overlay Y position.");
    }

    public void OnGUI() => DpsOverlay.OnGUI();

    // ---------- damage hook ----------

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte TakeDamageFn(IntPtr self, long amount, byte crit, int type, int d, byte canDodge, IntPtr attacker, IntPtr source, IntPtr mi);

    private static TakeDamageFn _origTakeDamage;
    private static int _hpOffset;
    private static IntPtr _enemyClass, _playerClass;

    public bool Install()
    {
        _enemyClass = Il2CppHooks.ClassOf<Enemy>();
        _playerClass = Il2CppHooks.ClassOf<Player>();
        _hpOffset = Il2CppHooks.FieldOffset<Character>(HealthField.Value, 0x118);

        // Enemy's override of: bool TakeDamage(long amount, bool crit, DamageType type, int ?, bool canDodge, Character attacker, ScriptableObject source)
        var takeDamage = Il2CppHooks.FindBySignature(typeof(Enemy), typeof(bool),
            typeof(long), typeof(bool), typeof(DamageType), typeof(int), typeof(bool), typeof(Character), typeof(ScriptableObject));
        if (takeDamage == null) return false;
        _origTakeDamage = Il2CppHooks.HookVirtual<TakeDamageFn>(takeDamage, TakeDamageHook);

        InstallThornsDetection();
        return true;
    }

    // ---------- thorns ----------
    //
    // Thorns is dealt from DamageEffect.ApplyTo(context) (obfuscated tb): when an enemy's direct hit lands
    // on the player, the game calls TakeDamage on that enemy with (Physical, canDodge: false, attacker: null,
    // source: null). We track which enemy's effect is being applied, and only a hit on *that* enemy with
    // exactly those arguments is labelled Thorns; other unsourced damage stays "Other (no source)".

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ApplyEffectFn(IntPtr self, IntPtr context, IntPtr mi);

    private static ApplyEffectFn _origApplyEffect;
    private static int _contextCasterOffset;
    private static readonly Stack<IntPtr> EffectCasters = new();

    private static void InstallThornsDetection()
    {
        // void tb(c context): the DamageEffect method whose context type has a parameterless method returning Enemy (c.sf()).
        var apply = typeof(DamageEffect)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType.GetMethods().Any(x => x.ReturnType == typeof(Enemy) && x.GetParameters().Length == 0));
        if (apply == null) { Plugin.L.LogWarning("DpsMeter: DamageEffect apply method not found; thorns will show as 'Other (no source)'."); return; }

        // The context's caster field (read by c.sf()).
        _contextCasterOffset = Il2CppHooks.FieldOffset(apply.GetParameters()[0].ParameterType, "lhb", 0x50);
        _origApplyEffect = Il2CppHooks.HookVirtual<ApplyEffectFn>(apply, ApplyEffectHook);
    }

    private static void ApplyEffectHook(IntPtr self, IntPtr context, IntPtr mi)
    {
        IntPtr caster = IntPtr.Zero;
        try
        {
            if (context != IntPtr.Zero) caster = Marshal.ReadIntPtr(context, _contextCasterOffset);
            if (EffectCasters.Count > 64) EffectCasters.Clear();
        }
        catch (Exception e) { Plugin.L.LogError(e); }

        EffectCasters.Push(caster);
        try { _origApplyEffect(self, context, mi); }
        finally { if (EffectCasters.Count > 0) EffectCasters.Pop(); }
    }

    private static bool IsThorns(IntPtr target, int type, byte canDodge, IntPtr attacker, IntPtr source) =>
        attacker == IntPtr.Zero && source == IntPtr.Zero && canDodge == 0 && type == (int)DamageType.Physical
        && EffectCasters.Count > 0 && EffectCasters.Peek() == target;

    private static long Hp(IntPtr character) => Marshal.ReadInt64(character, _hpOffset);

    // A hit can trigger further hits on the same target before it returns (procs, reflects...).
    // Each call measures its own HP delta minus what nested calls already counted.
    private sealed class Frame
    {
        public IntPtr Target;
        public long HpBefore, Nested;
    }

    private static readonly Stack<Frame> Frames = new();
    private static readonly Dictionary<IntPtr, string> SourceNames = new();

    private static byte TakeDamageHook(IntPtr self, long amount, byte crit, int type, int d, byte canDodge, IntPtr attacker, IntPtr source, IntPtr mi)
    {
        Frame f = null;
        bool thorns = false;
        try
        {
            if (Frames.Count > 64) Frames.Clear(); // never let a desync grow unbounded
            f = new Frame { Target = Il2CppHooks.IsA(self, _enemyClass) ? self : IntPtr.Zero };
            if (f.Target != IntPtr.Zero) f.HpBefore = Hp(self);
            Frames.Push(f);
            thorns = IsThorns(self, type, canDodge, attacker, source);
        }
        catch (Exception e) { Plugin.L.LogError(e); }

        byte result = _origTakeDamage(self, amount, crit, type, d, canDodge, attacker, source, mi);

        try
        {
            if (f != null)
            {
                if (Frames.Count > 0 && Frames.Peek() == f) Frames.Pop();
                if (f.Target != IntPtr.Zero)
                {
                    long delta = f.HpBefore - Hp(f.Target);
                    if (delta > 0)
                    {
                        if (Frames.Count > 0 && Frames.Peek().Target == f.Target) Frames.Peek().Nested += delta;
                        long own = delta - f.Nested;
                        if (own > 0) Meter.Current.Add(own, thorns ? "Thorns" : DescribeSource(attacker, source), crit != 0);
                    }
                }
            }
        }
        catch (Exception e) { Plugin.L.LogError(e); }
        return result;
    }

    private static string DescribeSource(IntPtr attacker, IntPtr src)
    {
        if (src != IntPtr.Zero)
        {
            if (SourceNames.TryGetValue(src, out var cached)) return cached;
            string typeName = Il2CppHooks.ClassName(src);
            string tag = typeName switch
            {
                "AbilityData" => "",
                "StatusEffectData" => " [status]",
                _ when typeName.StartsWith("TalentEffect") => " [talent]",
                _ when typeName.StartsWith("EquipmentEffect") => " [item]",
                _ => $" [{typeName}]",
            };
            return SourceNames[src] = Name(src) + tag;
        }
        if (attacker == IntPtr.Zero) return "Other (no source)";
        if (Il2CppHooks.IsA(attacker, _playerClass)) return "Basic attack / direct";
        return Name(attacker) + " [minion]";
    }

    /// <summary>In-game display name (localized, keyed by asset name) or the internal asset name.</summary>
    private static string Name(IntPtr obj)
    {
        string raw = Il2CppHooks.ObjName(obj);
        return DisplayNames.Value ? GameText.Localize(raw) ?? raw : raw;
    }
}
