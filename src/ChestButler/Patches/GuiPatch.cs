using HarmonyLib;
using ChestButler.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChestButler.Patches
{
    /// <summary>Chest-UI toolbar: [Sorter][Pin][Pull] in a HorizontalLayoutGroup one row
    /// below the container panel. Layout group handles sizing/spacing and compacts when buttons hide.</summary>
    [HarmonyPatch(typeof(InventoryGui))]
    internal static class GuiPatch
    {
        private static RectTransform _bar;
        private static Button _sorterBtn, _pinBtn, _clearBtn, _pullBtn;
        private static TMP_Text _sorterLabel, _pinLabel, _clearLabel, _pullLabel;
        private static Container _current;

        [HarmonyPostfix, HarmonyPatch("Show")]
        private static void ShowPostfix(InventoryGui __instance, Container container)
        {
            _current = container;
            EnsureBar(__instance);
            Refresh();
        }

        [HarmonyPostfix, HarmonyPatch("Hide")]
        private static void HidePostfix()
        {
            _current = null;
            if (_bar != null) _bar.gameObject.SetActive(false);
        }

        private static void EnsureBar(InventoryGui gui)
        {
            if (_bar != null) return;                      // Unity fake-null covers scene reloads
            var takeAll = gui.m_takeAllButton;
            if (takeAll == null) return;

            var barGo = new GameObject("psort_bar", typeof(RectTransform));
            _bar = (RectTransform)barGo.transform;
            _bar.SetParent(gui.m_container, false);        // container panel root
            _bar.anchorMin = new Vector2(0f, 0f);          // bottom-left of the panel…
            _bar.anchorMax = new Vector2(0f, 0f);
            _bar.pivot = new Vector2(0f, 1f);
            _bar.anchoredPosition = new Vector2(12f, -8f); // …hanging just below it (unused space)

            var layout = barGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = barGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            _sorterBtn = MakeButton(takeAll, "psort_toggle", OnSorterClick, out _sorterLabel);
            _pinBtn    = MakeButton(takeAll, "psort_pin",    OnPinClick,    out _pinLabel);
            _clearBtn  = MakeButton(takeAll, "psort_clear",  OnClearClick,  out _clearLabel);
            _pullBtn   = MakeButton(takeAll, "psort_pull",   OnPullClick,   out _pullLabel);
        }

        private static Button MakeButton(Button template, string name,
            UnityEngine.Events.UnityAction onClick, out TMP_Text label)
        {
            var btn = Object.Instantiate(template, _bar);
            btn.name = name;

            // strip cloned baggage: localization overwrites our label, gamepad hints steal input
            var loc = btn.GetComponentInChildren<Localize>(true);
            if (loc != null) Object.DestroyImmediate(loc);
            foreach (var gp in btn.GetComponentsInChildren<UIGamePad>(true))
                Object.DestroyImmediate(gp);

            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(onClick);

            var srcRt = template.GetComponent<RectTransform>();
            var le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = srcRt.rect.width * 0.8f;
            le.preferredHeight = srcRt.rect.height;

            label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.enableAutoSizing = true;
                label.fontSizeMin = 9f;
            }
            return btn;
        }

        private static void OnSorterClick()
        {
            if (_current == null) return;
            bool now = !SorterZdo.IsSorter(_current);
            SorterZdo.SetSorter(_current, now);
            Msg(now ? "Sorter enabled — items will be distributed when closed" : "Sorter disabled");
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
                    Msg($"Pinned: {string.Join(", ", names)} — auto-fill ON");
                    Plugin.Log.LogInfo($"[pin] {string.Join(", ", names)}");
                }
                else Msg("Chest is empty — add sample items first");
            }
            else
            {
                bool manual = !Filters.GetManual(_current);   // pure toggle, pins untouched
                Filters.SetManual(_current, manual);
                Msg(manual ? "Auto-fill OFF — fills only via Pull"
                           : "Auto-fill ON — sorter routes here automatically");
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
                ? $"Pulled {total} item{(total == 1 ? "" : "s")} ({types} type{(types == 1 ? "" : "s")})"
                : "Nothing to pull from nearby chests");
        }

        private static void Refresh()
        {
            if (_bar == null) return;
            bool usable = _current != null && SorterZdo.HasValidNView(_current);
            _bar.gameObject.SetActive(usable);
            if (!usable) return;

            bool isSorter = SorterZdo.IsSorter(_current);
            _sorterLabel.text = isSorter ? "Sorter: ON" : "Sorter: OFF";

            bool showPin = !isSorter;                      // sorters are sources, not targets
            bool showClear = false;
            bool showPull = false;
            if (showPin)
            {
                int n = Filters.GetPinned(_current).Count;
                _pinLabel.text = n == 0 ? "Pin"
                    : Filters.GetManual(_current) ? $"Manual ({n})" : $"Auto ({n})";
                showClear = n > 0;
                if (showClear) _clearLabel.text = "Clear";
                showPull = Filters.GetSpec(_current).HasExplicit;
                if (showPull) _pullLabel.text = "Pull";
            }
            _pinBtn.gameObject.SetActive(showPin);
            _clearBtn.gameObject.SetActive(showClear);
            _pullBtn.gameObject.SetActive(showPull);       // layout group closes the gaps
        }

        private static void Msg(string text)
        {
            if (Player.m_localPlayer != null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }
    }
}
