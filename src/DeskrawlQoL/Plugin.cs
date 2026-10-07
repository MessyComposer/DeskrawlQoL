using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DeskrawlQoL.Core;
using DeskrawlQoL.Features.BossHp;
using DeskrawlQoL.Features.DpsMeter;
using DeskrawlQoL.Features.GoldXp;
using DeskrawlQoL.Features.IncomingDamage;

namespace DeskrawlQoL;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "deskrawl.qol";
    public const string Name = "Deskrawl QoL";
    public const string Version = "1.3.1";

    internal static ManualLogSource L;
    internal static readonly List<IFeature> Features = new();

    public override void Load()
    {
        L = Log;
        Settings.Bind(Config);

        // Register new features here. Their order is the order of their sections in the settings panel.
        var all = new IFeature[] { new DpsMeterFeature(), new IncomingDamageFeature(), new GoldXpFeature(), new BossHpFeature() };

        foreach (var f in all)
        {
            var enabled = Config.Bind("Features", f.Id, true, f.Description);
            if (!enabled.Value) { Log.LogInfo($"{f.Id}: disabled in config."); continue; }
            try
            {
                f.Bind(Config);
                if (f.Install()) { Features.Add(f); Log.LogInfo($"{f.Id}: loaded."); }
                else Log.LogError($"{f.Id}: failed to install (a game update may have changed what it hooks).");
            }
            catch (Exception e) { Log.LogError($"{f.Id}: {e}"); }
        }

        FrameHost.Install(Config);
        Log.LogInfo($"{Name} {Version} loaded with {Features.Count} feature(s).");
    }
}

/// <summary>A self-contained mod feature. Update/OnGUI are called every frame from <see cref="FrameHost"/>.</summary>
internal interface IFeature
{
    /// <summary>Config key under [Features] and log prefix.</summary>
    string Id { get; }
    /// <summary>Section title in the settings panel.</summary>
    string Title => Id;
    string Description { get; }
    void Bind(ConfigFile config);
    /// <summary>Install hooks. Return false if the feature can't work on this game build.</summary>
    bool Install();
    void Update() { }
    void OnGUI() { }
    /// <summary>The feature's controls in the settings panel (under its title).</summary>
    void DrawSettings(SettingsGui g) { }
    /// <summary>Clear the feature's metrics (settings panel "Reset all").</summary>
    void Reset() { }
}
