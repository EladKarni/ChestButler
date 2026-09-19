using UnityEngine;

namespace ChestButler.Core
{
    /// <summary>Attached to every player-built container, inert unless the container is a Puller Chest.
    /// Sends what is left in a Puller back to storage once nobody has touched it for
    /// <c>[Puller] ReturnAfterSeconds</c>.
    ///
    /// Same shape as <see cref="SorterBehaviour"/> and for the same reasons: only the ZDO owner acts,
    /// nothing happens while the chest UI is open, and every move goes out through
    /// <see cref="Router"/> and MultiUserChest, so a returned item lands exactly where the sorter would
    /// have put it. The tick is slow because the answer only changes once every few minutes.</summary>
    internal class PullerBehaviour : MonoBehaviour
    {
        private const float TickInterval = 5f;

        private Container _container;
        private float _nextTick;
        private float _idleSince;
        private int _lastCount = -1;

        private void Awake()
        {
            _container = GetComponent<Container>();
            _idleSince = Time.time;
            _nextTick = Time.time + (GetInstanceID() & 0xF) * 0.1f;   // stagger, as the sorter does
        }

        private void FixedUpdate()
        {
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + TickInterval;

            if (_container == null || Player.m_localPlayer == null) { Hold(); return; }

            // Every player-built container gets one of these, because at Container.Awake the ZDO that
            // says which prefab this is does not exist yet. Once it does and the answer is "an ordinary
            // chest", stop paying for a FixedUpdate on every chest in the base.
            var zdoNview = SorterZdo.NView(_container);
            if (zdoNview != null && zdoNview.IsValid() && !PullerChestPiece.IsPullerChest(_container))
            {
                Destroy(this);
                return;
            }
            if (!PullerChestPiece.IsPullerChest(_container)) return;      // ZDO not ready yet

            var nview = SorterZdo.NView(_container);
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) { Hold(); return; }

            float wait = PullerConfig.ReturnAfter;
            if (wait <= 0f) { Hold(); return; }

            var inv = _container.GetInventory();
            if (inv == null) { Hold(); return; }

            int count = inv.GetAllItems().Count;

            // Someone is looking at it, or something new arrived: the clock starts again.
            //
            // IsInUse is a LOCAL field (v2 plan §16.2.2), so a player on another client browsing this
            // chest through MultiUserChest is invisible here. Ownership is the only networked signal,
            // and vanilla hands it to whoever opens the chest, so in practice the timer belongs to
            // whoever used it last. It can still fire while a second viewer has it open.
            if (_container.IsInUse() || count > _lastCount) { Hold(count); return; }

            _lastCount = count;
            if (count == 0) { _idleSince = Time.time; return; }
            if (Time.time - _idleSince < wait) return;
            if (Organizer.IsRunning) return;          // let the run finish, try again in 5 s

            int moved = PullerStorage.ReturnAll(_container, out int types);
            if (moved > 0)
                Plugin.Log.LogInfo("[puller] sent back " + moved + " item(s) across " + types + " type(s)");
            else
                _idleSince = Time.time;   // nowhere to put it; do not re-scan the base every tick
        }

        /// <summary>Park the clock while we cannot act. Freezing it instead would let a wait that ran
        /// down in the background fire the instant we regain ownership, emptying a chest somebody is
        /// standing at.</summary>
        private void Hold(int count = -1)
        {
            _idleSince = Time.time;
            if (count >= 0) _lastCount = count;
            else if (_container != null)
            {
                var inv = _container.GetInventory();
                _lastCount = inv != null ? inv.GetAllItems().Count : -1;
            }
        }
    }
}
