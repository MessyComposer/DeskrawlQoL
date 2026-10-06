using BepInEx.Configuration;

namespace DeskrawlQoL.Core;

/// <summary>Settings shared by all features.</summary>
internal static class Settings
{
    public static ConfigEntry<string> SettingsKey;
    public static ConfigEntry<int> FontSize;
    public static ConfigEntry<float> PanelX, PanelY;

    public static void Bind(ConfigFile c)
    {
        const string s = "General";
        SettingsKey = Hotkey.Bind(c, s, "SettingsKey", "Alt+Q", "Open/close the Deskrawl QoL settings panel");
        FontSize = c.Bind(s, "FontSize", 14, "Font size of all Deskrawl QoL panels (also adjustable in the settings panel).");
        PanelX = c.Bind(s, "SettingsPanelX", -1f, "Settings panel X position (-1 = centred).");
        PanelY = c.Bind(s, "SettingsPanelY", -1f, "Settings panel Y position (-1 = centred).");
    }
}
