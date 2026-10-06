using System;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using DeskrawlQoL.Core;
using UnityEngine;

namespace DeskrawlQoL.Features.IncomingDamage;

/// <summary>
/// Incoming damage tracker: damage taken per second, min/max hits, and a breakdown by damage type and
/// crit vs normal hits, with how much of each your defences prevented.
///
/// Game side: Player overrides TakeDamage(long raw, bool crit, DamageType type, int, bool canDodge,
/// Character attacker, ScriptableObject source). raw is after the attacker's crit, before your armor,
/// resistances and shields; the HP actually lost is measured as the HP difference across the call.
/// The call returns false when the hit is rejected (dodged, invulnerable).
/// </summary>
internal sealed class IncomingDamageFeature : IFeature
{
    public string Id => "IncomingDamage";
    public string Title => "Incoming damage";
    public string Description => "Damage taken per second, min/max hits, by damage type and crit vs normal.";

    internal static ConfigEntry<bool> Visible, ShowByType, ShowCrit;
    internal static ConfigEntry<float> CombatTimeout, PosX, PosY;

    public void Bind(ConfigFile c)
    {
        const string s = "IncomingDamage";
        Visible = c.Bind(s, "Visible", true, "Panel visible.");
        ShowByType = c.Bind(s, "ShowByType", true, "Show the breakdown by damage type.");
        ShowCrit = c.Bind(s, "ShowCrit", true, "Show crit vs normal hits.");
        CombatTimeout = c.Bind(s, "CombatTimeoutSeconds", 8f,
            "Seconds without taking damage before an encounter ends; the next hit starts a new one. 0 = only reset manually.");
        PosX = c.Bind(s, "PosX", 20f, "Panel X position (drag the title bar to move).");
        PosY = c.Bind(s, "PosY", 420f, "Panel Y position.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte TakeDamageFn(IntPtr self, long amount, byte crit, int type, int d, byte canDodge, IntPtr attacker, IntPtr source, IntPtr mi);

    private static TakeDamageFn _orig;
    private static int _hpOffset;

    public bool Install()
    {
        _hpOffset = Il2CppHooks.FieldOffset<Character>("<lss>k__BackingField", 0x118);
        var takeDamage = Il2CppHooks.FindBySignature(typeof(Player), typeof(bool),
            typeof(long), typeof(bool), typeof(DamageType), typeof(int), typeof(bool), typeof(Character), typeof(ScriptableObject));
        if (takeDamage == null) return false;
        _orig = Il2CppHooks.HookVirtual<TakeDamageFn>(takeDamage, PlayerTakeDamage);
        return true;
    }

    private static int _depth; // nested hits on the player (e.g. reflected effects) are part of the outer hit

    private static byte PlayerTakeDamage(IntPtr self, long amount, byte crit, int type, int d, byte canDodge, IntPtr attacker, IntPtr source, IntPtr mi)
    {
        long before = 0;
        bool outer = _depth == 0;
        try { if (outer) before = Marshal.ReadInt64(self, _hpOffset); } catch { outer = false; }

        _depth++;
        byte result;
        try { result = _orig(self, amount, crit, type, d, canDodge, attacker, source, mi); }
        finally { _depth--; }

        if (!outer) return result;
        try
        {
            long taken = Math.Max(0, before - Marshal.ReadInt64(self, _hpOffset));
            if (result == 0 && taken == 0) { if (amount > 0) IncomingMeter.Current.AddAvoided(); }
            else if (amount > 0) IncomingMeter.Current.AddHit(taken, amount, type, crit != 0);
        }
        catch (Exception e) { Plugin.L.LogError(e); }
        return result;
    }

    public void OnGUI() => IncomingOverlay.OnGUI();

    public void DrawSettings(SettingsGui g)
    {
        g.Toggle("Show panel", Visible);
        g.Toggle("By damage type", ShowByType);
        g.Toggle("Crit vs normal", ShowCrit);
        g.Button("Reset", IncomingMeter.Current.Reset);
    }

    public void Reset() => IncomingMeter.Current.Reset();
}
