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

        // Organize preview-then-confirm: first press builds+previews a plan, a second press on the
        // same chest within the window executes it. Any close / different chest / timeout cancels.
        private const float ConfirmWindow = 5f;
        private static OrganizePlan _pendingPlan;
        private static Container _pendingChest;
        private static float _pendingAt;

        [HarmonyPostfix, HarmonyPatch("Show")]
        private static void ShowPostfix(InventoryGui __instance, Container container)
        {
            if (container != _pendingChest) ClearPending();   // opening a different chest cancels a pending Organize
            _current = container;
            EnsureBar(__instance);
            Refresh();
        }

        [HarmonyPostfix, HarmonyPatch("Hide")]
        private static void HidePostfix()
        {
            _current = null;
            ClearPending();
            if (_bar != null) _bar.gameObject.SetActive(false);
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

            // Find where Take All sits inside the panel (it uses a centre anchor), then
            // anchor our bar to the panel's bottom-left CORNER and mirror those margins,
            // so it snaps to the corner and stays symmetric with the top row on any resize.
            Rect pr = parent.rect;
            float taW = srcRt.rect.width, taH = srcRt.rect.height;
            float refX = pr.x + pr.width * srcRt.anchorMin.x;
            float refY = pr.y + pr.height * srcRt.anchorMin.y;
            float centerX = refX + srcRt.anchoredPosition.x + (0.5f - srcRt.pivot.x) * taW;
            float centerY = refY + srcRt.anchoredPosition.y + (0.5f - srcRt.pivot.y) * taH;

            float leftMargin = (centerX - taW * 0.5f) - pr.xMin;   // Take All's gap from the left edge
            float topMargin  = pr.yMax - (centerY + taH * 0.5f);   // Take All's gap from the top edge

            _bar.anchorMin = new Vector2(0f, 0f);   // pin to the panel's bottom-left corner
            _bar.anchorMax = new Vector2(0f, 0f);
            _bar.pivot     = new Vector2(0f, 0f);   // bar's own bottom-left grows right + up
            _bar.anchoredPosition = new Vector2(leftMargin, topMargin);   // mirror the top row's margins

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
                else Msg("Chest is empty. Add sample items first");
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

            // second press on the same chest within the window -> execute the previewed plan
            if (_pendingPlan != null && _pendingChest == _current && Time.time - _pendingAt <= ConfirmWindow)
            {
                var plan = _pendingPlan;
                ClearPending();
                Organizer.Execute(plan);
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
            Msg("Organize: move " + s.TotalItems + " item" + (s.TotalItems == 1 ? "" : "s") +
                " across " + s.TargetChests + " chest" + (s.TargetChests == 1 ? "" : "s") + " - press again to confirm");
        }

        private static void ClearPending()
        {
            _pendingPlan = null;
            _pendingChest = null;
            _pendingAt = 0f;
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
                _pinLabel.text = n == 0 ? "Pin"
                    : Filters.GetManual(_current) ? "Manual (" + n + ")" : "Auto (" + n + ")";
                showClear = n > 0;
                if (showClear) _clearLabel.text = "Clear";
                showPull = Filters.GetSpec(_current).HasExplicit;
                if (showPull) _pullLabel.text = "Pull";
            }

            // Organize replaces the pin/clear/pull group on sorter chests
            if (isSorter) _organizeLabel.text = "Organize";

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
