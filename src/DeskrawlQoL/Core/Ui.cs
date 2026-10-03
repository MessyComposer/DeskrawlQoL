using System;
using UnityEngine;

namespace DeskrawlQoL.Core;

/// <summary>Shared IMGUI helpers. Only call from OnGUI.</summary>
internal static class Ui
{
    /// <summary>Formats big numbers as 1.23K / 45.6M / 789B ...</summary>
    public static string Num(double v)
    {
        string[] units = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc" };
        int u = 0;
        while (Math.Abs(v) >= 1000 && u < units.Length - 1) { v /= 1000; u++; }
        return u == 0 ? v.ToString("0") : v.ToString(Math.Abs(v) < 10 ? "0.00" : Math.Abs(v) < 100 ? "0.0" : "0") + units[u];
    }

    public static string Time(float seconds)
    {
        int t = Mathf.FloorToInt(seconds);
        return t >= 60 ? $"{t / 60}:{t % 60:00}" : $"{seconds:0.0}s";
    }

    /// <summary>
    /// A style that fills its rect with a solid colour. GUI.DrawTexture is stripped from this game
    /// build, so draw rectangles with <c>GUI.Label(rect, "", Ui.Fill(color))</c> instead.
    /// </summary>
    public static GUIStyle Fill(Color c)
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, c);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        var style = new GUIStyle();
        style.normal.background = tex;
        return style;
    }

    public static GUIStyle Text(int fontSize, Color color, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
    {
        var s = new GUIStyle(GUI.skin.label) { fontSize = fontSize, wordWrap = false, alignment = align };
        if (bold) s.fontStyle = FontStyle.Bold;
        s.normal.textColor = color;
        return s;
    }

    /// <summary>
    /// Lets the user drag a panel by its title bar. Call with the current event before drawing;
    /// writes the new position back to the config entries.
    /// </summary>
    public class Draggable
    {
        private bool _dragging;
        private Vector2 _offset;

        public bool IsDragging => _dragging;

        public void Handle(Event e, Rect handle, BepInEx.Configuration.ConfigEntry<float> x, BepInEx.Configuration.ConfigEntry<float> y)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && handle.Contains(e.mousePosition))
            {
                _dragging = true;
                _offset = e.mousePosition - new Vector2(x.Value, y.Value);
                e.Use();
            }
            else if (_dragging && e.type == EventType.MouseDrag)
            {
                var p = e.mousePosition - _offset;
                x.Value = Mathf.Clamp(p.x, 0, Screen.width - 50);
                y.Value = Mathf.Clamp(p.y, 0, Screen.height - 20);
                e.Use();
            }
            else if (_dragging && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
            {
                _dragging = false;
            }
        }
    }
}
