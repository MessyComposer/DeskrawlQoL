using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using DeskrawlQoL.Core;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace DeskrawlQoL.Features.KeepMenusOpen;

/// <summary>
/// Keeps the player's open menus (inventory, talents, paragon...) open through stage changes and death.
///
/// Game side (2026-10-09 build): every panel derives from PanelBase and closes through the virtual
/// PanelBase.Close. A static registry class (the one holding a HashSet&lt;PanelBase&gt;) has a close-all
/// method (lz.hdw) that the screen fade (HUD), the area change and a few others call directly.
/// Direct calls can't be hooked, so we hook Close and skip it when its caller is close-all, unless
/// close-all runs inside Esc handling (UIInputHandler.Update), a lost connection (NetworkStatusWidget.Update),
/// a save being loaded or a cutscene (coroutines of SaveSystem and CutsceneManager). Only panels the player
/// opens with a hotkey and that aren't town-only (UIInputHandler.Toggles) are kept; NPC panels still close.
/// </summary>
internal sealed unsafe class KeepMenusOpenFeature : IFeature
{
    public string Id => "KeepMenusOpen";
    public string Title => "Keep menus open";
    public string Description => "Keeps your open menus open when the stage changes or you die.";

    private static ConfigEntry<bool> _enabled;

    public void Bind(ConfigFile c)
    {
        _enabled = c.Bind(Id, "Enabled", true, "Keep menus like the inventory open when the stage changes or you die.");
    }

    public void DrawSettings(SettingsGui g) => g.Toggle("Keep menus open", _enabled);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidFn(IntPtr self, IntPtr mi);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte MoveNextFn(IntPtr self, IntPtr mi);

    private static VoidFn _origClose;
    private static IntPtr* _closeCaller;
    private static ulong _closeAll;
    private static int _allowedDepth; // > 0 while a close-all caller that should still close everything runs
    private static int _togglesOffset;

    public bool Install()
    {
        var registry = typeof(PanelBase).Assembly.GetTypes().FirstOrDefault(t =>
            t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Any(p => p.PropertyType.IsGenericType && p.PropertyType.Name == "HashSet`1"
                    && p.PropertyType.GetGenericArguments()[0] == typeof(PanelBase)));
        if (registry == null) return Fail("panel registry not found");

        // Close-all is the registry's parameterless static method that also hides the dialogue box.
        var hide = Il2CppHooks.NativeCode(Il2CppHooks.Method(typeof(DialogueUI), "Hide"));
        var closeAll = registry.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(void) && m.GetParameters().Length == 0)
            .Select(Il2CppHooks.NativeCode)
            .Where(code => GameCode.Calls(code, hide))
            .ToList();
        if (closeAll.Count != 1) return Fail($"{closeAll.Count} close-all candidates on {registry.Name}");
        _closeAll = GameCode.Function(closeAll[0]).start;

        // Close-all callers that should still close everything. Without the Esc one, Esc would stop working.
        if (!AllowUpdate(typeof(UIInputHandler))) return Fail("UIInputHandler.Update not found");
        AllowUpdate(typeof(NetworkStatusWidget));
        int coroutines = 0;
        foreach (var owner in new[] { typeof(SaveSystem), typeof(CutsceneManager) })
        {
            foreach (var t in owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                var moveNext = Il2CppHooks.Method(t, "MoveNext");
                if (moveNext == null || moveNext.ReturnType != typeof(bool) || !GameCode.Calls(Il2CppHooks.NativeCode(moveNext), closeAll[0])) continue;
                AllowCoroutine(moveNext);
                coroutines++;
            }
        }

        _togglesOffset = Il2CppHooks.FieldOffset<UIInputHandler>("Toggles", 0x20);
        _origClose = Il2CppHooks.HookVirtual<VoidFn>(Il2CppHooks.Method(typeof(PanelBase), "Close"), Close, out _closeCaller);
        Plugin.L.LogInfo($"KeepMenusOpen: close-all {registry.Name} at {GameCode.Rva(_closeAll)}, {coroutines} save/cutscene coroutine(s) still close menus.");
        return true;
    }

    private static bool Fail(string why)
    {
        Plugin.L.LogWarning($"KeepMenusOpen: {why}.");
        return false;
    }

    private static bool AllowUpdate(Type type)
    {
        var update = Il2CppHooks.Method(type, "Update");
        if (update == null) return false;
        VoidFn orig = null;
        orig = Il2CppHooks.HookMethodInfo<VoidFn>(update, (self, mi) =>
        {
            _allowedDepth++;
            try { orig(self, mi); }
            finally { _allowedDepth--; }
        });
        return orig != null;
    }

    private static void AllowCoroutine(MethodInfo moveNext)
    {
        MoveNextFn orig = null;
        MoveNextFn hook = (self, mi) =>
        {
            _allowedDepth++;
            try { return orig(self, mi); }
            finally { _allowedDepth--; }
        };
        // Unity may resume a coroutine through MoveNext's vtable slot or its MethodInfo; cover both.
        orig = Il2CppHooks.HookVirtual<MoveNextFn>(moveNext, hook);
        Il2CppHooks.HookMethodInfo<MoveNextFn>(moveNext, hook);
    }

    private static int _kept;
    private static bool _logged, _failed;

    private static void Close(IntPtr self, IntPtr mi)
    {
        IntPtr returnAddress = *_closeCaller; // read first: any later Close call overwrites it
        try
        {
            if (_enabled.Value && !_failed && ShouldKeep(self, returnAddress)) { _kept++; return; }
        }
        catch (Exception e) { _failed = true; Plugin.L.LogError($"KeepMenusOpen: {e}"); }
        _origClose(self, mi);
    }

    public void Update()
    {
        if (_kept == 0) return;
        string msg = $"KeepMenusOpen: kept {_kept} menu(s) open.";
        if (_logged) Plugin.L.LogDebug(msg);
        else { Plugin.L.LogInfo(msg); _logged = true; }
        _kept = 0;
    }

    private static bool ShouldKeep(IntPtr panel, IntPtr returnAddress)
    {
        ulong caller = GameCode.Caller(returnAddress);
        bool keep = caller == _closeAll && _allowedDepth == 0 && Keepable().Contains(panel);

        // Diagnostics for game updates: which game function closed an open menu.
        if (new PanelBase(panel).gameObject.activeSelf)
            Plugin.L.LogDebug($"KeepMenusOpen: Close {Il2CppHooks.ObjName(panel)} (frame {Time.frameCount}): caller {GameCode.Rva(caller)}"
                + $"{(caller == _closeAll ? " (close-all)" : "")}, allowed {_allowedDepth > 0}, keepable {Keepable().Contains(panel)} -> {(keep ? "KEEP" : "close")}.");
        return keep;
    }

    private static readonly HashSet<IntPtr> KeepableSet = new();
    private static int _keepableFrame = -1;
    private static bool _loggedKeepable;

    /// <summary>Panels with a hotkey that aren't town-only. Close-all closes every panel in one frame, so read once per frame.</summary>
    private static HashSet<IntPtr> Keepable()
    {
        if (Time.frameCount == _keepableFrame) return KeepableSet;
        _keepableFrame = Time.frameCount;
        KeepableSet.Clear();
        foreach (var handler in Resources.FindObjectsOfTypeAll(Il2CppType.Of<UIInputHandler>()))
        {
            IntPtr toggles = Marshal.ReadIntPtr(handler.Pointer, _togglesOffset);
            if (toggles == IntPtr.Zero) continue;
            // PanelToggle[]: elements from +0x20, 0x18 bytes each { Key Key; PanelBase Panel @+0x8; bool TownOnly @+0x10 }.
            long count = Marshal.ReadInt64(toggles, 0x18);
            for (int i = 0; i < count; i++)
            {
                IntPtr entry = toggles + 0x20 + i * 0x18;
                IntPtr p = Marshal.ReadIntPtr(entry, 0x8);
                if (p != IntPtr.Zero && Marshal.ReadByte(entry, 0x10) == 0) KeepableSet.Add(p);
            }
        }
        if (!_loggedKeepable)
        {
            _loggedKeepable = true;
            Plugin.L.LogInfo($"KeepMenusOpen: keepable menus: {string.Join(", ", KeepableSet.Select(Il2CppHooks.ObjName))}.");
        }
        return KeepableSet;
    }
}
