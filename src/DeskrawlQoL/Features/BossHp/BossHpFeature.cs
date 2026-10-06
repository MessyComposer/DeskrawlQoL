using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using DeskrawlQoL.Core;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DeskrawlQoL.Features.BossHp;

/// <summary>
/// Shows the boss's HP as numbers on the game's boss health bar (which otherwise only has a fill).
///
/// Game side (2026-10-03 build): BossHPBarUI tracks its boss in a private field (+0x48) and lerps a
/// Fill image in Update. Current HP is Character +0x118; max HP is the MaxHealth stat, read through the
/// same stat getter the game uses to cap health (Character.bja(StatType)).
/// </summary>
internal sealed unsafe class BossHpFeature : IFeature
{
    public string Id => "BossHp";
    public string Title => "Boss HP";
    public string Description => "Shows the boss's HP as numbers on the boss health bar.";

    private static ConfigEntry<string> _format;
    private static ConfigEntry<bool> _abbreviate, _show;

    public void Bind(ConfigFile c)
    {
        _show = c.Bind(Id, "Visible", true, "Show the numbers on the boss health bar.");
        _format = c.Bind(Id, "Format", "{current} / {max} ({percent})",
            "Text on the boss health bar. Placeholders: {current}, {max}, {percent}.");
        _abbreviate = c.Bind(Id, "AbbreviateNumbers", true, "Show 1.23M instead of 1,234,567.");
    }

    public void DrawSettings(SettingsGui g)
    {
        g.Toggle("Show numbers on boss bar", _show);
        g.Toggle("Abbreviate numbers", _abbreviate);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidFn(IntPtr self, IntPtr mi);

    private static VoidFn _origUpdate;
    private static int _bossOffset, _hpOffset;
    private static Func<Character, StatType, double> _getStat;

    public bool Install()
    {
        _bossOffset = Il2CppHooks.FieldOffset<BossHPBarUI>("nyz", 0x48);
        _hpOffset = Il2CppHooks.FieldOffset<Character>("<lrb>k__BackingField", 0x118);

        // double GetStat(StatType): the public non-virtual one (bja); the virtual ones are the per-class parts.
        var getStat = typeof(Character).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.ReturnType == typeof(double) && !m.IsVirtual
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(StatType) }));
        if (getStat == null) { Plugin.L.LogWarning("BossHp: stat getter not found."); return false; }
        _getStat = (Func<Character, StatType, double>)Delegate.CreateDelegate(typeof(Func<Character, StatType, double>), getStat);

        _origUpdate = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(BossHPBarUI), "Update"), BarUpdate);
        return _origUpdate != null;
    }

    // ---------- per bar ----------

    private static IntPtr _barPtr;
    private static TMP_Text _label;
    private static string _lastText;
    private static Character _boss;

    private static void BarUpdate(IntPtr self, IntPtr mi)
    {
        _origUpdate(self, mi);
        try { AfterBarUpdate(self); }
        catch (Exception e) { Plugin.L.LogError($"BossHp: {e}"); _barPtr = self; _label = null; }
    }

    private static void AfterBarUpdate(IntPtr self)
    {
        if (self != _barPtr || _label == null || _label.WasCollected)
        {
            if (self == _barPtr && _label == null) return; // creation failed for this bar; don't retry every frame
            _barPtr = self;
            _label = CreateLabel(new BossHPBarUI(self));
            _lastText = null;
        }
        if (_label == null) return;

        IntPtr boss = Marshal.ReadIntPtr(self, _bossOffset);
        if (boss == IntPtr.Zero || !_show.Value) { SetText(""); return; }

        if (_boss == null || _boss.Pointer != boss) _boss = new Character(boss);
        long hp = Math.Max(0L, Marshal.ReadInt64(boss, _hpOffset));
        double max = _getStat(_boss, StatType.MaxHealth);
        if (max <= 0) { SetText(""); return; }

        double pct = Math.Clamp(hp / max * 100.0, 0, 100);
        SetText(_format.Value
            .Replace("{current}", Number(hp))
            .Replace("{max}", Number(max))
            .Replace("{percent}", pct >= 10 || pct == 0 ? $"{pct:0}%" : $"{pct:0.0}%"));
    }

    private static string Number(double v) => _abbreviate.Value ? Ui.Num(v) : v.ToString("N0");

    private static void SetText(string text)
    {
        if (text == _lastText) return;
        _lastText = text;
        _label.text = text;
    }

    /// <summary>A copy of the bar's name text (for the game's font), stretched over the bar and centred.</summary>
    private static TMP_Text CreateLabel(BossHPBarUI bar)
    {
        if (bar.NameText == null || bar.Fill == null) { Plugin.L.LogWarning("BossHp: boss bar has no name text or fill."); return null; }

        var barRect = bar.Fill.transform.parent;
        var existing = barRect.Find("DeskrawlQoL_BossHp");
        var go = existing != null
            ? existing.gameObject
            : UnityEngine.Object.Instantiate(bar.NameText.gameObject.Cast<UnityEngine.Object>(), barRect).Cast<GameObject>();
        go.name = "DeskrawlQoL_BossHp";

        var rt = go.transform.Cast<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.SetAsLastSibling(); // draw on top of the fill

        // Drop what made the original a *name* label: its localization (would overwrite our text) and
        // any layout participation (would shift the bar's own layout).
        var localized = go.GetComponent(Il2CppType.Of<LocalizedTMPText>());
        if (localized != null) UnityEngine.Object.DestroyImmediate(localized);
        var layout = go.GetComponent(Il2CppType.Of<LayoutElement>())?.Cast<LayoutElement>()
            ?? go.AddComponent(Il2CppType.Of<LayoutElement>()).Cast<LayoutElement>();
        layout.ignoreLayout = true;

        var label = go.GetComponent(Il2CppType.Of<TMP_Text>()).Cast<TMP_Text>();
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        label.text = "";

        Plugin.L.LogInfo($"BossHp: label added to '{barRect.name}' (bar size {barRect.Cast<RectTransform>().rect.size}, font {label.fontSize}).");
        return label;
    }
}
