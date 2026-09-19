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

            // 2.1.3: the return timer for Puller Chests. Added to every chest rather than only to
            // Pullers because the prefab check needs a valid ZDO, and this postfix is exactly where a
            // freshly placed chest does not have one yet. The behaviour itself checks the prefab on
            // every tick and does nothing on an ordinary chest.
            if (__instance.GetComponent<PullerBehaviour>() == null &&
                __instance.GetComponentInParent<Piece>() != null)
            {
                __instance.gameObject.AddComponent<PullerBehaviour>();
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
