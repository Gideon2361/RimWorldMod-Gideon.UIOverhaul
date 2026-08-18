using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Integrations;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>
    /// Whether a bar button has an unread count to show, and what it should read.
    ///
    /// <b>One place for it, because the bar draws its buttons from three call sites.</b> A tab can sit directly
    /// on the bar, sit inside a menu, or be a row of the menu popup, and all three go through
    /// <c>UIButtonBarRenderer.Draw</c>. Asking here means a badge appears in all three rather than in whichever
    /// one was remembered.
    ///
    /// <b>The renderer is deliberately not told what a Phinix is.</b> It lays out a button and knows nothing
    /// about who wants a count on it, which is why this sits between them: a second integration wanting a badge
    /// adds a line to <see cref="CountFor"/> and touches neither the renderer nor the bar.
    /// </summary>
    internal static class UIBarBadges
    {
        /// <summary>
        /// Above this the badge stops counting and starts saying "lots".
        ///
        /// A bar button is a few dozen pixels wide and the difference between 100 unread and 250 unread is not
        /// something anybody acts on differently. Two digits and a plus is the widest this is allowed to get.
        /// </summary>
        private const int DisplayCap = 99;

        /// <summary>
        /// The unread count for one tab, or zero when it has nothing to show.
        ///
        /// Guarded, because this is called from the bar's draw path on every frame: an integration reaching into
        /// another mod's state must not be able to take the whole bar down with it.
        /// </summary>
        internal static int CountFor(MainButtonDef def)
        {
            if (def == null)
                return 0;

            return UIGuard.Try("ButtonBar.BadgeCount", () =>
                def.defName == PhinixIntegration.ChatTabDefName ? PhinixIntegration.Unread : 0, 0, null);
        }

        /// <summary>
        /// The combined count for a menu, so a tab folded away inside one still announces itself.
        ///
        /// Summed rather than counting the menus with anything unread: the number on the menu should mean the
        /// same thing as the number on a tab, which is messages rather than tabs.
        /// </summary>
        internal static int CountFor(UIButtonBarEntry menu)
        {
            if (menu?.children == null)
                return 0;

            int total = 0;

            foreach (UIButtonBarEntry child in menu.children)
                total += CountFor(child?.Def);

            return total;
        }

        /// <summary>
        /// The badge text for a count, or null when there is nothing to draw.
        ///
        /// Null rather than an empty string, because that is what the renderer treats as "no badge" and an
        /// empty badge would still reserve its padding and shorten the label.
        /// </summary>
        internal static string Format(int count)
        {
            if (count <= 0)
                return null;

            return count > DisplayCap ? DisplayCap + "+" : count.ToString();
        }

        /// <summary>Convenience for the common case: the badge text for one tab.</summary>
        internal static string For(MainButtonDef def)
        {
            return Format(CountFor(def));
        }
    }
}
