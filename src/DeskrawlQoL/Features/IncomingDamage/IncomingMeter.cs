using System.Collections.Generic;
using UnityEngine;
using S = DeskrawlQoL.Features.IncomingDamage.IncomingDamageFeature;

namespace DeskrawlQoL.Features.IncomingDamage;

/// <summary>Damage taken by the player in one encounter. Times use scaled game time, so pauses don't count.</summary>
internal sealed class IncomingMeter
{
    /// <summary>Totals for one slice (a damage type, or crit/normal hits).</summary>
    internal sealed class Bucket
    {
        public double Taken, Raw, MaxHit;
        public int Hits;

        public void Add(long taken, long raw)
        {
            Taken += taken;
            Raw += raw;
            Hits++;
            if (taken > MaxHit) MaxHit = taken;
        }

        /// <summary>Share of the raw damage your defences (armor, resistances, shields) prevented.</summary>
        public double Prevented => Raw > 0 ? 1.0 - Taken / Raw : 0;
    }

    public static readonly IncomingMeter Current = new();

    public readonly Bucket All = new(), Crit = new(), Normal = new();
    public readonly SortedDictionary<int, Bucket> ByType = new(); // DamageType -> totals
    public double MinHit = double.MaxValue, PeakRolling;
    public int Avoided;
    public float Start = -1f, Last = -1f;

    private readonly Queue<(float t, long amt)> _window = new();
    private double _windowSum;

    public bool HasData => Start >= 0f;

    public void Reset()
    {
        All.Taken = All.Raw = All.MaxHit = 0; All.Hits = 0;
        Crit.Taken = Crit.Raw = Crit.MaxHit = 0; Crit.Hits = 0;
        Normal.Taken = Normal.Raw = Normal.MaxHit = 0; Normal.Hits = 0;
        ByType.Clear();
        MinHit = double.MaxValue; PeakRolling = 0; Avoided = 0;
        Start = Last = -1f;
        _window.Clear(); _windowSum = 0;
    }

    private void Touch(float now)
    {
        float timeout = S.CombatTimeout.Value;
        if (HasData && timeout > 0f && now - Last > timeout) Reset();
        if (!HasData) Start = now;
        Last = now;
    }

    public void AddHit(long taken, long raw, int type, bool crit)
    {
        float now = Time.time;
        Touch(now);
        All.Add(taken, raw);
        (crit ? Crit : Normal).Add(taken, raw);
        if (!ByType.TryGetValue(type, out var b)) ByType[type] = b = new Bucket();
        b.Add(taken, raw);
        if (taken < MinHit) MinHit = taken;
        _window.Enqueue((now, taken));
        _windowSum += taken;
    }

    public void AddAvoided()
    {
        Touch(Time.time);
        Avoided++;
    }

    public bool InCombat(float now) => HasData && (S.CombatTimeout.Value <= 0f || now - Last <= S.CombatTimeout.Value);

    public float Duration(float now)
    {
        if (!HasData) return 0f;
        return Mathf.Max((InCombat(now) ? now : Last) - Start, 1f);
    }

    public double Dtps(float now) => HasData ? All.Taken / Duration(now) : 0;

    public double RollingDtps(float now)
    {
        const float win = 5f;
        while (_window.Count > 0 && now - _window.Peek().t > win)
            _windowSum -= _window.Dequeue().amt;
        if (!HasData) return 0;
        double r = _windowSum / Mathf.Max(Mathf.Min(win, now - Start), 1f);
        if (now - Start >= win && r > PeakRolling) PeakRolling = r;
        return r;
    }
}
