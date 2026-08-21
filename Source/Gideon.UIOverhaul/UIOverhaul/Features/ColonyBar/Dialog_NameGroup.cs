using System;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.ColonyBar
{
    /// <summary>
    /// Names a group, whether it is being created or renamed.
    ///
    /// <b>A window rather than a float menu, because naming takes typing,</b> and typing in RimWorld has to go
    /// through <see cref="UITextBoxControl"/>: a bare text field lets the camera keep its key handlers, so typing
    /// "was" in a name walks the view across the map.
    ///
    /// <b>One window for create and rename.</b> The difference is only whether the group already exists, which is
    /// the caller's business, so the caller hands over what to do with the name.
    /// </summary>
    public class Dialog_NameGroup : Window
    {
        private static readonly UITextBoxControl Field = new UITextBoxControl
        {
            Placeholder = "Group name",
            MaxLength = 40
        };

        private readonly string heading;
        private readonly string seed;
        private readonly Action<string> accepted;

        public Dialog_NameGroup(string title, string current, Action<string> onAccepted)
        {
            heading = title;
            seed = current ?? string.Empty;
            accepted = onAccepted;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
        }

        /// <summary>Renames an existing group in place.</summary>
        internal static Dialog_NameGroup For(PawnGroup group, Action changed)
        {
            return new Dialog_NameGroup("Rename group", group?.Name, name =>
            {
                if (group == null)
                    return;

                group.Name = name;

                changed?.Invoke();
            });
        }

        public override Vector2 InitialSize => new Vector2(420f, 190f);

        public override void PostOpen()
        {
            base.PostOpen();

            // The box is static and shared, so it carries whatever was typed last time. Seeded rather than
            // cleared, since a rename starts from the current name.
            Field.Text = seed;
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Bar.NameGroup", inRect, () => Contents(inRect),
                "This window failed to draw. Nothing has been renamed.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont font = Text.Font;
            Color color = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), heading);

                Text.Font = GameFont.Small;

                Field.Draw(new Rect(inRect.x, inRect.y + 40f, inRect.width, 30f), palette);

                Rect ok = new Rect(inRect.xMax - 110f, inRect.yMax - 34f, 110f, 32f);
                Rect cancel = new Rect(ok.x - 118f, ok.y, 110f, 32f);

                if (GzpPalette.GrayButton(cancel, "Cancel"))
                    Close();

                // A blank name is refused rather than accepted and defaulted: a group called "Group 3" that the
                // player did not type reads as a bug, and the button being dead says why without a message.
                if (GzpPalette.GrayButton(ok, "Save", !Field.Text.NullOrEmpty(), true))
                {
                    accepted?.Invoke(Field.Text.Trim());

                    Close();
                }
            }
            finally
            {
                Text.Font = font;
                GUI.color = color;
            }
        }
    }
}
