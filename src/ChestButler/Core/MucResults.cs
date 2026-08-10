using System.Collections.Generic;

namespace ChestButler.Core
{
    /// <summary>What MultiUserChest actually ANSWERED, per request id. Fed by the response postfix
    /// in Patches/MucResponsePatches.cs, consumed by Organizer's completion sweep.
    ///
    /// §16.2.9: the executor used to treat "the request left MUC's PackageHandler" as "the move
    /// landed" — but MUC removes the package unconditionally before it even reads Success, so a
    /// refused transfer (source stack gone on the owner's replica, addressee not owner, no
    /// instance) is byte-identical to a landed one from the outside. The measured result on the
    /// staging server: moves reported as done, world unchanged, identical plan next press. The
    /// response itself carries Success and the actual Amount; this ledger is how the executor
    /// finally reads them.</summary>
    internal static class MucResults
    {
        private struct Result
        {
            public bool Success;
            public int Amount;
        }

        private static readonly Dictionary<int, Result> Removes = new Dictionary<int, Result>();

        internal static void RecordRemove(int requestId, bool success, int amount)
        {
            // Puller/Gather share the same MUC primitive, so responses we never look up accumulate;
            // ids are random ints, so stale entries are dead weight rather than collisions. Reset
            // wholesale rather than tracking age.
            if (Removes.Count > 256) Removes.Clear();
            Removes[requestId] = new Result { Success = success, Amount = amount };
        }

        internal static bool TryTakeRemove(int requestId, out bool success, out int amount)
        {
            if (Removes.TryGetValue(requestId, out var r))
            {
                Removes.Remove(requestId);
                success = r.Success;
                amount = r.Amount;
                return true;
            }
            success = false;
            amount = 0;
            return false;
        }

        internal static void Clear()
        {
            Removes.Clear();
        }
    }
}
