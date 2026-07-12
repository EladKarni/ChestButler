using HarmonyLib;
using ChestButler.Core;

namespace ChestButler.Patches
{
    [HarmonyPatch(typeof(Sign), "Awake")]
    internal static class Sign_Awake_Patch
    {
        private static void Postfix(Sign __instance) => Filters.RegisterSign(__instance);
    }
}
