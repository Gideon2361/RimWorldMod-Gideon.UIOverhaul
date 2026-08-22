using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Helpers
{
    /// <summary>
    /// Drawing a one-line label that may contain colour tags, without breaking them.
    ///
    /// <b>The fault this exists for.</b> RimWorld puts rich text in labels all over the game -- an apparel label
    /// carries a coloured quality, a hediff carries a coloured severity, a faction carries a coloured relation --
    /// and <c>Widgets.LabelEllipses</c> cannot survive one. It calls
    /// <c>Text.ClampTextWithEllipsis</c>, which measures with <c>Text.CalcSize</c> and shortens by
    /// <i>characters of the raw string</i>. Those two do not agree: <c>CalcSize</c> calls <c>StripTags</c> on its
    /// way in, so it measures the text a reader sees, while the shortening chops the markup a reader does not.
    /// Cut a label in the middle of <c>&lt;color=#FFEB04&gt;</c> and Unity finds an unterminated tag, gives up on
    /// parsing it, and prints it as words. That is the raw <c>&lt;color=#FFEB0</c> that appeared across the gear
    /// body's apparel list.
    ///
    /// <b>So the shortening is done here, in visible characters, and the open tags are closed afterwards.</b> The
    /// colour survives, the label fits, and the ellipsis lands where a reader would put it.
    ///
    /// <b>It also gives back the thirteen pixels.</b> <c>ClampTextWithEllipsis</c> keeps 13 pixels in hand before
    /// it will accept a string, so a rect measured with <c>Text.CalcSize</c> and then handed to it is always
    /// thirteen pixels too small and the label ellipsises for no visible reason -- which is why a condition line
    /// reading "Healthy" came out as "Hea...". <see cref="WidthOf"/> is what a caller should size a lane with.
    /// </summary>
    internal static class UIRichText
    {
        /// <summary>
        /// What <c>Text.ClampTextWithEllipsis</c> holds back, and therefore what a caller has to add on.
        ///
        /// Vanilla's own literal, not a guess: the method compares against <c>rect.width - 13f</c> twice.
        /// </summary>
        internal const float EllipsisReserve = 13f;

        /// <summary>
        /// How wide a lane has to be for this text to be drawn whole.
        ///
        /// <c>Text.CalcSize</c> strips tags itself, so markup costs nothing here, which is correct: it costs
        /// nothing on screen either.
        /// </summary>
        internal static float WidthOf(string text)
        {
            if (text.NullOrEmpty())
                return 0f;

            return Text.CalcSize(text).x + EllipsisReserve;
        }

        /// <summary>
        /// A single-line label, shortened to fit without breaking a tag.
        ///
        /// Text with no markup goes straight to vanilla's own path, so the common case costs nothing extra and
        /// behaves exactly as it did.
        /// </summary>
        internal static void Label(Rect rect, string text)
        {
            if (text == null)
                text = string.Empty;

            if (text.IndexOf('<') < 0)
            {
                Widgets.LabelEllipses(rect, text);

                return;
            }

            Widgets.Label(rect, Clamp(rect.width, text));
        }

        /// <summary>
        /// The text, shortened to <paramref name="width"/> in visible characters, with every tag it opened
        /// closed again.
        ///
        /// <b>Binary search rather than a character at a time,</b> because this runs every frame for every
        /// tagged label on the panel and <c>GUIStyle.CalcSize</c> is not free. Six measurements settle a
        /// sixty-character label; peeling one character at a time would take sixty.
        /// </summary>
        internal static string Clamp(float width, string text)
        {
            float limit = width - EllipsisReserve;

            if (limit <= 0f)
                return string.Empty;

            if (Text.CalcSize(text).x <= limit)
                return text;

            string visible = text.StripTags();

            int fits = Fitting(visible, limit);

            if (fits <= 0)
                return "...";

            return Rebuild(text, fits);
        }

        /// <summary>
        /// How many visible characters fit, allowing for the ellipsis that will follow them.
        ///
        /// The answer is monotonic in the length, which is what makes the search valid: a longer prefix of the
        /// same string is never narrower.
        /// </summary>
        private static int Fitting(string visible, float limit)
        {
            int low = 0;
            int high = visible.Length;

            while (low < high)
            {
                int middle = (low + high + 1) / 2;

                if (Text.CalcSize(visible.Substring(0, middle) + "...").x <= limit)
                    low = middle;
                else
                    high = middle - 1;
            }

            return low;
        }

        /// <summary>
        /// Walks the original text again, copying markup and the first <paramref name="visibleWanted"/> visible
        /// characters, then closing whatever is still open.
        ///
        /// <b>Tags are copied whole and never counted,</b> which is the whole point: a cut can land between two
        /// characters and never inside <c>&lt;color=#FFEB04&gt;</c>.
        /// </summary>
        private static string Rebuild(string text, int visibleWanted)
        {
            StringBuilder built = new StringBuilder(text.Length + 16);
            List<string> open = new List<string>();

            int taken = 0;
            int at = 0;

            while (at < text.Length && taken < visibleWanted)
            {
                if (text[at] == '<')
                {
                    int close = text.IndexOf('>', at);

                    // An unterminated angle bracket is not markup, whatever it was meant to be. Treated as a
                    // character so the rest of the label still draws rather than being thrown away.
                    if (close < 0)
                    {
                        built.Append(text[at]);
                        taken++;
                        at++;

                        continue;
                    }

                    string tag = text.Substring(at, close - at + 1);

                    built.Append(tag);
                    Track(open, tag);

                    at = close + 1;

                    continue;
                }

                built.Append(text[at]);
                taken++;
                at++;
            }

            built.Append("...");

            for (int i = open.Count - 1; i >= 0; i--)
                built.Append("</").Append(open[i]).Append('>');

            return built.ToString();
        }

        /// <summary>
        /// Keeps the stack of tags that are still open at this point in the text.
        ///
        /// A closing tag pops its own name rather than the top of the stack, so overlapping markup from two
        /// different sources -- which happens the moment one label is built from two -- does not leave the stack
        /// wrong for everything after it.
        /// </summary>
        private static void Track(List<string> open, string tag)
        {
            if (tag.Length < 3)
                return;

            bool closing = tag[1] == '/';

            int from = closing ? 2 : 1;
            int to = from;

            while (to < tag.Length && tag[to] != '=' && tag[to] != '>' && tag[to] != ' ')
                to++;

            if (to <= from)
                return;

            string name = tag.Substring(from, to - from);

            if (!closing)
            {
                open.Add(name);

                return;
            }

            for (int i = open.Count - 1; i >= 0; i--)
            {
                if (open[i] != name)
                    continue;

                open.RemoveAt(i);

                return;
            }
        }
    }
}
