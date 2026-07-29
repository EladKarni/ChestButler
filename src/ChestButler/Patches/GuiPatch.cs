using HarmonyLib;
using ChestButler.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChestButler.Patches
{
    /// <summary>Chest-UI toolbar: [Sorter][Pin][Clear][Pull] for normal chests, or [Sorter][Organize]
    /// for sorter chests, in a HorizontalLayoutGroup styled from the vanilla Take All button and
    /// anchored to the panel's bottom-left corner.</summary>
    [HarmonyPatch(typeof(InventoryGui))]
    internal static class GuiPatch
    {
        private static RectTransform _bar;
        private static Button _sorterBtn, _pinBtn, _clearBtn, _pullBtn, _organizeBtn;
        private static TMP_Text _sorterLabel, _pinLabel, _clearLabel, _pullLabel, _organizeLabel;
        private static Container _current;

        // m_containerGrid is private on InventoryGui; reach it the same way SorterZdo reaches m_nview.
        private static readonly AccessTools.FieldRef<InventoryGui, InventoryGrid> ContainerGridRef =
            AccessTools.FieldRefAccess<InventoryGui, InventoryGrid>("m_containerGrid");

        // The grid's own RectTransform spans the WHOLE panel regardless of how many rows the chest
        // actually has, so measuring its bottom corner always parked the bar at the panel floor.
        // Measure the live slot objects instead — that is the bottom of the rows in use. They are the
        // children of m_gridRoot (InventoryGrid.Element itself is private, so we go via the root).
        private static readonly AccessTools.FieldRef<InventoryGrid, RectTransform> GridRootRef =
            AccessTools.FieldRefAccess<InventoryGrid, RectTransform>("m_gridRoot");

        private static float _lastBarY = float.NaN;
        private static readonly Vector3[] Corners = new Vector3[4];

        // Organize preview-then-confirm: first press builds+previews a plan, a second press on the
        // same chest within the window executes it. Any close / different chest / timeout cancels.
        private const float ConfirmWindow = 5f;
        private const float MinConfirmDelay = 0.3f;   // swallow double-click bounce on the arming press
        private static OrganizePlan _pendingPlan;
        private static Container _pendingChest;
        private static float _pendingAt;

        [HarmonyPostfix, HarmonyPatch("Show")]
        private static void ShowPostfix(InventoryGui __instance, Container container)
        {
            if (container != _pendingChest) ClearPending();   // opening a different chest cancels a pending Organize
            _current = container;
            _lastBarY = float.NaN;                            // force a reposition for the new chest
            EnsureBar(__instance);
            PositionBar(__instance);
            Refresh();
        }

        /// <summary>Reposition after the container grid has been rebuilt. At Show-time the slot
        /// elements still belong to the previous chest (or do not exist yet), so measuring there
        /// alone gets the wrong row count on the first frame. UpdateContainer runs every frame the
        /// panel is open, and PositionBar only writes when the target actually moved.</summary>
        [HarmonyPostfix, HarmonyPatch("UpdateContainer")]
        private static void UpdateContainerPostfix(InventoryGui __instance)
        {
            if (_current == null || _bar == null || !_bar.gameObject.activeSelf) return;
            PositionBar(__instance);
        }

        [HarmonyPostfix, HarmonyPatch("Hide")]
        private static void HidePostfix()
        {
            _current = null;
            ClearPending();
            if (_bar != null) _bar.gameObject.SetActive(false);
        }

        // Revert the Confirm? label the moment a pending plan expires (cheap: no-op unless pending).
        [HarmonyPostfix, HarmonyPatch("Update")]
        private static void UpdatePostfix()
        {
            if (_pendingPlan != null && Time.time - _pendingAt > ConfirmWindow)
                ClearPending();
        }

        private static void EnsureBar(InventoryGui gui)
        {
            if (_bar != null) return;
            var takeAll = gui.m_takeAllButton;
            if (takeAll == null) return;

            var srcRt = takeAll.GetComponent<RectTransform>();
            var parent = srcRt.parent as RectTransform;
            if (parent == null) return;

            var barGo = new GameObject("psort_bar", typeof(RectTransform));
            _bar = (RectTransform)barGo.transform;
            _bar.SetParent(srcRt.parent, false);

            // Anchor reference = panel bottom-left corner; PositionBar sets the exact Y each time a
            // chest opens so the bar sits just below the item grid (which grows with chest size).
            _bar.anchorMin = new Vector2(0f, 0f);
            _bar.anchorMax = new Vector2(0f, 0f);
            _bar.pivot     = new Vector2(0f, 1f);   // top-left pivot: the bar grows down + right

            var layout = barGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = barGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _sorterBtn   = MakeButton(takeAll, "psort_toggle",   OnSorterClick,   out _sorterLabel);
            _pinBtn      = MakeButton(takeAll, "psort_pin",      OnPinClick,      out _pinLabel);
            _clearBtn    = MakeButton(takeAll, "psort_clear",    OnClearClick,    out _clearLabel);
            _pullBtn     = MakeButton(takeAll, "psort_pull",     OnPullClick,     out _pullLabel);
            _organizeBtn = MakeButton(takeAll, "psort_organize", OnOrganizeClick, out _organizeLabel);
        }

        // Park the bar in the panel's bottom-left corner, mirroring Take All's margin from the top,
        // and only move it when the chest's rows actually reach down into that corner. EnsureBar
        // builds the bar once; this places it on open and whenever the grid is rebuilt.
        private static void PositionBar(InventoryGui gui)
        {
            if (_bar == null) return;
            var takeAll = gui.m_takeAllButton;
            if (takeAll == null) return;
            var srcRt = takeAll.GetComponent<RectTransform>();
            var parent = srcRt.parent as RectTransform;
            if (parent == null) return;

            Rect pr = parent.rect;
            float taW = srcRt.rect.width, taH = srcRt.rect.height;
            float refX = pr.x + pr.width * srcRt.anchorMin.x;
            float refY = pr.y + pr.height * srcRt.anchorMin.y;
            float centerX = refX + srcRt.anchoredPosition.x + (0.5f - srcRt.pivot.x) * taW;
            float centerY = refY + srcRt.anchoredPosition.y + (0.5f - srcRt.pivot.y) * taH;
            float leftMargin = (centerX - taW * 0.5f) - pr.xMin;      // keep Take All's left edge
            float topMargin = pr.yMax - (centerY + taH * 0.5f);       // Take All's gap from the panel top

            const float gap = 6f;

            // PREFERRED: bottom-left corner, symmetric with the top row. anchoredPosition.y is the
            // bar's TOP edge above the panel floor (anchor = panel bottom-left, pivot = bar top-left).
            float barY = topMargin + taH;

            // Only give that up if the rows in use would collide with it.
            float usedBottomLocalY;
            if (TryGetUsedGridBottom(gui, parent, out usedBottomLocalY))
            {
                float barTopLocalY = pr.yMin + barY;
                if (barTopLocalY > usedBottomLocalY - gap)
                {
                    // The grid reaches into the corner: sit directly under the last row instead,
                    // which for a full-height grid means just below the panel.
                    barY = (usedBottomLocalY - gap) - pr.yMin;
                    float floor = -taH;                               // never further than one row below
                    if (barY < floor) barY = floor;
                }
            }

            if (float.IsNaN(_lastBarY) || Mathf.Abs(barY - _lastBarY) > 0.5f)
            {
                _bar.anchoredPosition = new Vector2(leftMargin, barY);
                _lastBarY = barY;
                Plugin.Log.LogDebug("[ui] panel=" + pr + " usedGridBottom=" + usedBottomLocalY
                    + " barY=" + barY + " leftMargin=" + leftMargin);
            }
        }

        /// <summary>Lowest edge of the container grid's ACTIVE slot objects, in <paramref name="space"/>'s
        /// local coordinates. The grid's own RectTransform is sized to the whole panel whatever the
        /// chest holds, so it cannot be used to find where the rows end — the slots can.</summary>
        private static bool TryGetUsedGridBottom(InventoryGui gui, RectTransform space, out float bottomLocalY)
        {
            bottomLocalY = 0f;
            var grid = ContainerGridRef(gui);
            if (grid == null) return false;

            var root = GridRootRef(grid);
            if (root == null || root.childCount == 0) return false;

            float lowest = float.MaxValue;
            bool any = false;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == null || !child.gameObject.activeSelf) continue;   // pooled/unused slot
                var rt = child as RectTransform;
                if (rt == null) continue;
                rt.GetWorldCorners(Corners);                                  // 0=BL,1=TL,2=TR,3=BR
                float y = space.InverseTransformPoint(Corners[0]).y;
                if (y < lowest) { lowest = y; any = true; }
            }

            if (!any) return false;
            bottomLocalY = lowest;
            return true;
        }

        private static Button MakeButton(Button template, string name,
            UnityEngine.Events.UnityAction onClick, out TMP_Text label)
        {
            var btn = Object.Instantiate(template, _bar);
            btn.name = name;

            var loc = btn.GetComponentInChildren<Localize>(true);
            if (loc != null) Object.DestroyImmediate(loc);
            foreach (var gp in btn.GetComponentsInChildren<UIGamePad>(true))
                Object.DestroyImmediate(gp);

            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(onClick);

            // keep the vanilla button's exact footprint
            var srcRt = template.GetComponent<RectTransform>();
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = srcRt.rect.width;
            le.preferredHeight = srcRt.rect.height;

            // keep the vanilla font; shrink only if a label runs long
            label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                float vanilla = label.fontSize;
                label.enableAutoSizing = true;
                label.fontSizeMax = vanilla;
                label.fontSizeMin = vanilla - 4f;
            }
            return btn;
        }

        private static void OnSorterClick()
        {
            if (_current == null) return;
            bool now = !SorterZdo.IsSorter(_current);
            SorterZdo.SetSorter(_current, now);
            ClearPending();
            Msg(now ? "Sorter enabled. Contents distribute when the chest is closed" : "Sorter disabled");
            Refresh();
        }

        private static void OnPinClick()
        {
            if (_current == null) return;
            bool pinned = Filters.GetPinned(_current).Count > 0;

            if (!pinned)
            {
                int n = Filters.PinContents(_current);
                if (n > 0)
                {
                    Filters.SetManual(_current, false);
                    var names = Filters.GetPinned(_current);
                    Msg("Pinned: " + string.Join(", ", names) + " (auto-fill on)");
                    Plugin.Log.LogInfo("[pin] " + string.Join(", ", names));
                }
                else
                {
                    // W1 (v2 plan §15.10): an EMPTY chest used to dead-end here ("add sample items
                    // first"), so it could not be opted out of anything — and empty chests are exactly
                    // what the v2 allocator claims as new bucket homes. Toggling Manual is that escape
                    // hatch: a Manual chest is never an Organize target.
                    bool nowManual = !Filters.GetManual(_current);
                    Filters.SetManual(_current, nowManual);
                    Msg(nowManual
                        ? "Chest is empty - marked Manual. Organize will not claim or fill it"
                        : "Manual off. Organize may claim this empty chest as a home");
                }
            }
            else
            {
                bool manual = !Filters.GetManual(_current);
                Filters.SetManual(_current, manual);
                Msg(manual ? "Auto-fill off. This chest only fills when you click Pull"
                           : "Auto-fill on. The sorter routes matching items here");
            }
            Refresh();
        }

        private static void OnClearClick()
        {
            if (_current == null) return;
            Filters.ClearPinned(_current);
            Filters.SetManual(_current, false);
            // W1 (v2 plan §4.1): Clear also releases a home the allocator claimed, otherwise a chest
            // Organize adopted can never be handed back — the marker would keep making it an anchor.
            Filters.ClearHome(_current);
            Msg("Filters cleared");
            Refresh();
        }

        private static void OnPullClick()
        {
            if (_current == null) return;
            Puller.PullInto(_current, out int total, out int types);
            Msg(total > 0
                ? "Pulled " + total + " item" + (total == 1 ? "" : "s") + " (" + types + " type" + (types == 1 ? "" : "s") + ")"
                : "Nothing to pull from nearby chests");
        }

        private static void OnOrganizeClick()
        {
            if (_current == null) return;

            // second press on the same chest within the window -> RE-PLAN, then execute
            if (_pendingPlan != null && _pendingChest == _current && Time.time - _pendingAt <= ConfirmWindow)
            {
                if (Time.time - _pendingAt < MinConfirmDelay) return;   // accidental double-click: keep the preview
                var chest = _current;
                ClearPending();

                // v2 plan §16.2.6: confirm used to execute the PREVIEWED plan, built on a census up to
                // 5.5 s old, and then act on it for minutes. Re-plan instead — it costs one BuildPlan
                // and it is the difference between executing the base as it is and as it was.
                var fresh = Organizer.BuildPlan(chest, Plugin.SorterRadius.Value);
                if (fresh.IsEmpty)
                {
                    Msg("Nothing left to organize");
                    return;
                }
                Organizer.Execute(fresh);
                return;
            }

            // first press (or stale/other-chest) -> build a fresh plan and preview it
            var built = Organizer.BuildPlan(_current, Plugin.SorterRadius.Value);
            if (built.IsEmpty)
            {
                ClearPending();
                Msg("Nothing to organize");
                return;
            }

            _pendingPlan = built;
            _pendingChest = _current;
            _pendingAt = Time.time;
            var s = built.Summary;
            if (_organizeLabel != null) _organizeLabel.text = "Confirm?";   // unmissable, unlike the fading Msg
            Plugin.Log.LogInfo("[organize] plan ready: " + s.TotalItems + " items -> " + s.TargetChests +
                " chest(s) from " + s.SourceChests + " source(s); awaiting confirm");
            string preview = "Organize: move " + s.TotalItems + " item" + (s.TotalItems == 1 ? "" : "s") +
                " across " + s.TargetChests + " chest" + (s.TargetChests == 1 ? "" : "s");
            // v2 plan §4 step 5 / §8: an under-provisioned base is a normal outcome, not an error, and
            // the player needs to hear about it BEFORE confirming rather than after.
            if (s.HomelessItems > 0)
                preview += " (" + s.HomelessItems + " won't fit - add more chests)";
            Msg(preview + " - press again to confirm");
        }

        private static void ClearPending()
        {
            _pendingPlan = null;
            _pendingChest = null;
            _pendingAt = 0f;
            if (_organizeLabel != null) _organizeLabel.text = "Organize";
        }

        private static void Refresh()
        {
            if (_bar == null) return;
            bool usable = _current != null && SorterZdo.HasValidNView(_current);
            _bar.gameObject.SetActive(usable);
            if (!usable) return;

            bool isSorter = SorterZdo.IsSorter(_current);
            _sorterLabel.text = isSorter ? "Sorter: ON" : "Sorter: OFF";

            bool showPin = !isSorter;
            bool showClear = false;
            bool showPull = false;
            if (showPin)
            {
                int n = Filters.GetPinned(_current).Count;
                bool manual = Filters.GetManual(_current);
                var spec = Filters.GetSpec(_current);

                // W1 (§15.10): the Manual state is now visible with zero pins, so an empty chest can be
                // opted out of the allocator and can be seen to be opted out.
                _pinLabel.text = n == 0
                    ? (manual ? "Manual" : "Pin")
                    : (manual ? "Manual (" + n + ")" : "Auto (" + n + ")");

                // Clear appears whenever there is something to clear — including a home the allocator
                // claimed on a chest that has no pins of its own (v2 plan §4.1).
                showClear = n > 0 || manual || !string.IsNullOrEmpty(spec.Home);
                if (showClear) _clearLabel.text = "Clear";
                showPull = spec.HasExplicit;
                if (showPull) _pullLabel.text = "Pull";
            }

            // Organize replaces the pin/clear/pull group on sorter chests
            if (isSorter)
                _organizeLabel.text = (_pendingPlan != null && _pendingChest == _current) ? "Confirm?" : "Organize";

            _pinBtn.gameObject.SetActive(showPin);
            _clearBtn.gameObject.SetActive(showClear);
            _pullBtn.gameObject.SetActive(showPull);
            _organizeBtn.gameObject.SetActive(isSorter);
        }

        private static void Msg(string text)
        {
            if (Player.m_localPlayer != null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }
    }
}
