using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Utils;
using ChestButler.Core;

namespace ChestButler
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    [BepInDependency("com.maxsch.valheim.MultiUserChest")]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "eksolutions.chestbutler";
        public const string ModName = "ChestButler";
        public const string ModVersion = "1.1.2";

        internal static Plugin Instance;          // for StartCoroutine (Organize execution)
        internal static ManualLogSource Log;
        internal static ConfigEntry<float> SorterRadius;
        internal static ConfigEntry<float> TransferInterval;
        internal static ConfigEntry<int> StacksPerTick;
        internal static ConfigEntry<bool> ContainsFallback;
        internal static ConfigEntry<bool> VehiclesAreStorage;

        // WAVE 0: the [Organize] entries now live in OrganizeConfig so that W1 can add to that
        // section without touching this file, and so no two 2.0 workstreams edit the same region of
        // Plugin.Awake. These forwarders keep every existing reader compiling unchanged.
        //
        // W1: the OrganizeMovesPerTick forwarder is gone with the key it pointed at — the per-FRAME
        // budget is replaced by [Organize] MovesPerSecond (v2 plan §16.3), read directly from
        // OrganizeConfig by its only consumer.
        internal static ConfigEntry<float> StationRange => OrganizeConfig.StationRange;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            SorterRadius = Config.Bind("Sorting", "Radius", 128f,
                new ConfigDescription("Radius (m) around a sorter chest in which target chests are searched.",
                    new AcceptableValueRange<float>(5f, 128f),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            // 1.1.2: the floor was 0.2 s. Each tick can cost a full base scan per item type in the
            // sorter, so 20 sorters at 5 ticks/s was enough to eat a core on a large base. The
            // default was already 1.0; this only removes the settings that were never safe.
            // NOT admin-only, deliberately: the two rate knobs below change only how FAST this client
            // works, never what the result is, so a player on a weak machine can turn them down
            // without an admin. Everything that changes the OUTCOME (radius, item groups, station map)
            // stays admin-only and server-synced — two clients computing different answers for the same
            // base would be a correctness bug, not a preference. The mod also self-throttles, so these
            // are a ceiling rather than a tuning knob.
            TransferInterval = Config.Bind("Sorting", "TransferInterval", 1.0f,
                new ConfigDescription("Seconds between transfer ticks per sorter. Client-side: raise it if the mod costs you frames.",
                    new AcceptableValueRange<float>(1f, 10f)));

            StacksPerTick = Config.Bind("Sorting", "StacksPerTick", 2,
                new ConfigDescription("How many item stacks a sorter moves per tick. Client-side.",
                    new AcceptableValueRange<int>(1, 8)));

            ContainsFallback = Config.Bind("Sorting", "ContainsFallback", true,
                new ConfigDescription("Route items to chests that already contain them when no explicit filter matches.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            VehiclesAreStorage = Config.Bind("Sorting", "VehiclesAreStorage", false,
                new ConfigDescription("Treat cart and ship inventories as storage. Off (default): the sorter, Organize, Pull and Gather all ignore vehicles entirely - they are transport, and their own Pin/Pull buttons still work for manual loading.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            Groups.Init(Config);
            Stations.Init(Config);

            // WAVE 0 (2.0): one line per feature, each owned by exactly one workstream. Fill in your
            // own module; do not add config binds or registrations directly to this method.
            OrganizeConfig.Init(Config);       // W1 - Organize v2
            Gather.Init(Config);               // W2 - Gather
            SorterChestPiece.Register();       // W3 - Dedicated Sorter Chest

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll();

            Log.LogInfo($"{ModName} {ModVersion} loaded");
        }
    }
}
