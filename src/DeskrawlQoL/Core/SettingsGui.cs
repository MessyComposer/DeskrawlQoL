using System;
using BepInEx.Configuration;
using UnityEngine;

namespace DeskrawlQoL.Core;

/// <summary>
/// Minimal immediate-mode controls for the settings panel, laid out top to bottom. Drawn by hand
/// (styled labels + our own click handling) because parts of Unity's IMGUI are stripped from this build.
/// Call the same controls in the same order for every event; clicks act on MouseDown.
/// </summary>
internal sealed class SettingsGui
{
    private readonly Event _e;
    private readonly float _x, _width, _line;
    private readonly Styles _s;
    public float Y;

    internal sealed class Styles
    {
        public GUIStyle Label, Center, Header, Box, Check, Button, ButtonHover;

        public Styles(int size)
        {
            Label = Ui.Text(size, Color.white);
            Center = Ui.Text(size, Color.white, TextAnchor.MiddleCenter);
            Header = Ui.Text(size, new Color(1f, 0.8f, 0.4f), bold: true);
            Box = Ui.Fill(new Color(1f, 1f, 1f, 0.25f));
            Check = Ui.Fill(new Color(1f, 0.8f, 0.4f, 0.95f));
            Button = Ui.Fill(new Color(1f, 1f, 1f, 0.12f));
            ButtonHover = Ui.Fill(new Color(1f, 1f, 1f, 0.25f));
        }
    }

    public SettingsGui(Event e, float x, float y, float width, float line, Styles styles)
    {
        _e = e; _x = x; Y = y; _width = width; _line = line; _s = styles;
    }

    private bool Repaint => _e.type == EventType.Repaint;

    private bool Clicked(Rect r)
    {
        if (_e.type != EventType.MouseDown || _e.button != 0 || !r.Contains(_e.mousePosition)) return false;
        _e.Use();
        return true;
    }

    public void Header(string text)
    {
        Y += 4;
        if (Repaint) GUI.Label(new Rect(_x, Y, _width, _line), text, _s.Header);
        Y += _line;
    }

    public void Toggle(string label, ConfigEntry<bool> entry)
    {
        var row = new Rect(_x, Y, _width, _line);
        if (Clicked(row)) entry.Value = !entry.Value;
        if (Repaint)
        {
            float box = _line - 8;
            var outer = new Rect(_x + 4, Y + 4, box, box);
            GUI.Label(outer, "", _s.Box);
            if (entry.Value) GUI.Label(new Rect(outer.x + 3, outer.y + 3, box - 6, box - 6), "", _s.Check);
            GUI.Label(new Rect(_x + box + 12, Y, _width - box - 12, _line), label, _s.Label);
        }
        Y += _line;
    }

    public void Button(string label, Action onClick)
    {
        var r = new Rect(_x + 4, Y + 2, Mathf.Min(_width - 8, label.Length * _line * 0.55f + 24), _line - 2);
        if (Clicked(r)) onClick();
        if (Repaint)
        {
            GUI.Label(r, "", r.Contains(_e.mousePosition) ? _s.ButtonHover : _s.Button);
            GUI.Label(r, label, _s.Center);
        }
        Y += _line + 2;
    }

    public void Stepper(string label, ConfigEntry<int> entry, int min, int max)
    {
        float b = _line - 2;
        var plus = new Rect(_x + _width - b - 4, Y + 1, b, b);
        var minus = new Rect(plus.x - b * 2.2f, Y + 1, b, b);
        if (Clicked(minus)) entry.Value = Math.Max(min, entry.Value - 1);
        if (Clicked(plus)) entry.Value = Math.Min(max, entry.Value + 1);
        if (Repaint)
        {
            GUI.Label(new Rect(_x + 4, Y, _width, _line), label, _s.Label);
            GUI.Label(minus, "", minus.Contains(_e.mousePosition) ? _s.ButtonHover : _s.Button);
            GUI.Label(minus, "-", _s.Center);
            GUI.Label(new Rect(minus.xMax, Y, plus.x - minus.xMax, _line), entry.Value.ToString(), _s.Center);
            GUI.Label(plus, "", plus.Contains(_e.mousePosition) ? _s.ButtonHover : _s.Button);
            GUI.Label(plus, "+", _s.Center);
        }
        Y += _line;
    }
}
