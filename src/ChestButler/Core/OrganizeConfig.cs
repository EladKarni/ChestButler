using BepInEx.Configuration;

namespace ChestButler.Core
{
    /// <summary>Owns the <c>[Organize]</c> config section.
    ///
    /// WAVE 0 (2.0 groundwork): these entries used to be declared as fields on <see cref="Plugin"/> and
    /// bound inside <c>Plugin.Awake</c>. Organize v2 (W1) needs to add several more, and every other
    /// 2.0 workstream also has to add one line to <c>Plugin.Awake</c> — so the config block moved here,
    /// where exactly one workstream edits it. <see cref="Plugin"/> keeps forwarding properties so
    /// existing readers keep compiling unchanged.
    ///
    /// W1: add new [Organize] entries in this file, not in Plugin.cs.</summary>
    internal static class OrganizeConfig
    {
        internal static ConfigEntry<int> MovesPerTick;
        internal static ConfigEntry<float> StationRange;

        internal static void Init(ConfigFile config)
        {
            // Rate only, so client-side and NOT admin-only (see the note in Plugin.Awake).
            MovesPerTick = config.Bind("Organize", "MovesPerTick", 4,
                new ConfigDescription("How many item moves the Organize sweep performs per frame (higher = faster, more hitch). " +
                    "The rate is additionally capped in real time, so a high-refresh-rate client does not send proportionally more traffic. " +
                    "Client-side: lower it if Organize costs you frames.",
                    new AcceptableValueRange<int>(1, 16)));

            StationRange = config.Bind("Organize", "StationRange", 8f,
                new ConfigDescription("Max distance (m) from a chest to a crafting station for the chest to inherit that station's item groups during Organize. Nearest mapped station wins. " +
                    "NOTE: this sets the match distance only — station detection scans every loaded station regardless.",
                    new AcceptableValueRange<float>(1f, 20f),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
        }
    }
}
