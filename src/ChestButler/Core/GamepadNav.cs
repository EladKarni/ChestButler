using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace ChestButler.Core
{
    /// <summary>W4 — makes ChestButler's buttons reachable on a controller.
    ///
    /// The roadmap framed this as "re-enable <c>UIGamePad</c> and add key hints", because
    /// <c>GuiPatch.MakeButton</c> strips <c>UIGamePad</c> from every clone. Reading the game says that
    /// framing leads somewhere bad, and there is a better route:
    ///
    /// - <c>UIGamePad</c> is NOT a navigation component. It binds one gamepad button
    ///   (<c>m_zinputKey</c>) directly to one UI Button. Keeping it on a clone would make our button
    ///   fire on the *same* gamepad button as the vanilla button it was cloned from — which is exactly
    ///   why the original author strips it, and re-enabling it as written would double-fire Take All.
    ///   Assigning fresh keys instead needs to know which gamepad buttons are still free while each
    ///   panel is open, and that is Unity-serialized prefab data that cannot be read offline (roadmap
    ///   §9 item 6, still unanswered).
    /// - <c>UIGroupHandler</c> (assembly_guiutils) drives focus with plain Unity
    ///   <c>Selectable</c> navigation — it has <c>m_defaultElement</c>, a private
    ///   <c>FindSelectable(GameObject)</c> and <c>ResetActiveElement()</c>. Our buttons are already
    ///   <c>Button</c>s, hence already <c>Selectable</c>s.
    ///
    /// So the fix is explicit navigation links, not key bindings: no free-button hunt, no conflict with
    /// vanilla shortcuts, and no need for key hints at all, since Unity's own selection highlight shows
    /// which button is focused. <c>UIGamePad</c> stays stripped.
    ///
    /// Explicit rather than <c>Navigation.Mode.Automatic</c> deliberately: automatic navigation picks
    /// neighbours by geometry, and this toolbar moves — it is re-anchored under the item grid every time
    /// a differently-sized chest opens — so geometric guessing would silently change which button the
    /// D-pad reaches.</summary>
    internal static class GamepadNav
    {
        private static ConfigEntry<bool> _enabled;
        private static bool _bound;

        /// <summary>Bound lazily off <c>Plugin.Instance.Config</c> rather than from a line in
        /// <c>Plugin.Awake</c>. Wave 0 never stubbed an Init for W4, and roadmap §3 lists W4 as touching
        /// only the two patch files — so binding here keeps that footprint honest.</summary>
        internal static bool Enabled
        {
            get
            {
                if (!_bound)
                {
                    _bound = true;
                    var plugin = Plugin.Instance;
                    if (plugin != null)
                    {
                        _enabled = plugin.Config.Bind("Gamepad", "Enabled", true,
                            new ConfigDescription(
                                "Let a controller reach ChestButler's buttons by linking them into the panel's " +
                                "D-pad navigation. Client-side.",
                                null, new ConfigurationManagerAttributes { IsAdminOnly = false }));
                    }
                }
                return _enabled == null || _enabled.Value;
            }
        }

        /// <summary>Chain a row of buttons left-to-right. Only currently-active buttons are included:
        /// explicit navigation into an inactive Selectable dead-ends, and this toolbar hides buttons
        /// depending on whether the chest is a sorter, has pins and so on — so the chain has to be
        /// rebuilt whenever that visibility changes, not once at construction.</summary>
        internal static void LinkRow(IList<Selectable> row)
        {
            if (!Enabled || row == null) return;

            var live = new List<Selectable>();
            for (int i = 0; i < row.Count; i++)
            {
                var s = row[i];
                if (s != null && s.gameObject.activeInHierarchy) live.Add(s);
            }
            if (live.Count == 0) return;

            for (int i = 0; i < live.Count; i++)
            {
                var nav = live[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnLeft = i > 0 ? live[i - 1] : null;
                nav.selectOnRight = i < live.Count - 1 ? live[i + 1] : null;
                live[i].navigation = nav;
            }
        }

        /// <summary>Link two buttons vertically. <paramref name="onlyIfVanillaEmpty"/> guards the
        /// direction that points back into a VANILLA button: if the game already routes that way we
        /// leave its link alone rather than stealing it, because breaking existing controller
        /// navigation to add ours would be a bad trade. It logs which way it went so the in-game check
        /// has something to read.</summary>
        internal static void LinkVertical(Selectable above, Selectable below, bool onlyIfVanillaEmpty)
        {
            if (!Enabled || above == null || below == null) return;

            var downNav = above.navigation;
            var upNav = below.navigation;

            bool wroteDown = false;
            if (!onlyIfVanillaEmpty || downNav.selectOnDown == null)
            {
                downNav.mode = Navigation.Mode.Explicit;
                downNav.selectOnDown = below;
                above.navigation = downNav;
                wroteDown = true;
            }

            upNav.mode = Navigation.Mode.Explicit;
            upNav.selectOnUp = above;
            below.navigation = upNav;

            if (!wroteDown)
                Plugin.Log.LogDebug("[gamepad] '" + above.name + "' already navigates down to '" +
                    (downNav.selectOnDown != null ? downNav.selectOnDown.name : "?") +
                    "'; left it alone. Our button is reachable upward from '" + below.name + "' only.");
        }

        /// <summary>Attach the chain that ends at <paramref name="entry"/> to a vanilla anchor above it.</summary>
        internal static void AttachRowToAnchor(Selectable anchor, IList<Selectable> row)
        {
            if (!Enabled || anchor == null || row == null) return;
            for (int i = 0; i < row.Count; i++)
            {
                var s = row[i];
                if (s == null || !s.gameObject.activeInHierarchy) continue;
                LinkVertical(anchor, s, onlyIfVanillaEmpty: true);
                return;                                  // first live button in the row is the entry
            }
        }
    }
}
