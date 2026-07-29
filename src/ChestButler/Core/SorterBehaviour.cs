using System.Collections.Generic;
using MultiUserChest;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>Attached to every player-built container. Inert unless flagged as sorter.
    /// Runs only on the ZDO owner; all moves go through MultiUserChest.</summary>
    internal class SorterBehaviour : MonoBehaviour
    {
        // How long a "no home for this item" answer is trusted before we scan for it again.
        // Homeless items are the steady state of a sorter chest, and each scan is a full
        // Router.FindTarget -> ContainerTracker.Candidates sweep over the whole base. Before 1.1.2
        // a miss cost a sweep and consumed no budget, so a sorter holding 32 unroutable items paid
        // ~32 base-wide scans EVERY tick, forever — the single most expensive path in the mod.
        private const float MissCooldown = 10f;

        private Container _container;
        private float _nextTick;

        // normalized item name -> Time.time when the miss was recorded (per sorter chest)
        private readonly Dictionary<string, float> _misses = new Dictionary<string, float>();

        private void Awake()
        {
            _container = GetComponent<Container>();
            _nextTick = Time.time + (GetInstanceID() & 0xF) * 0.05f; // stagger
        }

        private void FixedUpdate()
        {
            if (Time.time < _nextTick) return;
            // W1 (v2 plan §16.6): the interval is the CONFIGURED value stretched by however much the
            // mod is currently over its own cost budget. Config is a ceiling, never a floor.
            _nextTick = Time.time + Throttle.TransferInterval(Plugin.TransferInterval.Value);

            // Routing is evaluated from the LOCAL player's point of view: both the per-container
            // access check and the ward check need Player.m_localPlayer. On a dedicated server that
            // is always null, so every candidate is rejected and the sweep can only do wasted work.
            if (Player.m_localPlayer == null) return;

            if (_container == null) return;
            var nview = SorterZdo.NView(_container);
            if (nview == null || !nview.IsValid()) return;
            if (!nview.IsOwner()) return;                    // rule zero: owner simulates
            if (!SorterZdo.IsSorter(_container)) return;
            if (_container.IsInUse()) return;                // wait until the chest UI closes

            var inv = _container.GetInventory();
            if (inv == null) return;

            // W1 (§16.6): time our own work so the throttle has a signal to act on. This is the second
            // of the two hot paths it measures (the other is Organizer's plan/run).
            using (Throttle.Measure())
            {
            var block = InventoryBlock.Get(inv);
            int budget = Plugin.StacksPerTick.Value;
            float now = Time.time;
            float missCooldown = Throttle.MissCooldown(MissCooldown);

            var snapshot = new List<ItemDrop.ItemData>(inv.GetAllItems());
            foreach (var item in snapshot)
            {
                if (budget <= 0) break;
                if (item == null || item.m_shared == null) continue;
                if (block != null && block.IsSlotBlocked(item.m_gridPos)) continue; // in flight

                // Skip item types we recently failed to place. Worst case, a newly built or newly
                // labelled chest starts receiving them up to MissCooldown seconds late.
                var norm = Names.Normalize(item.m_shared.m_name);
                if (norm.Length > 0 && _misses.TryGetValue(norm, out var missedAt))
                {
                    if (now - missedAt < missCooldown) continue;
                    _misses.Remove(norm);
                }

                var target = Router.FindTarget(_container, item, Plugin.SorterRadius.Value, out int amount);
                if (target == null || amount <= 0)
                {
                    if (norm.Length > 0) _misses[norm] = now; // no home → stays in sorter
                    continue;
                }

                ContainerHandler.AddItemToChest(
                    target, item, inv, new Vector2i(-1, -1),
                    nview.GetZDO().m_uid, amount);           // partial fill: remainder re-routes next tick

                budget--;
                Plugin.Log.LogDebug($"[sorter] routed {item.m_shared.m_name} x{amount}");
            }
            }
        }
    }
}
