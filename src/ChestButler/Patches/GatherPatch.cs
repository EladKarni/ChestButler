using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using ChestButler.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChestButler.Patches
{
    /// <summary>W2 — the Gather button in InventoryGui's CRAFTING panel, plus "(N in storage)" beside
    /// each ingredient.
    ///
    /// Deliberately a separate Harmony patch class from <see cref="GuiPatch"/>: that one lives in the
    /// chest toolbar and clones <c>m_takeAllButton</c>, this one lives in the crafting panel and clones
    /// <c>m_craftButton</c>. Two independent classes can both postfix InventoryGui, so W1 and W2 share
    /// no UI code (roadmap §3).
    ///
    /// The requirement list is read out of the UI callback rather than out of the selected recipe.
    /// <c>InventoryGui.SetupRequirement</c> hands us the Requirement, the quality and the craft
    /// multiplier for each ingredient actually being displayed, which is correct on both the Craft and
    /// Upgrade tabs by construction — and it avoids <c>m_selectedRecipe</c> entirely, whose type
    /// (<c>InventoryGui.RecipeDataPair</c>) is nested-PRIVATE and cannot be named in source. See
    /// docs/gather-plan.md §2.</summary>
    [HarmonyPatch(typeof(InventoryGui))]
    internal static class GatherPatch
    {
        private static Button _gatherBtn;
        private static TMP_Text _gatherLabel;

        /// <summary>What the panel is displaying right now, rebuilt on every refresh.</summary>
        private static readonly List<GatherNeed> Needs = new List<GatherNeed>();
        private static readonly HashSet<string> Seen = new HashSet<string>();
        private static bool _onlyOneIngredient;
        private static List<Container> _sources;

        /// <summary>Hard cap: if the SetupRequirementList prefix ever fails to bind, Needs must not grow
        /// without bound. Vanilla recipes list at most four.</summary>
        private const int MaxNeeds = 16;

        // m_selectedRecipe is a private field of a nested-PRIVATE struct, so the only way to reach
        // Recipe.m_requireOnlyOneIngredient is reflection. Guarded: the audit found "one static
        // initializer that would have taken down an entire Harmony patch class on a mistyped field
        // name", so nothing here throws at type-load and every use is null-checked.
        private static readonly FieldInfo SelectedRecipeField =
            AccessTools.Field(typeof(InventoryGui), "m_selectedRecipe");
        private static readonly MethodInfo RecipeGetter =
            SelectedRecipeField != null ? AccessTools.PropertyGetter(SelectedRecipeField.FieldType, "Recipe") : null;
        private static bool _reflectionWarned;

        private static Recipe SelectedRecipe(InventoryGui gui)
        {
            if (gui == null || SelectedRecipeField == null || RecipeGetter == null)
            {
                if (!_reflectionWarned)
                {
                    _reflectionWarned = true;
                    Plugin.Log.LogWarning("[gather] could not reach InventoryGui.m_selectedRecipe; " +
                        "require-only-one-ingredient recipes will be treated as normal ones");
                }
                return null;
            }
            try
            {
                var pair = SelectedRecipeField.GetValue(gui);
                return pair == null ? null : RecipeGetter.Invoke(pair, null) as Recipe;
            }
            catch (Exception e)
            {
                if (!_reflectionWarned)
                {
                    _reflectionWarned = true;
                    Plugin.Log.LogWarning("[gather] reading m_selectedRecipe failed: " + e.Message);
                }
                return null;
            }
        }

        // ---- capture what the panel is showing ----------------------------------------------------

        /// <summary>A refresh is starting: drop the previous list and re-read the one recipe-level flag
        /// the per-ingredient callback does not carry.</summary>
        [HarmonyPrefix, HarmonyPatch("SetupRequirementList")]
        private static void SetupRequirementListPrefix(InventoryGui __instance)
        {
            Needs.Clear();
            Seen.Clear();
            _sources = null;
            var recipe = SelectedRecipe(__instance);
            _onlyOneIngredient = recipe != null && recipe.m_requireOnlyOneIngredient;
        }

        /// <summary>One ingredient row was just built. Record it, and annotate it with what storage holds.
        /// Parameter names must match the game's exactly or Harmony will not inject them — verified
        /// against assembly_valheim.dll as
        /// <c>SetupRequirement(Transform elementRoot, Piece.Requirement req, Player player, bool craft,
        /// int quality, int craftMultiplier)</c>, which is SIX parameters (the roadmap recorded five).</summary>
        [HarmonyPostfix, HarmonyPatch("SetupRequirement")]
        private static void SetupRequirementPostfix(Transform elementRoot, Piece.Requirement req,
            Player player, bool craft, int quality, int craftMultiplier, bool __result)
        {
            if (!__result) return;                                  // row not shown
            if (req?.m_resItem?.m_itemData?.m_shared == null) return;
            if (player == null) return;

            string sharedName = req.m_resItem.m_itemData.m_shared.m_name;
            if (string.IsNullOrEmpty(sharedName)) return;

            int perCraft = req.GetAmount(quality);
            if (perCraft <= 0) return;
            int needed = perCraft * Mathf.Max(1, craftMultiplier);

            var inv = player.GetInventory();
            int inPlayer = inv != null ? inv.CountItems(sharedName, -1, true) : 0;

            if (_sources == null) _sources = Gatherer.Sources();
            int inStorage = Gatherer.CountInStorage(_sources, sharedName);

            if (Seen.Add(sharedName) && Needs.Count < MaxNeeds)
            {
                Needs.Add(new GatherNeed
                {
                    SharedName = sharedName,
                    Display = Names.Normalize(sharedName),
                    Needed = needed,
                    InPlayer = inPlayer,
                    InStorage = inStorage,
                });
            }

            if (Gather.CountsShown && inStorage > 0)
                Annotate(elementRoot, inStorage);
        }

        /// <summary>Append "(N in storage)" to the row's amount text. The <c>res_amount</c> TMP_Text
        /// child is the known-working anchor for this (MyLittleUI uses it); the fallback exists because
        /// a child name is exactly the kind of thing a game update renames.</summary>
        private static void Annotate(Transform elementRoot, int inStorage)
        {
            if (elementRoot == null) return;

            TMP_Text text = null;
            var child = elementRoot.Find("res_amount");
            if (child != null) text = child.GetComponent<TMP_Text>();
            if (text == null)
            {
                var all = elementRoot.GetComponentsInChildren<TMP_Text>(true);
                // last one is the amount in vanilla's layout; be forgiving rather than clever
                if (all != null && all.Length > 0) text = all[all.Length - 1];
            }
            if (text == null) return;

            const string marker = "  <color=#9BE07A>(";
            if (text.text != null && text.text.Contains(marker)) return;   // already annotated this frame
            text.text += marker + inStorage + " stored)</color>";
        }

        // ---- the button ---------------------------------------------------------------------------

        [HarmonyPostfix, HarmonyPatch("Show")]
        private static void ShowPostfix(InventoryGui __instance) => EnsureButton(__instance);

        [HarmonyPostfix, HarmonyPatch("UpdateRecipe")]
        private static void UpdateRecipePostfix(InventoryGui __instance)
        {
            EnsureButton(__instance);
            RefreshButton();
        }

        [HarmonyPostfix, HarmonyPatch("Hide")]
        private static void HidePostfix()
        {
            if (_gatherBtn != null) _gatherBtn.gameObject.SetActive(false);
        }

        private static void EnsureButton(InventoryGui gui)
        {
            if (_gatherBtn != null || gui == null) return;
            var craft = gui.m_craftButton;
            if (craft == null) return;

            var srcRt = craft.GetComponent<RectTransform>();
            if (srcRt == null || srcRt.parent == null) return;

            var btn = UnityEngine.Object.Instantiate(craft, srcRt.parent);
            btn.name = "psort_gather";

            // Same treatment as the chest toolbar: drop the localizer so our label survives, and the
            // gamepad hint, which W4 re-adds deliberately across every ChestButler button at once.
            var loc = btn.GetComponentInChildren<Localize>(true);
            if (loc != null) UnityEngine.Object.DestroyImmediate(loc);
            foreach (var gp in btn.GetComponentsInChildren<UIGamePad>(true))
                UnityEngine.Object.DestroyImmediate(gp);

            btn.onClick = new Button.ButtonClickedEvent();
            btn.onClick.AddListener(OnGatherClick);

            var rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = srcRt.anchorMin;
            rt.anchorMax = srcRt.anchorMax;
            rt.pivot = srcRt.pivot;
            rt.sizeDelta = srcRt.sizeDelta;
            // Directly above the Craft button, one height plus a small gap.
            rt.anchoredPosition = srcRt.anchoredPosition + new Vector2(0f, srcRt.rect.height + 6f);

            _gatherLabel = btn.GetComponentInChildren<TMP_Text>();
            if (_gatherLabel != null)
            {
                float vanilla = _gatherLabel.fontSize;
                _gatherLabel.enableAutoSizing = true;
                _gatherLabel.fontSizeMax = vanilla;
                _gatherLabel.fontSizeMin = vanilla - 4f;
                _gatherLabel.text = "Gather";
            }

            _gatherBtn = btn;
        }

        private static void RefreshButton()
        {
            if (_gatherBtn == null) return;

            if (!Gather.IsEnabled) { _gatherBtn.gameObject.SetActive(false); return; }

            var resolved = GatherMath.Resolve(Needs, _onlyOneIngredient);
            int total = 0;
            foreach (var n in resolved) total += n.Gatherable;

            bool anything = total > 0;
            _gatherBtn.gameObject.SetActive(Needs.Count > 0);
            _gatherBtn.interactable = anything;
            if (_gatherLabel != null)
                _gatherLabel.text = anything ? "Gather (" + total + ")" : "Gather";

            // W4: Gather sits directly above the Craft button, so that is the natural D-pad route into
            // it. Core/GamepadNav.cs explains why this is navigation rather than a UIGamePad key.
            var gui = InventoryGui.instance;
            if (gui != null && gui.m_craftButton != null && _gatherBtn.gameObject.activeInHierarchy)
                GamepadNav.LinkVertical(_gatherBtn, gui.m_craftButton, onlyIfVanillaEmpty: false);
        }

        private static void OnGatherClick()
        {
            if (!Gather.IsEnabled) return;
            if (Player.m_localPlayer == null) return;

            var resolved = GatherMath.Resolve(Needs, _onlyOneIngredient);
            if (resolved.Count == 0)
            {
                Msg(Needs.Count == 0
                    ? "Select a recipe first"
                    : "Nothing to gather - nearby chests have none of what this needs");
                return;
            }

            Gatherer.Pull(resolved, out int moved, out int types);
            Msg(moved > 0
                ? "Gathered " + moved + " item" + (moved == 1 ? "" : "s") +
                  " (" + types + " type" + (types == 1 ? "" : "s") + ")"
                : "Nothing could be gathered - your inventory may be full");
        }

        private static void Msg(string text)
        {
            if (Player.m_localPlayer != null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, text);
        }
    }
}
