using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using S = DeskrawlQoL.Features.DpsMeter.DpsMeterFeature;

namespace DeskrawlQoL.Features.DpsMeter;

/// <summary>Accumulates damage for one encounter. Times use scaled game time, so pauses don't count.</summary>
internal sealed class Meter
{
    internal sealed class Source
    {
        public string Name;
        public double Total;
        public int Hits, Crits;
    }

    public static readonly Meter Current = new();

    public double Total, PeakRolling;
    public float Start = -1f, Last = -1f;
    public int Hits;
    public readonly Dictionary<string, Source> Sources = new();

    private readonly Queue<(float t, long amt)> _window = new();
    private double _windowSum;
    private float _lastDebugLog = -100f;

    public bool HasData => Start >= 0f;

    public void Reset()
    {
        Total = 0; Start = Last = -1f; PeakRolling = 0; Hits = 0;
        Sources.Clear(); _window.Clear(); _windowSum = 0;
    }

    public void Add(long amount, string source, bool crit)
    {
        float now = Time.time;
        float timeout = S.CombatTimeout.Value;
        if (HasData && timeout > 0f && now - Last > timeout) Reset();
        if (!HasData) Start = now;
        Last = now;

        Total += amount;
        Hits++;
        if (!Sources.TryGetValue(source, out var s)) Sources[source] = s = new Source { Name = source };
        s.Total += amount;
        s.Hits++;
        if (crit) s.Crits++;

        _window.Enqueue((now, amount));
        _windowSum += amount;

        if (S.DebugLog.Value && now - _lastDebugLog > 20f)
        {
            _lastDebugLog = now;
            Plugin.L.LogInfo($"[DpsMeter] total={Total:0} hits={Hits} dps={Dps(now):0} top sources: " +
                string.Join(", ", Sources.Values.OrderByDescending(x => x.Total).Take(6).Select(x => $"{x.Name}={x.Total:0}")));
        }
    }

    public bool InCombat(float now) => HasData && (S.CombatTimeout.Value <= 0f || now - Last <= S.CombatTimeout.Value);

    public float Duration(float now)
    {
        if (!HasData) return 0f;
        float end = InCombat(now) ? now : Last;
        return Mathf.Max(end - Start, 1f);
    }

    public double Dps(float now) => HasData ? Total / Duration(now) : 0;

    public double RollingDps(float now)
    {
        float win = Mathf.Max(S.RollingWindow.Value, 0.5f);
        while (_window.Count > 0 && now - _window.Peek().t > win)
            _windowSum -= _window.Dequeue().amt;
        if (!HasData) return 0;
        float elapsed = Mathf.Max(Mathf.Min(win, now - Start), 1f);
        double r = _windowSum / elapsed;
        if (now - Start >= win && r > PeakRolling) PeakRolling = r;
        return r;
    }
}
