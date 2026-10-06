using System.Collections.Generic;
using DeskrawlQoL.Core;
using UnityEngine;
using S = DeskrawlQoL.Features.IncomingDamage.IncomingDamageFeature;

namespace DeskrawlQoL.Features.IncomingDamage;

internal static class IncomingOverlay
{
    private static GUIStyle _label, _right, _title, _dim, _bg;
    private static int _styleFont = -1;
    private static readonly Ui.Draggable Drag = new();
    private static readonly Dictionary<int, GUIStyle> TypeBars = new();

    private static readonly Dictionary<int, Color> TypeColors = new()
    {
        [(int)DamageType.Physical] = new Color(0.75f, 0.75f, 0.75f, 0.45f),
        [(int)DamageType.Fire] = new Color(1f, 0.45f, 0.1f, 0.5f),
        [(int)DamageType.Cold] = new Color(0.4f, 0.75f, 1f, 0.5f),
        [(int)DamageType.Lightning] = new Color(1f, 0.9f, 0.2f, 0.5f),
        [(int)DamageType.Poison] = new Color(0.35f, 0.85f, 0.3f, 0.5f),
        [(int)DamageType.Arcane] = new Color(0.7f, 0.4f, 1f, 0.5f),
    };

    private static void EnsureStyles()
    {
        int size = Settings.FontSize.Value;
        if (_label != null && _styleFont == size) return;
        _styleFont = size;
        _label = Ui.Text(size, Color.white);
        _right = Ui.Text(size, Color.white, TextAnchor.MiddleRight);
        _dim = Ui.Text(size, new Color(1f, 1f, 1f, 0.65f));
        _title = Ui.Text(size, new Color(1f, 0.55f, 0.5f), bold: true);
        _bg ??= Ui.Fill(new Color(0f, 0f, 0f, 0.6f));
    }

    private static GUIStyle Bar(int type)
    {
        if (!TypeBars.TryGetValue(type, out var s))
            TypeBars[type] = s = Ui.Fill(TypeColors.TryGetValue(type, out var c) ? c : new Color(1f, 1f, 1f, 0.4f));
        return s;
    }

    public static void OnGUI()
    {
        if (!S.Visible.Value) return;
        var e = Event.current;
        EnsureStyles();
        float line = Settings.FontSize.Value + 6f;
        float width = Settings.FontSize.Value * 26f;
        float x = S.PosX.Value, y = S.PosY.Value;

        Drag.Handle(e, new Rect(x, y, width, line), S.PosX, S.PosY);
        if (e.type != EventType.Repaint) return;

        var m = IncomingMeter.Current;
        float now = Time.time;
        double rolling = m.RollingDtps(now);
        bool showCrit = S.ShowCrit.Value && m.All.Hits > 0;
        bool showTypes = S.ShowByType.Value && m.ByType.Count > 0;
        int lines = 6 + (showCrit ? 3 : 0) + (showTypes ? m.ByType.Count + 1 : 0);

        var panel = new Rect(x, y, width, lines * line + 8);
        ClickThrough.Block(Drag.IsDragging ? new Rect(0, 0, Screen.width, Screen.height) : panel);
        GUI.Label(panel, "", _bg);

        float pad = 6f, cx = x + pad, cw = width - 2 * pad, cy = y + 4;
        string state = !m.HasData ? "idle" : m.InCombat(now) ? "in combat" : "ended";
        GUI.Label(new Rect(cx, cy, cw, line), "Incoming damage", _title);
        GUI.Label(new Rect(cx, cy, cw, line), state, _right);
        cy += line;

        Row(ref cy, cx, cw, line, "Damage taken / s", Ui.Num(m.Dtps(now)));
        Row(ref cy, cx, cw, line, $"Last 5s / s  (peak {Ui.Num(m.PeakRolling)})", Ui.Num(m.InCombat(now) ? rolling : 0));
        Row(ref cy, cx, cw, line, $"Taken  ·  {m.All.Hits} hits, {m.Avoided} avoided", Ui.Num(m.All.Taken));
        Row(ref cy, cx, cw, line, "Smallest / biggest hit",
            m.All.Hits > 0 ? $"{Ui.Num(m.MinHit)} / {Ui.Num(m.All.MaxHit)}" : "-");
        Row(ref cy, cx, cw, line, "Prevented by defences", Pct(m.All.Prevented));

        if (showCrit)
        {
            cy += 4;
            GUI.Label(new Rect(cx, cy, cw, line), "Hits   (share of damage · hits · biggest · prevented)", _dim);
            cy += line;
            Split(ref cy, cx, cw, line, "Crit", m.Crit, m.All.Taken);
            Split(ref cy, cx, cw, line, "Normal", m.Normal, m.All.Taken);
        }

        if (showTypes)
        {
            cy += 4;
            GUI.Label(new Rect(cx, cy, cw, line), "By type   (per second · share · prevented)", _dim);
            cy += line;
            float dur = m.Duration(now);
            foreach (var (type, b) in m.ByType)
            {
                float frac = m.All.Taken > 0 ? (float)(b.Taken / m.All.Taken) : 0f;
                GUI.Label(new Rect(cx, cy + 2, cw * frac, line - 4), "", Bar(type));
                GUI.Label(new Rect(cx + 2, cy, cw, line), ((DamageType)type).ToString(), _label);
                GUI.Label(new Rect(cx, cy, cw - 2, line), $"{Ui.Num(b.Taken / dur)}/s  {frac * 100f:0}%  {Pct(b.Prevented)}", _right);
                cy += line;
            }
        }
    }

    private static void Split(ref float cy, float cx, float cw, float line, string name, IncomingMeter.Bucket b, double total)
    {
        string share = total > 0 ? $"{b.Taken / total * 100:0}%" : "-";
        Row(ref cy, cx, cw, line, name, b.Hits > 0 ? $"{share} · {b.Hits} · {Ui.Num(b.MaxHit)} · {Pct(b.Prevented)}" : "-");
    }

    private static string Pct(double v) => $"{v * 100:0}%";

    private static void Row(ref float cy, float cx, float cw, float line, string label, string value)
    {
        GUI.Label(new Rect(cx, cy, cw, line), label, _label);
        GUI.Label(new Rect(cx, cy, cw, line), value, _right);
        cy += line;
    }
}
