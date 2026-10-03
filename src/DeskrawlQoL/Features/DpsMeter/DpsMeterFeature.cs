using System;
using System.Collections.Generic;
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
    internal static ConfigEntry<bool> Visible, ShowBreakdown, DebugLog;

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
        return true;
    }

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
        try
        {
            if (Frames.Count > 64) Frames.Clear(); // never let a desync grow unbounded
            f = new Frame { Target = Il2CppHooks.IsA(self, _enemyClass) ? self : IntPtr.Zero };
            if (f.Target != IntPtr.Zero) f.HpBefore = Hp(self);
            Frames.Push(f);
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
                        if (own > 0) Meter.Current.Add(own, DescribeSource(attacker, source), crit != 0);
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
            return SourceNames[src] = Il2CppHooks.ObjName(src) + tag;
        }
        if (attacker == IntPtr.Zero) return "Other (no source)";
        if (Il2CppHooks.IsA(attacker, _playerClass)) return "Basic attack / direct";
        return Il2CppHooks.ObjName(attacker) + " [minion]";
    }
}
