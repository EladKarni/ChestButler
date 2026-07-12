using System.Collections.Generic;
using MultiUserChest;
using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>Attached to every player-built container. Inert unless flagged as sorter.
    /// Runs only on the ZDO owner; all moves go through MultiUserChest.</summary>
    internal class SorterBehaviour : MonoBehaviour
    {
        private Container _container;
        private float _nextTick;

        private void Awake()
        {
            _container = GetComponent<Container>();
            _nextTick = Time.time + (GetInstanceID() & 0xF) * 0.05f; // stagger
        }

        private void FixedUpdate()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + Plugin.TransferInterval.Value;

            if (_container == null) return;
            var nview = SorterZdo.NView(_container);
            if (nview == null || !nview.IsValid()) return;
            if (!nview.IsOwner()) return;                    // rule zero: owner simulates
            if (!SorterZdo.IsSorter(_container)) return;
            if (_container.IsInUse()) return;                // wait until the chest UI closes

            var inv = _container.GetInventory();
            if (inv == null) return;

            var block = InventoryBlock.Get(inv);
            int budget = Plugin.StacksPerTick.Value;

            var snapshot = new List<ItemDrop.ItemData>(inv.GetAllItems());
            foreach (var item in snapshot)
            {
                if (budget <= 0) break;
                if (item == null || item.m_shared == null) continue;
                if (block != null && block.IsSlotBlocked(item.m_gridPos)) continue; // in flight

                var target = Router.FindTarget(_container, item, Plugin.SorterRadius.Value, out int amount);
                if (target == null || amount <= 0) continue; // no home → stays in sorter

                ContainerHandler.AddItemToChest(
                    target, item, inv, new Vector2i(-1, -1),
                    nview.GetZDO().m_uid, amount);           // partial fill: remainder re-routes next tick

                budget--;
                Plugin.Log.LogDebug($"[sorter] routed {item.m_shared.m_name} x{amount}");
            }
        }
    }
}
