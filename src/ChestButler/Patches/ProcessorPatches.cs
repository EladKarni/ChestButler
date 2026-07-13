using HarmonyLib;
using ChestButler.Core;

namespace ChestButler.Patches
{
    /// <summary>Smelters, kilns, blast furnaces, eitr refineries, fermenters and cooking stations
    /// are NOT CraftingStations, so CraftingStation.m_allStations never lists them. Track them
    /// ourselves (same Awake/OnDestroyed pattern as Container tracking) so Organize's station
    /// adjacency can pool ore+coal by the smelter, meads by the fermenter, etc. Stations also
    /// prunes unloaded pieces on scan, since OnDestroyed only fires on damage-destruction.</summary>
    [HarmonyPatch(typeof(Smelter), "Awake")]
    internal static class Smelter_Awake_Patch
    {
        private static void Postfix(Smelter __instance) =>
            Stations.RegisterProcessor(__instance, __instance.m_name);
    }

    [HarmonyPatch(typeof(Smelter), "OnDestroyed")]
    internal static class Smelter_OnDestroyed_Patch
    {
        private static void Prefix(Smelter __instance) =>
            Stations.UnregisterProcessor(__instance);
    }

    [HarmonyPatch(typeof(Fermenter), "Awake")]
    internal static class Fermenter_Awake_Patch
    {
        private static void Postfix(Fermenter __instance) =>
            Stations.RegisterProcessor(__instance, __instance.m_name);
    }

    [HarmonyPatch(typeof(Fermenter), "OnDestroyed")]
    internal static class Fermenter_OnDestroyed_Patch
    {
        private static void Prefix(Fermenter __instance) =>
            Stations.UnregisterProcessor(__instance);
    }

    [HarmonyPatch(typeof(CookingStation), "Awake")]
    internal static class CookingStation_Awake_Patch
    {
        private static void Postfix(CookingStation __instance) =>
            Stations.RegisterProcessor(__instance, __instance.m_name);
    }

    [HarmonyPatch(typeof(CookingStation), "OnDestroyed")]
    internal static class CookingStation_OnDestroyed_Patch
    {
        private static void Prefix(CookingStation __instance) =>
            Stations.UnregisterProcessor(__instance);
    }
}
