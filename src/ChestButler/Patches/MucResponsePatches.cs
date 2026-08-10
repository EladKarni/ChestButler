using HarmonyLib;
using MultiUserChest;
using ChestButler.Core;

namespace ChestButler.Patches
{
    /// <summary>Record every RequestChestRemove response before it disappears. MUC's handler calls
    /// InventoryPreview.RemovePackage(response) unconditionally — success and failure both make the
    /// package vanish from PackageHandler, which is the only completion signal Organizer had. The
    /// postfix runs after MUC has fully applied (or declined to apply) the response, on the same
    /// call stack that removed the package, so by the time Organizer's coroutine next polls, the
    /// verdict for that id is always already recorded.</summary>
    [HarmonyPatch(typeof(InventoryHandler), nameof(InventoryHandler.RPC_RequestItemRemoveResponse),
        typeof(Inventory), typeof(RequestChestRemoveResponse))]
    internal static class InventoryHandler_RemoveResponse_Patch
    {
        private static void Postfix(RequestChestRemoveResponse response)
        {
            if (response == null) return;
            MucResults.RecordRemove(response.SourceID, response.Success, response.Amount);
        }
    }
}
