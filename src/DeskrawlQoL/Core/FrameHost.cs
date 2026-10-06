using System;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using UnityEngine;

namespace DeskrawlQoL.Core;

/// <summary>
/// Gives features a per-frame Update and OnGUI.
///
/// Injecting our own MonoBehaviour crashes on this Unity build, so we borrow an existing game
/// component that has OnGUI (GUIControls, an unused crafting debug helper), put one on a hidden
/// GameObject, and replace its Awake/Update/OnGUI for that instance only.
/// </summary>
internal static class FrameHost
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidFn(IntPtr self, IntPtr mi);

    private static VoidFn _origCursorUpdate, _origGameUpdate, _origHostAwake, _origHostUpdate, _origHostOnGUI;
    private static IntPtr _host;
    private static bool _creating, _hostAttempted;
    // A feature that throws every frame would flood the log; stop calling it after the first error.
    private static readonly System.Collections.Generic.HashSet<IFeature> Failed = new();

    public static void Install(ConfigFile config)
    {
        var disable = config.Bind("Debug", "DisableFrameHost", false,
            "Don't create the overlay/update host (all overlays and hotkeys stop working). Use to narrow down crashes after a game update.");
        if (disable.Value || Plugin.Features.Count == 0) return;

        _origHostAwake = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(GUIControls), "Awake"), HostAwake);
        _origHostUpdate = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(GUIControls), "Update"), HostUpdate);
        _origHostOnGUI = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(GUIControls), "OnGUI"), HostOnGUI);

        // Components can't be created while BepInEx's chainloader runs, so create the host on the first game frame.
        _origCursorUpdate = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(CursorManager), "Update"), CursorUpdate);
        _origGameUpdate = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(GameManager), "Update"), GameUpdate);
        ClickThrough.Install(config);
    }

    private static void EnsureHost()
    {
        if (_hostAttempted) return;
        _hostAttempted = true;
        try
        {
            var go = new GameObject("DeskrawlQoL");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _creating = true;
            try { _host = go.AddComponent<GUIControls>().Pointer; }
            finally { _creating = false; }
        }
        catch (Exception e) { Plugin.L.LogError(e); }
    }

    private static void CursorUpdate(IntPtr self, IntPtr mi)
    {
        _origCursorUpdate(self, mi);
        EnsureHost();
    }

    private static void GameUpdate(IntPtr self, IntPtr mi)
    {
        _origGameUpdate(self, mi);
        EnsureHost();
    }

    private static void HostAwake(IntPtr self, IntPtr mi)
    {
        if (_creating || self == _host) return;
        _origHostAwake(self, mi);
    }

    private static void HostUpdate(IntPtr self, IntPtr mi)
    {
        if (self != _host) { _origHostUpdate(self, mi); return; }
        GameKeys.Update();
        foreach (var f in Plugin.Features)
        {
            if (Failed.Contains(f)) continue;
            try { f.Update(); }
            catch (Exception e) { Failed.Add(f); Plugin.L.LogError($"{f.Id}.Update failed, feature stopped: {e}"); }
        }
    }

    private static void HostOnGUI(IntPtr self, IntPtr mi)
    {
        if (self != _host) { _origHostOnGUI(self, mi); return; }

        // The settings panel sits on top: it handles input first and paints last.
        bool repaint = Event.current.type == EventType.Repaint;
        if (!repaint) DrawSettingsPanel();
        foreach (var f in Plugin.Features)
        {
            if (Failed.Contains(f)) continue;
            try { f.OnGUI(); }
            catch (Exception e) { Failed.Add(f); Plugin.L.LogError($"{f.Id}.OnGUI failed, feature stopped: {e}"); }
        }
        if (repaint)
        {
            DrawSettingsPanel();
            ClickThrough.EndRepaint();
        }
    }

    private static void DrawSettingsPanel()
    {
        try { SettingsPanel.OnGUI(); }
        catch (Exception e) { Plugin.L.LogError($"Settings panel: {e}"); }
    }
}
