using BepInEx.Configuration;

namespace ChestButler.Core
{
    /// <summary>Owns the <c>[Puller]</c> config section.
    ///
    /// A Puller Chest is invisible to everything automatic, which is what makes it a staging chest and
    /// also what makes it a trap: materials left in one are materials Gather, the sorter and Organize
    /// cannot see, and the first symptom is a build that will not pay for itself with a full base
    /// behind you. So a Puller empties itself back into storage once you stop using it.</summary>
    internal static class PullerConfig
    {
        internal static ConfigEntry<float> ReturnAfterSeconds;

        /// <summary>Seconds of no activity before a Puller Chest sends its contents back. 0 is off.</summary>
        internal static float ReturnAfter => ReturnAfterSeconds != null ? ReturnAfterSeconds.Value : 300f;

        internal static void Init(ConfigFile config)
        {
            // Admin-only and server-synced, like every other entry that changes where items end up.
            ReturnAfterSeconds = config.Bind("Puller", "ReturnAfterSeconds", 300f,
                new ConfigDescription(
                    "Seconds a Puller Chest waits after you last touch it before sending what is left back to storage. " +
                    "0 leaves items in the Puller until you take them out yourself.",
                    new AcceptableValueRange<float>(0f, 3600f),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));
        }
    }
}
