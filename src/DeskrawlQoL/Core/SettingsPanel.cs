using System;
using UnityEngine;

namespace DeskrawlQoL.Core;

/// <summary>
/// The settings panel (General.SettingsKey, Alt+Q by default): shows/hides each view, toggles their
/// options and resets their metrics. Each feature adds its own section via IFeature.DrawSettings.
/// </summary>
internal static class SettingsPanel
{
    private static bool _open;
    private static float _height = 200f; // measured on the previous frame
    private static int _styleFont = -1;
    private static SettingsGui.Styles _styles;
    private static GUIStyle _title, _bg, _close, _closeHover, _closeLabel;
    private static readonly Ui.Draggable Drag = new();

    public static void OnGUI()
    {
        var e = Event.current;
        if (Hotkey.Pressed(e, Settings.SettingsKey.Value)) { _open = !_open; e.Use(); }
        if (!_open) return;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape) { _open = false; return; }

        int size = Settings.FontSize.Value;
        if (_styles == null || _styleFont != size)
        {
            _styleFont = size;
            _styles = new SettingsGui.Styles(size);
            _title = Ui.Text(size + 1, Color.white, bold: true);
            _closeLabel = Ui.Text(size, Color.white, TextAnchor.MiddleCenter);
            _bg ??= Ui.Fill(new Color(0.05f, 0.05f, 0.07f, 0.92f));
            _close ??= Ui.Fill(new Color(1f, 1f, 1f, 0.12f));
            _closeHover ??= Ui.Fill(new Color(1f, 0.3f, 0.3f, 0.5f));
        }

        float line = size + 8f;
        float width = size * 20f;
        if (Settings.PanelX.Value < 0f) Settings.PanelX.Value = (Screen.width - width) / 2f;
        if (Settings.PanelY.Value < 0f) Settings.PanelY.Value = Mathf.Max(20f, (Screen.height - _height) / 2f);
        float x = Settings.PanelX.Value, y = Settings.PanelY.Value;

        var titleBar = new Rect(x, y, width, line + 4);
        var close = new Rect(x + width - line - 2, y + 2, line, line);
        if (e.type == EventType.MouseDown && e.button == 0 && close.Contains(e.mousePosition)) { _open = false; e.Use(); return; }
        Drag.Handle(e, titleBar, Settings.PanelX, Settings.PanelY);

        var panel = new Rect(x, y, width, _height);
        ClickThrough.Block(Drag.IsDragging ? new Rect(0, 0, Screen.width, Screen.height) : panel);
        if (e.type == EventType.Repaint)
        {
            GUI.Label(panel, "", _bg);
            GUI.Label(new Rect(x + 8, y + 2, width, line), "Deskrawl QoL", _title);
            GUI.Label(close, "", close.Contains(e.mousePosition) ? _closeHover : _close);
            GUI.Label(close, "x", _closeLabel);
        }

        var g = new SettingsGui(e, x + 8, y + line + 4, width - 16, line, _styles);
        g.Header("General");
        g.Stepper("Font size", Settings.FontSize, 9, 28);
        g.Button("Reset all metrics", ResetAll);

        foreach (var f in Plugin.Features)
        {
            g.Header(f.Title);
            try { f.DrawSettings(g); }
            catch (Exception ex) { Plugin.L.LogError($"{f.Id}.DrawSettings: {ex}"); }
        }
        _height = g.Y - y + 8;

        // Swallow other clicks on the panel so they don't fall through to the game's UI.
        if (e.type == EventType.MouseDown && panel.Contains(e.mousePosition)) e.Use();
    }

    private static void ResetAll()
    {
        foreach (var f in Plugin.Features)
        {
            try { f.Reset(); }
            catch (Exception ex) { Plugin.L.LogError($"{f.Id}.Reset: {ex}"); }
        }
    }
}
