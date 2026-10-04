using System;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using DeskrawlQoL.Core;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace DeskrawlQoL.Features.AutoNextStage;

/// <summary>
/// Adds an "Auto next stage" toggle next to the game's auto-replay toggles (stage-complete screen and
/// combat settings). When on and a next stage exists, the stage-complete screen counts down (the game's
/// auto-replay delay) and presses Next Stage, so the game's own next-stage logic and guards run.
/// Without a next stage (or with the toggle off) the game's auto-replay behaves as usual.
///
/// Game side (2026-10-03 build): CombatMapCompletionUI.fth() fills the screen and stores the next map at
/// +0xD8; fti() arms auto-replay (flag +0xC8, timer +0xC4); Update() ticks it and replays.
/// CombatSettingPanel has no Update, so its toggle is handled from FrameHost's per-frame Update.
/// </summary>
internal sealed unsafe class AutoNextStageFeature : IFeature
{
    public string Id => "AutoNextStage";
    public string Description => "Adds an 'Auto next stage' toggle to the stage-complete screen and combat settings.";

    private static ConfigEntry<bool> _enabled;
    private static ConfigEntry<string> _label;

    public void Bind(ConfigFile c)
    {
        _enabled = c.Bind(Id, "Enabled", false, "State of the 'Auto next stage' toggle (also changed by clicking it in game).");
        _label = c.Bind(Id, "Label", "Auto next stage", "Text shown on the toggle.");
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidFn(IntPtr self, IntPtr mi);

    private static VoidFn _origUpdate;
    private static int _nextMapOffset, _replayArmedOffset, _replayTimerOffset, _gmStateOffset;
    private static IntPtr _gameManagerInstanceField, _settingsInstanceField;

    public bool Install()
    {
        _nextMapOffset = Il2CppHooks.FieldOffset<CombatMapCompletionUI>("oah", 0xD8);
        _replayTimerOffset = Il2CppHooks.FieldOffset<CombatMapCompletionUI>("oae", 0xC4);
        _replayArmedOffset = Il2CppHooks.FieldOffset<CombatMapCompletionUI>("oaf", 0xC8);
        _gmStateOffset = Il2CppHooks.FieldOffset<GameManager>("mve", 0xA8);
        _gameManagerInstanceField = IL2CPP.il2cpp_class_get_field_from_name(Il2CppHooks.ClassOf<GameManager>(), "<muy>k__BackingField");
        _settingsInstanceField = IL2CPP.il2cpp_class_get_field_from_name(Il2CppHooks.ClassOf<CombatSettingPanel>(), "<oak>k__BackingField");
        if (_settingsInstanceField == IntPtr.Zero) Plugin.L.LogWarning("AutoNextStage: combat settings panel not found; toggle only on the stage-complete screen.");

        _origUpdate = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(CombatMapCompletionUI), "Update"), CompletionUpdate);
        return _origUpdate != null;
    }

    // ---------- toggles (one per game screen, all mirroring _enabled) ----------

    private sealed class Slot
    {
        public IntPtr Owner;      // game UI instance the toggle was added to
        public bool Attempted;    // creation tried for this owner (don't retry every frame)
        public GameUi.ClonedToggle Clone;
        public bool Live => Clone != null && Clone.Live;
    }

    private static readonly Slot ScreenSlot = new(), SettingsSlot = new();

    private static void Ensure(Slot slot, IntPtr owner, Func<Toggle> original, bool above)
    {
        if (slot.Owner == owner && (slot.Live || slot.Attempted && slot.Clone == null)) return;
        slot.Owner = owner;
        slot.Attempted = true;
        slot.Clone = GameUi.CloneToggle(original(), "DeskrawlQoL_AutoNextStage", above);
        if (slot.Clone != null) slot.Clone.Toggle.isOn = _enabled.Value;
    }

    /// <summary>A click on either toggle changes the setting; everything else follows it.</summary>
    private static void SyncToggles()
    {
        foreach (var s in new[] { ScreenSlot, SettingsSlot })
        {
            if (!s.Live || s.Clone.Toggle.isOn == _enabled.Value) continue;
            _enabled.Value = s.Clone.Toggle.isOn;
            if (!_enabled.Value && _countdown >= 0f) CancelAndRestoreReplay();
            break;
        }
        foreach (var s in new[] { ScreenSlot, SettingsSlot })
            if (s.Live && s.Clone.Toggle.isOn != _enabled.Value) s.Clone.Toggle.isOn = _enabled.Value;

        if (SettingsSlot.Live) SettingsSlot.Clone.SetText(_label.Value);
        if (ScreenSlot.Live)
            ScreenSlot.Clone.SetText(_countdown >= 0f ? $"{_label.Value} ({Mathf.CeilToInt(Mathf.Max(_countdown, 0f))}s)" : _label.Value);
    }

    /// <summary>Per frame (FrameHost): the combat settings panel has no Update of its own.</summary>
    public void Update()
    {
        if (_settingsInstanceField == IntPtr.Zero) return;
        IntPtr instance;
        IL2CPP.il2cpp_field_static_get_value(_settingsInstanceField, &instance);
        IntPtr panel = instance;
        if (panel == IntPtr.Zero) return;
        Ensure(SettingsSlot, panel, () => new CombatSettingPanel(panel).AutoRerunToggle, above: false);
        SyncToggles();
    }

    // ---------- stage-complete screen ----------

    private static CombatMapCompletionUI _ui;
    private static float _countdown = -1f; // < 0: not counting

    private static void CompletionUpdate(IntPtr self, IntPtr mi)
    {
        try { BeforeGameUpdate(self); }
        catch (Exception e) { Plugin.L.LogError($"AutoNextStage: {e}"); _countdown = -1f; }
        _origUpdate(self, mi);
    }

    private static void BeforeGameUpdate(IntPtr self)
    {
        if (_ui == null || _ui.Pointer != self) _ui = new CombatMapCompletionUI(self);
        // Above the auto-replay toggle: below it overlaps the Replay button.
        Ensure(ScreenSlot, self, () => _ui.AutoReplayToggle, above: true);
        SyncToggles();

        bool shown = _ui.Panel != null && _ui.Panel.activeInHierarchy;
        if (!shown) { _countdown = -1f; return; }

        bool nextAvailable = Marshal.ReadIntPtr(self, _nextMapOffset) != IntPtr.Zero
            && _ui.NextStageButton != null && _ui.NextStageButton.gameObject.activeInHierarchy && _ui.NextStageButton.interactable;

        // Start counting when the screen opens, or when the toggle is switched on while it's open.
        if (_enabled.Value && nextAvailable && _countdown < 0f)
            _countdown = Mathf.Max(_ui.AutoReplayDelay, 0.5f);
        if (_countdown < 0f || !_enabled.Value) return;

        // We're handling this screen: keep the game's auto-replay disarmed.
        *((byte*)self + _replayArmedOffset) = 0;
        if (_ui.AutoReplayCountdownText != null && _ui.AutoReplayCountdownText.gameObject.activeSelf)
            _ui.AutoReplayCountdownText.gameObject.SetActive(false);

        _countdown -= Time.deltaTime;
        if (_countdown > 0f || !nextAvailable || !GamePlaying()) return;

        _countdown = -1f;
        SyncToggles(); // drop the countdown from the label
        Plugin.L.LogInfo("AutoNextStage: going to the next stage.");
        _ui.NextStageButton.onClick.Invoke(); // same as the player clicking it
    }

    /// <summary>Turned off mid-countdown: hand the screen back to the game's auto-replay (if that's on).</summary>
    private static void CancelAndRestoreReplay()
    {
        _countdown = -1f;
        if (_ui == null || _ui.WasCollected || _ui.AutoReplayToggle == null || !_ui.AutoReplayToggle.isOn) return;
        *(float*)((byte*)_ui.Pointer + _replayTimerOffset) = _ui.AutoReplayDelay;
        *((byte*)_ui.Pointer + _replayArmedOffset) = 1;
        _ui.AutoReplayCountdownText?.gameObject.SetActive(true);
    }

    private static bool GamePlaying()
    {
        if (_gameManagerInstanceField == IntPtr.Zero) return true;
        IntPtr gm;
        IL2CPP.il2cpp_field_static_get_value(_gameManagerInstanceField, &gm);
        return gm == IntPtr.Zero || Marshal.ReadInt32(gm, _gmStateOffset) == 1; // GameState.Playing
    }
}
