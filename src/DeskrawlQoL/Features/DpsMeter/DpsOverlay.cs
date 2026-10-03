using System.Collections.Generic;
using System.Linq;
using DeskrawlQoL.Core;
using UnityEngine;
using S = DeskrawlQoL.Features.DpsMeter.DpsMeterFeature;

namespace DeskrawlQoL.Features.DpsMeter;

internal static class DpsOverlay
{
    private static GUIStyle _label, _right, _title, _bg, _bar;
    private static int _styleFont = -1;
    private static readonly Ui.Draggable Drag = new();

    private static void EnsureStyles()
    {
        if (_label != null && _styleFont == S.FontSize.Value) return;
        _styleFont = S.FontSize.Value;
        _label = Ui.Text(_styleFont, Color.white);
        _right = Ui.Text(_styleFont, Color.white, TextAnchor.MiddleRight);
        _title = Ui.Text(_styleFont, new Color(1f, 0.8f, 0.4f), bold: true);
        _bg ??= Ui.Fill(new Color(0f, 0f, 0f, 0.6f));
        _bar ??= Ui.Fill(new Color(0.85f, 0.35f, 0.2f, 0.45f));
    }

    public static void OnGUI()
    {
        var e = Event.current;
        if (Hotkey.Pressed(e, S.ToggleKey.Value)) { S.Visible.Value = !S.Visible.Value; e.Use(); }
        else if (Hotkey.Pressed(e, S.ResetKey.Value)) { Meter.Current.Reset(); e.Use(); }
        else if (Hotkey.Pressed(e, S.BreakdownKey.Value)) { S.ShowBreakdown.Value = !S.ShowBreakdown.Value; e.Use(); }
        if (!S.Visible.Value) return;

        EnsureStyles();
        float line = S.FontSize.Value + 6f;
        float width = S.FontSize.Value * 24f;
        float x = S.PosX.Value, y = S.PosY.Value;

        Drag.Handle(e, new Rect(x, y, width, line), S.PosX, S.PosY);
        if (e.type != EventType.Repaint) return;

        var m = Meter.Current;
        float now = Time.time;
        double rolling = m.RollingDps(now);
        bool showBreak = S.ShowBreakdown.Value && m.Sources.Count > 0;
        var rows = showBreak
            ? m.Sources.Values.OrderByDescending(s => s.Total).Take(Mathf.Max(1, S.MaxRows.Value)).ToList()
            : new List<Meter.Source>();

        int lines = 5 + (showBreak ? rows.Count + 1 : 0);
        var panel = new Rect(x, y, width, lines * line + 8);
        // While dragging, keep the whole window solid so a fast drag can't outrun the panel and lose the mouse-up.
        ClickThrough.Block(Drag.IsDragging ? new Rect(0, 0, Screen.width, Screen.height) : panel);
        GUI.Label(panel, "", _bg);

        float pad = 6f, cx = x + pad, cw = width - 2 * pad;
        float cy = y + 4;
        string state = !m.HasData ? "idle" : m.InCombat(now) ? "in combat" : "ended";
        GUI.Label(new Rect(cx, cy, cw, line), "DPS Meter", _title);
        GUI.Label(new Rect(cx, cy, cw, line), $"{state}  [{S.ResetKey.Value}] reset", _right);
        cy += line;

        Row(ref cy, cx, cw, line, "Encounter DPS", Ui.Num(m.Dps(now)));
        Row(ref cy, cx, cw, line, $"Last {S.RollingWindow.Value:0}s DPS  (peak {Ui.Num(m.PeakRolling)})", Ui.Num(m.InCombat(now) ? rolling : 0));
        Row(ref cy, cx, cw, line, "Total damage", Ui.Num(m.Total));
        Row(ref cy, cx, cw, line, $"Duration  ·  {m.Hits} hits", Ui.Time(m.Duration(now)));

        if (!showBreak) return;
        cy += 4;
        float dur = m.Duration(now);
        foreach (var s in rows)
        {
            float frac = m.Total > 0 ? (float)(s.Total / m.Total) : 0f;
            GUI.Label(new Rect(cx, cy + 2, cw * frac, line - 4), "", _bar);
            string crit = s.Crits > 0 ? $" {100f * s.Crits / s.Hits:0}%c" : "";
            GUI.Label(new Rect(cx + 2, cy, cw, line), s.Name, _label);
            GUI.Label(new Rect(cx, cy, cw - 2, line), $"{Ui.Num(s.Total / dur)}/s  {frac * 100f:0}%{crit}", _right);
            cy += line;
        }
    }

    private static void Row(ref float cy, float cx, float cw, float line, string label, string value)
    {
        GUI.Label(new Rect(cx, cy, cw, line), label, _label);
        GUI.Label(new Rect(cx, cy, cw, line), value, _right);
        cy += line;
    }
}
