using System.Globalization;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Bills
{
    /// <summary>
    /// A number you can either type or step, for the bill editor.
    ///
    /// <b>Typed, because stepping to 2000 is not a feature.</b> The stepper alone means holding a button through
    /// two thousand clicks, or a hundred at ten a time with shift held, to say a number the player already knows.
    /// The buttons stay for nudging, which is what they are good at.
    ///
    /// <b>It has to be a <see cref="UITextBoxControl"/> and nothing else.</b> RimWorld's camera reads raw key
    /// state, so any other text field lets W, A, S and D drive the map while somebody types into it. That control
    /// is the only thing in this mod that holds the gate shut, and it is the reason this class exists rather than
    /// a call to <c>Widgets.TextFieldNumeric</c>.
    ///
    /// <b>The low bound is applied when typing finishes, not while it happens.</b> A radius that cannot go below
    /// three would otherwise turn the first keystroke of "30" into a 3 and leave nowhere to put the zero. Until
    /// focus is lost the box only guards the ceiling, and the floor is applied once the number is complete.
    ///
    /// <b>One box is one place on screen, not one bill.</b> The editor shows a single bill at a time, so the
    /// control refills itself whenever the subject changes. Keeping a box per bill would leak one per bill the
    /// player ever clicked.
    /// </summary>
    internal sealed class BillNumberBox
    {
        private readonly UITextBoxControl box = new UITextBoxControl
        {
            MaxLength = 5,
            ShowClearButton = false
        };

        /// <summary>What the box is currently showing a number for, compared by reference.</summary>
        private object owner;

        /// <summary>
        /// The number the text last stood for.
        ///
        /// Compared against the live value to notice a change made anywhere else, such as the stepper on the row
        /// or another window, without stealing the text back while somebody is mid-word.
        /// </summary>
        private int shown = int.MinValue;

        /// <summary>
        /// Draws the control and returns what the number should now be.
        ///
        /// A null <paramref name="label"/> draws no label and gives the whole rectangle to the field and its two
        /// buttons, which is what a bill row wants: the row already says what the number is for, and repeating it
        /// in 96 pixels the row does not have would cost the field instead.
        /// </summary>
        internal int Draw(Rect rect, UIColorPaletteDef palette, string label, object subject, int value, int low,
            int high)
        {
            float labelWidth = 0f;

            if (label != null)
            {
                labelWidth = 96f;

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                Widgets.Label(new Rect(rect.x, rect.y, labelWidth - 6f, rect.height), label);

                Text.Anchor = TextAnchor.UpperLeft;
            }

            Rect minus = new Rect(rect.x + labelWidth, rect.y, 26f, rect.height);
            Rect plus = new Rect(rect.xMax - 26f, rect.y, 26f, rect.height);
            Rect field = new Rect(minus.xMax + 2f, rect.y, plus.x - minus.xMax - 4f, rect.height);

            if (!ReferenceEquals(owner, subject) || (!box.Focused && (box.IsEmpty || shown != value)))
                Fill(subject, value);

            bool held = box.Focused;

            box.Draw(field, palette);

            int result = value;
            int typed;

            if (int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out typed))
                result = Mathf.Clamp(typed, 0, high);

            // Focus has just gone, so the number is finished and the floor can be applied without fighting the
            // next keystroke.
            if (held && !box.Focused)
            {
                result = Mathf.Clamp(result, low, high);

                Fill(subject, result);
            }

            int step = Event.current != null && Event.current.shift ? 10 : 1;

            if (Step(minus, "-", palette))
            {
                result = Mathf.Clamp(result - step, low, high);

                Fill(subject, result);
            }

            if (Step(plus, "+", palette))
            {
                result = Mathf.Clamp(result + step, low, high);

                Fill(subject, result);
            }

            shown = result;

            return result;
        }

        /// <summary>Puts a number in the box and records what it stands for.</summary>
        private void Fill(object subject, int value)
        {
            owner = subject;
            shown = value;
            box.Text = value.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// One end of the stepper.
        ///
        /// <b>The mod's own button rather than a painted rect,</b> changed on 2026-08-23: Aaron said these did
        /// not look like buttons and he was right. <c>PaintButton</c> draws a bordered raised box, which is a
        /// button on its own and is not one sitting against a text field that has the same border and the same
        /// height -- the row read as three fields, one of which happened to contain a minus sign.
        /// <c>TabParts.Button</c> is what every button he does recognise is drawn with, so using it makes the
        /// question moot rather than answering it with another shade of grey.
        /// </summary>
        private static bool Step(Rect rect, string glyph, UIColorPaletteDef palette)
        {
            return TabParts.Button(rect, glyph, palette);
        }
    }
}
