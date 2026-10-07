using System;
using DeskrawlQoL.Core;
using UnityEngine;
using S = DeskrawlQoL.Features.GoldXp.GoldXpFeature;

namespace DeskrawlQoL.Features.GoldXp;

internal static class GoldXpOverlay
{
    private static GUIStyle _label, _right, _title, _bg;
    private static int _styleFont = -1;
    private static readonly Ui.Draggable Drag = new();

    private static void EnsureStyles()
    {
        if (_label != null && _styleFont == Settings.FontSize.Value) return;
        _styleFont = Settings.FontSize.Value;
        _label = Ui.Text(_styleFont, Color.white);
        _right = Ui.Text(_styleFont, Color.white, TextAnchor.MiddleRight);
        _title = Ui.Text(_styleFont, new Color(1f, 0.85f, 0.35f), bold: true);
        _bg ??= Ui.Fill(new Color(0f, 0f, 0f, 0.6f));
    }

    public static void OnGUI()
    {
        var e = Event.current;
        if (Hotkey.Pressed(e, S.ToggleKey.Value)) { S.Visible.Value = !S.Visible.Value; e.Use(); }
        else if (Hotkey.Pressed(e, S.ResetKey.Value)) { S.ResetSession(); e.Use(); }
        if (!S.Visible.Value) return;

        EnsureStyles();
        float line = Settings.FontSize.Value + 6f;
        float width = Settings.FontSize.Value * 20f;
        float x = S.PosX.Value, y = S.PosY.Value;

        Drag.Handle(e, new Rect(x, y, width, line), S.PosX, S.PosY);
        if (e.type != EventType.Repaint) return;

        float elapsed = S.SessionStart < 0f ? 0f : Time.realtimeSinceStartup - S.SessionStart;
        double hours = Math.Max(elapsed, 1f) / 3600.0;
        double xpPerHour = S.XpGained / hours;

        var panel = new Rect(x, y, width, 6 * line + 8);
        ClickThrough.Block(Drag.IsDragging ? new Rect(0, 0, Screen.width, Screen.height) : panel);
        GUI.Label(panel, "", _bg);

        float pad = 6f, cx = x + pad, cw = width - 2 * pad, cy = y + 4;
        GUI.Label(new Rect(cx, cy, cw, line), "Gold / XP", _title);
        GUI.Label(new Rect(cx, cy, cw, line), string.IsNullOrEmpty(S.ResetKey.Value) ? "" : $"[{S.ResetKey.Value}] reset", _right);
        cy += line;

        Row(ref cy, cx, cw, line, "Gold / hour", Ui.Num(S.GoldGained / hours));
        Row(ref cy, cx, cw, line, "XP / hour", Ui.Num(xpPerHour));
        Row(ref cy, cx, cw, line, $"Gained: {Ui.Num(S.GoldGained)} gold", $"{Ui.Num(S.XpGained)} XP");
        Row(ref cy, cx, cw, line, $"Level {S.Level + 1} in", TimeToLevel(xpPerHour));
        Row(ref cy, cx, cw, line, "Session", Ui.Time(elapsed));
    }

    private static string TimeToLevel(double xpPerHour)
    {
        long remaining = S.XpToLevel - S.CurrentXp;
        if (S.XpToLevel <= 0 || remaining <= 0 || xpPerHour <= 0) return "-";
        return Ui.Time((float)(remaining / xpPerHour * 3600.0));
    }

    private static void Row(ref float cy, float cx, float cw, float line, string label, string value)
    {
        GUI.Label(new Rect(cx, cy, cw, line), label, _label);
        GUI.Label(new Rect(cx, cy, cw, line), value, _right);
        cy += line;
    }
}
