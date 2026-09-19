using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ChestButler.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChestButler.Patches
{
    /// <summary>2.1.2 — the Puller Chest panel: a search box over a list of everything in storage around
    /// the chest. Click a row to pull a stack of it into the chest, Shift-click to pull all of it.
    ///
    /// It takes the crafting panel's place while a Puller Chest is open. The crafting panel is the one
    /// part of the inventory screen that stays up next to an open chest, so its rect is known to be on
    /// screen at every resolution, and nobody crafts out of a chest they opened to fetch something.
    /// The crafting panel is hidden for exactly as long as the Puller is open and put back after.
    ///
    /// Everything is cloned from vanilla UI so it looks like the game: the search box from the build
    /// menu's own search field, the rows from the crafting panel's recipe element, the title from the
    /// chest name label.</summary>
    [HarmonyPatch(typeof(InventoryGui))]
    internal static class PullerPanelPatch
    {
        private const float CensusInterval = 1f;
        private const float Pad = 12f;
        private const float TitleHeight = 32f;
        private const float SearchHeight = 34f;
        private const float StatusHeight = 26f;
        private const float ButtonHeight = 34f;

        private static readonly AccessTools.FieldRef<InventoryGui, Container> CurrentContainerRef =
            AccessTools.FieldRefAccess<InventoryGui, Container>("m_currentContainer");

        /// <summary>BuildUi.m_searchField is private. Reached by reflection and null-checked, so a
        /// rename costs the search box and nothing else: the list still works without it.</summary>
        private static readonly FieldInfo BuildSearchField = AccessTools.Field(typeof(BuildUi), "m_searchField");

        private static RectTransform _panel;
        private static TMP_Text _title;
        private static TMP_InputField _search;
        private static ScrollRect _scroll;
        private static RectTransform _content;
        private static TMP_Text _status;
        private static Button _sendBack;
        private static float _rowHeight = 30f;

        private static readonly List<GameObject> Rows = new List<GameObject>();
        private static readonly List<string> RowNames = new List<string>();

        private static Container _chest;              // the open Puller Chest, or null
        private static bool _hidCrafting;
        private static List<PullerStorage.Entry> _census = new List<PullerStorage.Entry>();
        private static string _censusKey = "";
        private static float _censusAt = float.NegativeInfinity;

        /// <summary>True while the player is typing in the Puller search box. Fed into vanilla's own
        /// "a text field has focus" check, see <see cref="PullerSearchFocusPatch"/>.</summary>
        internal static bool SearchFocused =>
            _chest != null && _search != null && _search.isFocused && _panel != null && _panel.gameObject.activeInHierarchy;

        // ---- lifetime -----------------------------------------------------------------------------

        [HarmonyPostfix, HarmonyPatch("Show")]
        private static void ShowPostfix(InventoryGui __instance, Container container)
        {
            if (container != null && PullerChestPiece.IsPullerChest(container)) Open(__instance, container);
            else Close(__instance);
        }

        [HarmonyPostfix, HarmonyPatch("Hide")]
        private static void HidePostfix(InventoryGui __instance) => Close(__instance);

        /// <summary>Runs every frame the inventory screen is up. Catches the chest being closed some
        /// other way than Hide (walking out of range drops the container but keeps the screen open),
        /// and refreshes the counts while the panel is showing.</summary>
        [HarmonyPostfix, HarmonyPatch("UpdateContainer")]
        private static void UpdateContainerPostfix(InventoryGui __instance)
        {
            // Unity fake-null: a chest destroyed while open (a troll, a neighbour with a hammer, a
            // zone unload) reads as null here, and vanilla never re-enables the crafting panel by
            // itself - the one SetActive(true) is in InventoryGui.Awake.
            if (_chest == null)
            {
                if (_hidCrafting || (_panel != null && _panel.gameObject.activeSelf)) Close(__instance);
                return;
            }
            if (CurrentContainerRef(__instance) != _chest)
            {
                Close(__instance);
                return;
            }

            if (__instance.m_crafting != null && __instance.m_crafting.gameObject.activeSelf)
            {
                __instance.m_crafting.gameObject.SetActive(false);
                _hidCrafting = true;
            }

            ScrollWithWheel();

            if (Time.unscaledTime - _censusAt >= CensusInterval)
                RefreshCensus(false);
        }

        private const int RowsPerNotch = 3;

        private static bool PointerOverList()
        {
            if (_scroll == null || _scroll.viewport == null) return false;
            var view = _scroll.viewport;
            var canvas = view.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(view, ZInput.pointerPosition, cam);
        }

        /// <summary>Mouse wheel over the list moves it a fixed number of rows per notch.
        ///
        /// Left to its own OnScroll, the ScrollRect crawled a few pixels per notch: the wheel delta the
        /// UI module hands it in this build is tiny, and ZInput itself reads the wheel through a
        /// scale(0.15) processor. Only the SIGN of ZInput's value is used here, so whatever the scale
        /// is, or becomes after an update, one notch is RowsPerNotch rows.</summary>
        private static void ScrollWithWheel()
        {
            if (_scroll == null || _content == null || _scroll.viewport == null) return;

            float wheel = ZInput.GetMouseScrollWheel();
            if (Mathf.Approximately(wheel, 0f)) return;

            if (!PointerOverList()) return;

            var view = _scroll.viewport;
            float overflow = _content.rect.height - view.rect.height;
            if (overflow <= 0f) return;

            // Normalized 1 is the top of the list, and a positive wheel value is scrolling up.
            float step = RowsPerNotch * _rowHeight / overflow;
            _scroll.verticalNormalizedPosition =
                Mathf.Clamp01(_scroll.verticalNormalizedPosition + Mathf.Sign(wheel) * step);
        }

        /// <summary>While typing, the letters E and Tab are still the Use and Inventory buttons, and
        /// InventoryGui.Update closes the screen on either before any text field sees them. Swallow
        /// them for the frame. Escape still closes, which is what Escape is for.</summary>
        [HarmonyPrefix, HarmonyPatch("Update")]
        private static void UpdatePrefix()
        {
            if (!SearchFocused) return;
            ZInput.ResetButtonStatus("Use");
            ZInput.ResetButtonStatus("Inventory");
        }

        private static void Open(InventoryGui gui, Container chest)
        {
            if (!EnsurePanel(gui)) return;

            _chest = chest;
            if (gui.m_crafting != null && gui.m_crafting.gameObject.activeSelf)
            {
                gui.m_crafting.gameObject.SetActive(false);
                _hidCrafting = true;
            }

            _panel.gameObject.SetActive(true);
            _panel.SetAsLastSibling();
            if (_search != null) _search.text = "";
            RefreshCensus(true);
            if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
        }

        private static void Close(InventoryGui gui)
        {
            _chest = null;
            if (_search != null && _search.isFocused) _search.DeactivateInputField();
            if (_panel != null && _panel.gameObject.activeSelf) _panel.gameObject.SetActive(false);

            if (_hidCrafting)
            {
                _hidCrafting = false;
                if (gui != null && gui.m_crafting != null) gui.m_crafting.gameObject.SetActive(true);
            }
        }

        // ---- data ---------------------------------------------------------------------------------

        private static void RefreshCensus(bool force)
        {
            _censusAt = Time.unscaledTime;
            if (_chest == null) return;

            // Rows are pooled and a click carries the row's INDEX, so re-sorting the list under the
            // pointer turns a click on Wood into a pull of Stone. Counts change on their own all the
            // time (a smelter finishing, another player moving a stack), so hold the redraw while the
            // pointer is over the list. Typing still redraws: that comes through Rebuild directly.
            if (!force && PointerOverList()) return;

            var fresh = PullerStorage.Census(_chest);

            // Only redraw when something changed, so the list does not flicker or lose the hover once a
            // second while the player is looking at it.
            var sb = new System.Text.StringBuilder();
            foreach (var e in fresh) sb.Append(e.SharedName).Append('=').Append(e.Count).Append(';');
            string key = sb.ToString();
            if (!force && key == _censusKey) return;

            _census = fresh;
            _censusKey = key;
            Rebuild();
        }

        private static void Rebuild()
        {
            if (_content == null) return;

            string filter = _search != null ? (_search.text ?? "").Trim() : "";
            int shown = 0;

            foreach (var e in _census)
            {
                if (filter.Length > 0 &&
                    e.Display.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                    e.SharedName.IndexOf(filter, System.StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var row = RowAt(shown);
                if (row == null) break;
                FillRow(row, e);
                RowNames[shown] = e.SharedName;
                shown++;
            }

            for (int i = shown; i < Rows.Count; i++)
                if (Rows[i] != null && Rows[i].activeSelf) Rows[i].SetActive(false);

            _content.sizeDelta = new Vector2(_content.sizeDelta.x, shown * _rowHeight);

            if (_status != null)
            {
                _status.text = shown > 0
                    ? "Click pulls a stack, Shift-click pulls all"
                    : (_census.Count == 0 ? "Nothing in storage nearby" : "No matches");
            }
        }

        private static void OnRowClick(int index)
        {
            if (_chest == null || index < 0 || index >= RowNames.Count) return;
            string name = RowNames[index];
            if (string.IsNullOrEmpty(name)) return;

            bool all = ZInput.GetKey(KeyCode.LeftShift, false) || ZInput.GetKey(KeyCode.RightShift, false);
            int moved = PullerStorage.Pull(_chest, name, all);

            string display = Localization.instance != null ? Localization.instance.Localize(name) : name;
            Msg(moved > 0
                ? "Pulling " + moved + " " + display
                : "Couldn't pull " + display + ", the chest may be full");

            // Counts change when the transfers land, which for a chest another peer owns is a moment
            // later. Refresh soon rather than waiting out the full interval.
            _censusAt = Time.unscaledTime - CensusInterval + 0.3f;
        }

        // ---- building the panel -------------------------------------------------------------------

        private static bool EnsurePanel(InventoryGui gui)
        {
            if (_panel != null) return true;
            if (gui == null || gui.m_crafting == null) return false;

            // A world reload destroys the panel with the scene, and _panel fake-nulls so we land here
            // again - but these lists still hold the dead rows, and RowAt would hand one back forever.
            // GuiPatch and GatherPatch never hit this because they pool nothing.
            Rows.Clear();
            RowNames.Clear();
            _title = null; _search = null; _scroll = null; _content = null; _status = null; _sendBack = null;

            var crafting = gui.m_crafting;
            var parent = crafting.parent as RectTransform;
            if (parent == null) return false;

            try
            {
                var go = new GameObject("psort_puller_panel", typeof(RectTransform));
                _panel = (RectTransform)go.transform;
                _panel.SetParent(parent, false);
                _panel.anchorMin = crafting.anchorMin;
                _panel.anchorMax = crafting.anchorMax;
                _panel.pivot = crafting.pivot;
                _panel.anchoredPosition = crafting.anchoredPosition;
                _panel.sizeDelta = crafting.sizeDelta;
                _panel.localScale = crafting.localScale;

                // Background: the crafting panel's own if it has one, so the two read as the same frame.
                var bg = go.AddComponent<Image>();
                var srcBg = crafting.GetComponent<Image>();
                if (srcBg != null && srcBg.sprite != null)
                {
                    bg.sprite = srcBg.sprite;
                    bg.type = srcBg.type;
                    bg.color = srcBg.color;
                    bg.pixelsPerUnitMultiplier = srcBg.pixelsPerUnitMultiplier;
                }
                else
                {
                    bg.color = new Color(0f, 0f, 0f, 0.8f);
                }
                bg.raycastTarget = true;

                BuildTitle(gui);
                BuildSearch();
                BuildStatus(gui);
                BuildSendBack(gui);
                BuildList(gui);

                _panel.gameObject.SetActive(false);
                Plugin.Log.LogInfo("[puller] panel built over the crafting panel (" +
                                   crafting.rect.width.ToString("0") + "x" + crafting.rect.height.ToString("0") +
                                   ", search " + (_search != null ? "on" : "unavailable") + ")");
                return true;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("[puller] could not build the panel: " + e);
                if (_panel != null) Object.Destroy(_panel.gameObject);
                _panel = null;
                _search = null;
                _scroll = null;
                _content = null;
                Rows.Clear();
                RowNames.Clear();
                return false;
            }
        }

        private static void BuildTitle(InventoryGui gui)
        {
            if (gui.m_containerName == null) return;
            _title = Object.Instantiate(gui.m_containerName, _panel, false);
            _title.name = "psort_puller_title";
            StripLocalize(_title.gameObject);
            _title.text = "Pull from storage";
            _title.alignment = TextAlignmentOptions.Center;
            Stretch(_title.rectTransform, Pad, -Pad - TitleHeight, -Pad, -Pad, top: true);
        }

        private static void BuildSearch()
        {
            var hud = Hud.instance;
            var src = hud != null && hud.m_buildUi != null && BuildSearchField != null
                ? BuildSearchField.GetValue(hud.m_buildUi) as TMP_InputField
                : null;
            if (src == null)
            {
                Plugin.Log.LogWarning("[puller] build menu search field not found; the Puller list will have no search box");
                return;
            }

            var go = Object.Instantiate(src.gameObject, _panel, false);
            go.name = "psort_puller_search";
            go.SetActive(true);

            // The build menu's key hints ("F to search") mean nothing here.
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string t = mb.GetType().Name;
                if (t == "UIInputHint" || t == "UIGamePad") Object.DestroyImmediate(mb);
            }

            _search = go.GetComponent<TMP_InputField>();
            if (_search == null) { Object.Destroy(go); return; }

            // Fresh event objects: anything the prefab carried belongs to the build menu. BuildUi wires
            // its own listeners at runtime onto the ORIGINAL field, so those never come along anyway.
            _search.onValueChanged = new TMP_InputField.OnChangeEvent();
            _search.onEndEdit = new TMP_InputField.SubmitEvent();
            _search.onSubmit = new TMP_InputField.SubmitEvent();
            _search.onSelect = new TMP_InputField.SelectionEvent();
            _search.onDeselect = new TMP_InputField.SelectionEvent();
            _search.text = "";
            _search.onValueChanged.AddListener(_ => Rebuild());

            var le = go.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = true;

            float top = -Pad - TitleHeight - 4f;
            Stretch((RectTransform)go.transform, Pad, top - SearchHeight, -Pad, top, top: true);
        }

        private static void BuildList(InventoryGui gui)
        {
            if (gui.m_recipeListSpace > 1f) _rowHeight = gui.m_recipeListSpace;

            var view = new GameObject("psort_puller_list", typeof(RectTransform));
            var viewRt = (RectTransform)view.transform;
            viewRt.SetParent(_panel, false);
            viewRt.anchorMin = Vector2.zero;
            viewRt.anchorMax = Vector2.one;
            viewRt.pivot = new Vector2(0.5f, 0.5f);
            float listTop = Pad + TitleHeight + 4f + (_search != null ? SearchHeight + 8f : 0f);
            float bottom = Pad + StatusHeight + (_sendBack != null ? ButtonHeight + 10f : 0f);
            viewRt.offsetMin = new Vector2(Pad, bottom);
            viewRt.offsetMax = new Vector2(-Pad, -listTop);

            // A near-transparent image so the mouse wheel and drag reach the ScrollRect between rows.
            var viewImg = view.AddComponent<Image>();
            viewImg.color = new Color(0f, 0f, 0f, 0.25f);
            view.AddComponent<RectMask2D>();

            var content = new GameObject("psort_puller_content", typeof(RectTransform));
            _content = (RectTransform)content.transform;
            _content.SetParent(viewRt, false);
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = new Vector2(0f, 0f);

            _scroll = view.AddComponent<ScrollRect>();
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 0f;       // the wheel is handled in ScrollWithWheel
            _scroll.viewport = viewRt;
            _scroll.content = _content;
        }

        private static void BuildStatus(InventoryGui gui)
        {
            if (gui.m_containerName == null) return;
            _status = Object.Instantiate(gui.m_containerName, _panel, false);
            _status.name = "psort_puller_status";
            StripLocalize(_status.gameObject);
            _status.alignment = TextAlignmentOptions.Center;
            _status.enableAutoSizing = true;
            _status.fontSizeMax = _status.fontSize * 0.8f;
            _status.fontSizeMin = _status.fontSize * 0.5f;
            _status.text = "";
            var rt = _status.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(Pad, Pad * 0.5f);
            rt.offsetMax = new Vector2(-Pad, Pad * 0.5f + StatusHeight);
        }

        /// <summary>Send back: empty the Puller into storage by hand, without waiting out the timer.
        ///
        /// A Puller Chest is deliberately invisible to the sorter, Organize and Gather, so anything
        /// parked in one is out of reach of everything else the mod does. This is the way back, and
        /// <see cref="PullerBehaviour"/> does the same thing on its own after
        /// <c>[Puller] ReturnAfterSeconds</c>.</summary>
        private static void BuildSendBack(InventoryGui gui)
        {
            if (gui.m_takeAllButton == null) return;

            var btn = Object.Instantiate(gui.m_takeAllButton, _panel, false);
            btn.name = "psort_puller_sendback";

            // All of them: a second Localize would re-localize the subtree a frame after we set the
            // label and put "Take all" back. The tooltip belongs to the button we cloned.
            StripLocalize(btn.gameObject);
            foreach (var gp in btn.GetComponentsInChildren<UIGamePad>(true))
                Object.DestroyImmediate(gp);
            foreach (var mb in btn.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb.GetType().Name == "UITooltip") Object.DestroyImmediate(mb);

            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(OnSendBackClick);

            var label = btn.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                float vanilla = label.fontSize;
                label.enableAutoSizing = true;
                label.fontSizeMax = vanilla;
                label.fontSizeMin = vanilla - 4f;
                label.text = "Send back";
            }

            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(Pad * 3f, Pad * 0.5f + StatusHeight + 6f);
            rt.offsetMax = new Vector2(-Pad * 3f, Pad * 0.5f + StatusHeight + 6f + ButtonHeight);
            rt.localScale = Vector3.one;

            _sendBack = btn;

            // W4's rule: reachable on a controller. The rows are pooled and rebuilt constantly, so
            // they stay mouse-only for now; the search box and this button are the two fixed controls.
            var row = new List<Selectable>();
            if (_search != null) row.Add(_search);
            row.Add(btn);
            GamepadNav.LinkRow(row);
            if (gui.m_takeAllButton != null) GamepadNav.AttachRowToAnchor(gui.m_takeAllButton, row);
        }

        private static void OnSendBackClick()
        {
            if (_chest == null) return;

            int moved = PullerStorage.ReturnAll(_chest, out int types);
            Msg(moved > 0
                ? "Sent back " + moved + " item" + (moved == 1 ? "" : "s") +
                  " (" + types + " type" + (types == 1 ? "" : "s") + ")"
                : "Nothing to send back, or no chest in range has room");

            _censusAt = Time.unscaledTime - CensusInterval + 0.3f;
        }

        /// <summary>The row at <paramref name="index"/>, cloned from the recipe element the crafting
        /// list uses (icon, name, durability bar, quality badge) and pooled, so scrolling through a big
        /// base does not mint new GameObjects every refresh.</summary>
        private static GameObject RowAt(int index)
        {
            while (Rows.Count <= index)
            {
                var gui = InventoryGui.instance;
                if (gui == null || gui.m_recipeElementPrefab == null || _content == null) return null;

                var row = Object.Instantiate(gui.m_recipeElementPrefab, _content, false);
                row.name = "psort_puller_row";
                int i = Rows.Count;

                var rt = (RectTransform)row.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(0f, _rowHeight);
                rt.anchoredPosition = new Vector2(0f, -i * _rowHeight);

                var dur = row.transform.Find("Durability");
                if (dur != null) dur.gameObject.SetActive(false);
                var q = row.transform.Find("QualityLevel");
                if (q != null) q.gameObject.SetActive(false);
                var sel = row.transform.Find("selected");
                if (sel != null) sel.gameObject.SetActive(false);

                var btn = row.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick = new Button.ButtonClickedEvent();
                    btn.onClick.AddListener(() => OnRowClick(i));
                }

                Rows.Add(row);
                RowNames.Add(null);
            }

            var r = Rows[index];
            if (r != null && !r.activeSelf) r.SetActive(true);
            return r;
        }

        private static void FillRow(GameObject row, PullerStorage.Entry e)
        {
            var icon = row.transform.Find("icon");
            if (icon != null)
            {
                var img = icon.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = e.Icon;
                    img.color = Color.white;
                }
            }

            var name = row.transform.Find("name");
            if (name != null)
            {
                var text = name.GetComponent<TMP_Text>();
                if (text != null)
                {
                    text.text = e.Display + "  <color=#9BE07A>" + e.Count + "</color>";
                    text.color = Color.white;
                }
            }
        }

        // ---- helpers ------------------------------------------------------------------------------

        /// <summary>Place <paramref name="rt"/> as a full-width band. With <paramref name="top"/> the
        /// y values are measured down from the panel's top edge.</summary>
        private static void Stretch(RectTransform rt, float left, float bottomY, float right, float topY, bool top)
        {
            float a = top ? 1f : 0f;
            rt.anchorMin = new Vector2(0f, a);
            rt.anchorMax = new Vector2(1f, a);
            rt.pivot = new Vector2(0.5f, a);
            rt.offsetMin = new Vector2(left, bottomY);
            rt.offsetMax = new Vector2(right, topY);
            rt.localScale = Vector3.one;
        }

        private static void StripLocalize(GameObject go)
        {
            foreach (var loc in go.GetComponentsInChildren<Localize>(true))
                Object.DestroyImmediate(loc);
        }

        private static void Msg(string text)
        {
            if (Player.m_localPlayer != null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }
    }

    /// <summary>Vanilla already knows how to stop the player walking around while someone types in a
    /// text field: PlayerController.TakeInput checks BuildUi.SearchFieldFocused, the flag 1.0 added for
    /// the build menu's search. Answering yes to that same question while the Puller search has focus
    /// means WASD types into the box instead of walking away from the chest.</summary>
    [HarmonyPatch(typeof(BuildUi), nameof(BuildUi.SearchFieldFocused), MethodType.Getter)]
    internal static class PullerSearchFocusPatch
    {
        private static void Postfix(ref bool __result)
        {
            if (!__result && PullerPanelPatch.SearchFocused) __result = true;
        }
    }
}
