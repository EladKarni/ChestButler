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
        internal static ConfigEntry<int> OrganizeMovesPerTick;
        internal static ConfigEntry<float> StationRange;

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
            TransferInterval = Config.Bind("Sorting", "TransferInterval", 1.0f,
                new ConfigDescription("Seconds between transfer ticks per sorter.",
                    new AcceptableValueRange<float>(1f, 10f),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            StacksPerTick = Config.Bind("Sorting", "StacksPerTick", 2,
                new ConfigDescription("How many item stacks a sorter moves per tick.",
                    new AcceptableValueRange<int>(1, 8),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            ContainsFallback = Config.Bind("Sorting", "ContainsFallback", true,
                new ConfigDescription("Route items to chests that already contain them when no explicit filter matches.",
                    null, new ConfigurationManagerAttributes { IsAdminOnly = true }));

            OrganizeMovesPerTick = Config.Bind("Organize", "MovesPerTick", 4,
                new ConfigDescription("How many item moves the Organize sweep performs per frame (higher = faster, more hitch).",
                    new AcceptableValueRange<int>(1, 16),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            StationRange = Config.Bind("Organize", "StationRange", 8f,
                new ConfigDescription("Max distance (m) from a chest to a crafting station for the chest to inherit that station's item groups during Organize. Nearest mapped station wins.",
                    new AcceptableValueRange<float>(1f, 20f),
                    new ConfigurationManagerAttributes { IsAdminOnly = true }));

            Groups.Init(Config);
            Stations.Init(Config);

            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll();

            Log.LogInfo($"{ModName} {ModVersion} loaded");
        }
    }
}
