using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BepInEx.Configuration;
using UnityEngine;

namespace DeskrawlQoL.Core;

/// <summary>
/// Deskrawl's overlay mode is a transparent desktop window that lets clicks through to the desktop
/// unless the cursor is over game UI: each frame TransparentWindow.Update reads MouseRaycaster's
/// "pointer over UI" flag and toggles WS_EX_TRANSPARENT on the window. Our IMGUI panels are invisible
/// to that check, so features register the rects they draw with <see cref="Block"/>, and when the
/// cursor is over one of them we:
///  - raise MouseRaycaster's flag (so the click doesn't also act on the game world), and
///  - after TransparentWindow.Update, make the window solid ourselves, keeping the game's own
///    "is click-through" field in sync so it re-applies transparency once the cursor leaves.
/// </summary>
internal static unsafe class ClickThrough
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VoidFn(IntPtr self, IntPtr mi);

    private const int GWL_EXSTYLE = -20;
    private const uint WS_EX_TRANSPARENT = 0x20;

    private static VoidFn _origRaycasterUpdate, _origWindowUpdate;
    private static int _overUiOffset, _hwndOffset, _clickThroughOffset;
    private static List<Rect> _building = new(), _active = new();
    private static bool _overPanel;
    private static ConfigEntry<bool> _debug;
    private static float _lastDebugLog;

    public static void Install(ConfigFile config)
    {
        _debug = config.Bind("Debug", "LogClickThrough", false,
            "Log once a second whether the cursor is over a mod panel and what the game window's click-through state is.");

        // Obfuscated names from the 2026-10-03 build, with their known offsets as fallback.
        _overUiOffset = Il2CppHooks.FieldOffset<MouseRaycaster>("<nak>k__BackingField", 0x78);
        _hwndOffset = Il2CppHooks.FieldOffset<TransparentWindow>("nnz", 0x20);
        _clickThroughOffset = Il2CppHooks.FieldOffset<TransparentWindow>("noa", 0x28);

        _origRaycasterUpdate = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(MouseRaycaster), "Update"), RaycasterUpdate);
        _origWindowUpdate = Il2CppHooks.HookMethodInfo<VoidFn>(Il2CppHooks.Method(typeof(TransparentWindow), "Update"), WindowUpdate);
    }

    /// <summary>Mark a GUI-space rect as solid for this frame. Call during the Repaint event.</summary>
    public static void Block(Rect r)
    {
        if (Event.current.type == EventType.Repaint) _building.Add(r);
    }

    /// <summary>Called by FrameHost after all features drew a Repaint pass.</summary>
    internal static void EndRepaint()
    {
        (_active, _building) = (_building, _active);
        _building.Clear();
    }

    // MouseRaycaster runs early in the frame (DefaultExecutionOrder -100), before TransparentWindow.
    private static void RaycasterUpdate(IntPtr self, IntPtr mi)
    {
        _origRaycasterUpdate(self, mi);
        if (_overPanel) *((byte*)self + _overUiOffset) = 1;
    }

    private static void WindowUpdate(IntPtr self, IntPtr mi)
    {
        _origWindowUpdate(self, mi);
        try
        {
            IntPtr hwnd = *(IntPtr*)((byte*)self + _hwndOffset);
            _overPanel = hwnd != IntPtr.Zero && _active.Count > 0 && CursorOverPanel(hwnd, out var cursor);

            uint style = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (_overPanel && (style & WS_EX_TRANSPARENT) != 0)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, style & ~WS_EX_TRANSPARENT);
                *((byte*)self + _clickThroughOffset) = 0; // game's own state, so it restores transparency later
            }

            if (_debug.Value && Time.unscaledTime - _lastDebugLog > 1f)
            {
                _lastDebugLog = Time.unscaledTime;
                GetCursorPos(out var sp);
                var cp = sp;
                ScreenToClient(hwnd, ref cp);
                Plugin.L.LogInfo($"[ClickThrough] hwnd=0x{hwnd:X} cursor screen=({sp.X},{sp.Y}) client=({cp.X},{cp.Y}) " +
                    $"screenSize={Screen.width}x{Screen.height} panels={_active.Count} first={(_active.Count > 0 ? _active[0].ToString() : "-")} " +
                    $"overPanel={_overPanel} exStyle=0x{GetWindowLong(hwnd, GWL_EXSTYLE):X} gameClickThrough={*((byte*)self + _clickThroughOffset)}");
            }
        }
        catch (Exception e) { Plugin.L.LogError(e); _active.Clear(); _overPanel = false; }
    }

    private static bool CursorOverPanel(IntPtr hwnd, out Vector2 cursor)
    {
        cursor = default;
        if (!GetCursorPos(out var p) || !ScreenToClient(hwnd, ref p)) return false;
        cursor = new Vector2(p.X, p.Y); // client area, y down: same as IMGUI coordinates
        foreach (var r in _active)
            if (r.Contains(cursor)) return true;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT p);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern uint GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(IntPtr hwnd, int index, uint value);
}
