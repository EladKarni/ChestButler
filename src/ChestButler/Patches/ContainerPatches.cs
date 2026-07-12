using HarmonyLib;
using ChestButler.Core;

namespace ChestButler.Patches
{
    [HarmonyPatch(typeof(Container), "Awake")]
    internal static class Container_Awake_Patch
    {
        private static void Postfix(Container __instance)
        {
            ContainerTracker.Register(__instance);
            if (__instance.GetComponent<SorterBehaviour>() == null &&
                __instance.GetComponentInParent<Piece>() != null)
            {
                __instance.gameObject.AddComponent<SorterBehaviour>();
            }
        }
    }

    [HarmonyPatch(typeof(Container), "OnDestroyed")]
    internal static class Container_OnDestroyed_Patch
    {
        private static void Prefix(Container __instance)
        {
            ContainerTracker.Unregister(__instance);
        }
    }
}
