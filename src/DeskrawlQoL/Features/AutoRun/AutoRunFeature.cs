using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using DeskrawlQoL.Core;
using UnityEngine;

namespace DeskrawlQoL.Features.AutoRun;

/// <summary>
/// Auto-run tweaks on the stage-complete and mythic rift completion screens:
///  - A configurable auto-run countdown (0 = instant), applied to the game's own auto-run on both screens.
///  - An "Auto next tier" toggle on the mythic rift completion screen. The game's auto-run "Next Stage" mode
///    doesn't apply to mythic rifts; there it only reruns the same tier. When on and the next tier is
///    available, the screen counts down and presses Next Tier, so the game's own next-tier logic and guards run.
///
/// Game side (2026-10-10 build): each screen's Update ticks its auto-run countdown while the game is Playing
/// and acts when it runs out. Stage complete (CombatMapCompletionUI): timer +0xC4, armed flag +0xC8, armed from
/// AutoReplayDelay. Mythic (MythicCompletionUI): timer +0xE4, armed flag +0xE8, armed from CountdownDuration (8 s).
/// </summary>
internal sealed unsafe class AutoRunFeature : IFeature
{
    public string Id => "AutoRun";
    public string Title => "Auto-run";
    public string Description => "Configurable auto-run countdown, and an 'Auto next tier' toggle for mythic rifts.";

    private static ConfigEntry<bool> _nextTier;
    private static ConfigEntry<int> _countdown;
    private static ConfigEntry<string> _label;

    private const int MaxCountdown = 8;

    public void Bind(ConfigFile c)
    {
        _countdown = c.Bind(Id, "CountdownSeconds", 3,
            new ConfigDescription("Seconds before auto-run moves on (rerun, next stage or next tier). 0 = instantly.",
                new AcceptableValueRange<int>(0, MaxCountdown)));
        _nextTier = c.Bind(Id, "AutoNextTier", false, "State of the 'Auto next tier' toggle on the mythic rift completion screen (also changed by clicking it in game).");
        _label = c.Bind(Id, "AutoNextTierLabel", "Auto next tier", "Text shown on the toggle.");
    }

    public void DrawSettings(SettingsGui g)
    {
        g.Stepper("Countdown (s, 0 = instant)", _countdown, 0, MaxCountdown);
        g.Toggle("Auto next tier (mythic rift)", _nextTier);
    }

    private static float Countdown => Mathf.Clamp(_countdown.Value, 0, MaxCountdown);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidFn(IntPtr self, IntPtr mi);

    private static VoidFn _origStageUpdate, _origMythicUpdate;
    private static PropertyInfo _gameManager;
    private static MethodInfo _gameState;
    private static GameCountdown _stage, _mythic;

    public bool Install()
    {
        _stage = new GameCountdown("stage",
            Il2CppHooks.FieldOffset<CombatMapCompletionUI>("oin", 0xC4),
            Il2CppHooks.FieldOffset<CombatMapCompletionUI>("oio", 0xC8));
        _mythic = new GameCountdown("mythic",
            Il2CppHooks.FieldOffset<MythicCompletionUI>("paw", 0xE4),
            Il2CppHooks.FieldOffset<MythicCompletionUI>("pax", 0xE8));

        // GameManager's static instance, and its GameState getter (the method, not the property accessor).
        _gameManager = typeof(GameManager).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .FirstOrDefault(p => p.PropertyType == typeof(GameManager) && p.GetGetMethod(true) != null);
        _gameState = typeof(GameManager).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .FirstOrDefault(m => m.ReturnType == typeof(GameState) && m.GetParameters().Length == 0 && !m.IsSpecialName);
        if (_gameManager == null || _gameState == null) Plugin.L.LogWarning("AutoRun: game state not found; not checking it before moving on.");

        _origStageUpdate = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(CombatMapCompletionUI), "Update"), StageUpdate);
        _origMythicUpdate = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(MythicCompletionUI), "Update"), MythicUpdate);
        return _origStageUpdate != null || _origMythicUpdate != null;
    }

    /// <summary>
    /// A screen's own auto-run countdown, capped at our setting. Capped every frame: the game re-arms it
    /// without disarming first (e.g. when the auto-run mode is changed on the screen), and it only counts down.
    /// </summary>
    private sealed class GameCountdown
    {
        private readonly string _name;
        private readonly int _timer, _armed;
        private bool _logged;

        public GameCountdown(string name, int timerOffset, int armedOffset) { _name = name; _timer = timerOffset; _armed = armedOffset; }

        public void Apply(IntPtr screen)
        {
            if (*((byte*)screen + _armed) == 0) return;
            float* timer = (float*)((byte*)screen + _timer);
            if (*timer <= Countdown) return;
            string msg = $"AutoRun: {_name} countdown {*timer:0.#}s -> {Countdown}s.";
            if (_logged) Plugin.L.LogDebug(msg);
            else { Plugin.L.LogInfo(msg); _logged = true; }
            *timer = Countdown;
        }

        public void Disarm(IntPtr screen) => *((byte*)screen + _armed) = 0;

        public void Arm(IntPtr screen)
        {
            *(float*)((byte*)screen + _timer) = Countdown;
            *((byte*)screen + _armed) = 1;
        }
    }

    // ---------- stage-complete screen ----------

    private static void StageUpdate(IntPtr self, IntPtr mi)
    {
        try { _stage.Apply(self); }
        catch (Exception e) { Plugin.L.LogError($"AutoRun: {e}"); }
        _origStageUpdate(self, mi);
    }

    // ---------- mythic rift completion screen ----------

    private static IntPtr _toggleOwner;
    private static bool _toggleAttempted, _lastToggleState;
    private static GameUi.ClonedToggle _toggle;
    private static MythicCompletionUI _ui;
    private static float _nextTierIn = -1f; // < 0: not counting

    private static void MythicUpdate(IntPtr self, IntPtr mi)
    {
        try { BeforeMythicUpdate(self); }
        catch (Exception e) { Plugin.L.LogError($"AutoRun: {e}"); _nextTierIn = -1f; }
        _origMythicUpdate(self, mi);
    }

    private static void EnsureToggle(IntPtr owner)
    {
        if (_toggleOwner == owner && (_toggle != null && _toggle.Live || _toggleAttempted && _toggle == null)) return;
        _toggleOwner = owner;
        _toggleAttempted = true;
        // Above the auto-rerun toggle (below it can overlap the buttons).
        _toggle = GameUi.CloneToggle(_ui.AutoRerunToggle, "DeskrawlQoL_AutoNextTier", above: true);
        if (_toggle != null) _toggle.Toggle.isOn = _nextTier.Value;
    }

    /// <summary>A click on the toggle changes the setting; otherwise the toggle follows the setting.</summary>
    private static void SyncToggle()
    {
        if (_toggle == null || !_toggle.Live) return;
        if (_toggle.Toggle.isOn != _lastToggleState)
        {
            _nextTier.Value = _toggle.Toggle.isOn;
            if (!_nextTier.Value && _nextTierIn >= 0f) CancelAndRestoreRerun();
        }
        if (_toggle.Toggle.isOn != _nextTier.Value) _toggle.Toggle.isOn = _nextTier.Value;
        _lastToggleState = _toggle.Toggle.isOn;
        _toggle.SetText(_nextTierIn > 0f ? $"{_label.Value} ({Mathf.CeilToInt(_nextTierIn)}s)" : _label.Value);
    }

    private static void BeforeMythicUpdate(IntPtr self)
    {
        if (_ui == null || _ui.Pointer != self) { _ui = new MythicCompletionUI(self); _lastToggleState = _nextTier.Value; }
        EnsureToggle(self);

        bool shown = _ui.Panel != null && _ui.Panel.activeInHierarchy;
        if (!shown) { _nextTierIn = -1f; SyncToggle(); return; }

        var next = _ui.NextTierButton;
        bool nextAvailable = next != null && next.gameObject.activeInHierarchy && next.interactable;

        // Start counting when the screen opens, or when the toggle is switched on while it's open.
        if (_nextTier.Value && nextAvailable && _nextTierIn < 0f) _nextTierIn = Countdown;
        // Next tier went away mid-countdown (e.g. not unlocked after all): give the screen back to the game.
        if (_nextTierIn >= 0f && !nextAvailable) CancelAndRestoreRerun();
        SyncToggle();
        if (_nextTierIn < 0f || !_nextTier.Value) { _mythic.Apply(self); return; }

        // We're handling this screen: keep the game's auto-rerun disarmed.
        _mythic.Disarm(self);
        if (_ui.CountdownText != null && _ui.CountdownText.gameObject.activeSelf)
            _ui.CountdownText.gameObject.SetActive(false);

        _nextTierIn -= Time.deltaTime;
        if (_nextTierIn > 0f || !GamePlaying()) return;

        _nextTierIn = -1f;
        SyncToggle(); // drop the countdown from the label
        Plugin.L.LogInfo("AutoRun: going to the next tier.");
        next.onClick.Invoke(); // same as the player clicking it
    }

    /// <summary>Turned off or no next tier mid-countdown: hand the screen back to the game's auto-rerun (if that's on).</summary>
    private static void CancelAndRestoreRerun()
    {
        _nextTierIn = -1f;
        if (_ui == null || _ui.WasCollected || _ui.AutoRerunToggle == null || !_ui.AutoRerunToggle.isOn) return;
        _mythic.Arm(_ui.Pointer);
        _ui.CountdownText?.gameObject.SetActive(true);
    }

    private static bool GamePlaying()
    {
        if (_gameManager == null || _gameState == null) return true;
        var gm = _gameManager.GetValue(null);
        return gm == null || (GameState)_gameState.Invoke(gm, null) == GameState.Playing;
    }
}
