using System.Globalization;
using UnityEngine;

namespace Gideon.UIFramework.Components.Colors
{
    /// <summary>
    /// Turns the color strings written in XML into <see cref="Color"/> values.
    ///
    /// The framework parses these itself rather than leaning on RimWorld's ParseHelper. Two reasons:
    /// hex is what anyone picking colors actually has in front of them, and a parser we own can
    /// report which def and which field were wrong instead of failing somewhere inside the def loader.
    /// </summary>
    public static class UIColorParser
    {
        /// <summary>
        /// The color a role falls back to when its authored value cannot be parsed. Deliberately
        /// hideous: a broken palette entry should be obvious on screen, not quietly black or
        /// invisible against the background it was meant to sit on.
        /// </summary>
        public static readonly Color ErrorColor = new Color(1f, 0f, 1f, 1f);

        /// <summary>
        /// Parses a color string. Accepted forms, all case-insensitive and tolerant of surrounding
        /// whitespace:
        ///
        ///   #RGB       #1A2       shorthand hex, each digit doubled
        ///   #RRGGBB    #15191D    hex, opaque
        ///   #RRGGBBAA  #15191D80  hex with alpha
        ///   r,g,b      0.1,0.2,0.3 or 21,25,29
        ///   r,g,b,a
        ///
        /// The leading # is optional. Comma-separated components may be 0-1 floats or 0-255 values;
        /// if any component exceeds 1 the whole set is read as 0-255. That makes "1,1,1" white rather
        /// than near-black, which is worth knowing but is exactly why hex is the recommended form.
        /// Parentheses around a comma-separated list are accepted so RimWorld's own "(r,g,b,a)" style
        /// does not have to be rewritten.
        /// </summary>
        /// <param name="text">The authored value. Null or blank fails.</param>
        /// <param name="color">The parsed color, or <see cref="ErrorColor"/> on failure.</param>
        /// <param name="error">A description of what was wrong, or null on success.</param>
        /// <returns>True when <paramref name="text"/> was understood.</returns>
        public static bool TryParse(string text, out Color color, out string error)
        {
            color = ErrorColor;
            error = null;

            if (string.IsNullOrEmpty(text))
            {
                error = "value is empty";
                return false;
            }

            string trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                error = "value is blank";
                return false;
            }

            // A comma means a component list; anything else is treated as hex. Checked before the
            // hex path so "(1,1,1)" is not mistaken for malformed hex.
            if (trimmed.IndexOf(',') >= 0)
                return TryParseComponents(trimmed, out color, out error);

            return TryParseHex(trimmed, out color, out error);
        }

        /// <summary>
        /// Convenience form for callers that only want a color and will accept
        /// <see cref="ErrorColor"/> when the text is bad. Prefer
        /// <see cref="TryParse(string, out Color, out string)"/> anywhere the reason matters.
        /// </summary>
        public static Color Parse(string text)
        {
            TryParse(text, out Color color, out string _);
            return color;
        }

        private static bool TryParseHex(string text, out Color color, out string error)
        {
            color = ErrorColor;
            error = null;

            string digits = text[0] == '#' ? text.Substring(1) : text;

            if (digits.Length != 3 && digits.Length != 6 && digits.Length != 8)
            {
                error = $"'{text}' is not a color: hex needs 3, 6 or 8 digits";
                return false;
            }

            // Shorthand: #1A2 means #11AA22, the same expansion CSS uses.
            if (digits.Length == 3)
            {
                digits = new string(new[]
                {
                    digits[0], digits[0],
                    digits[1], digits[1],
                    digits[2], digits[2]
                });
            }

            if (!uint.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            {
                error = $"'{text}' is not a color: '{digits}' is not hexadecimal";
                return false;
            }

            if (digits.Length == 6)
            {
                color = new Color(
                    ((value >> 16) & 0xFF) / 255f,
                    ((value >> 8) & 0xFF) / 255f,
                    (value & 0xFF) / 255f,
                    1f);
                return true;
            }

            color = new Color(
                ((value >> 24) & 0xFF) / 255f,
                ((value >> 16) & 0xFF) / 255f,
                ((value >> 8) & 0xFF) / 255f,
                (value & 0xFF) / 255f);
            return true;
        }

        private static bool TryParseComponents(string text, out Color color, out string error)
        {
            color = ErrorColor;
            error = null;

            string body = text;
            if (body.Length >= 2 && body[0] == '(' && body[body.Length - 1] == ')')
                body = body.Substring(1, body.Length - 2);

            string[] parts = body.Split(',');
            if (parts.Length != 3 && parts.Length != 4)
            {
                error = $"'{text}' is not a color: expected 3 or 4 components, found {parts.Length}";
                return false;
            }

            float[] values = new float[4];
            values[3] = 1f;

            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                        out values[i]))
                {
                    error = $"'{text}' is not a color: component '{parts[i].Trim()}' is not a number";
                    return false;
                }
            }

            // Any component above 1 means the author was working in 0-255, including the alpha.
            bool byteScale = false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (values[i] > 1f)
                    byteScale = true;
            }

            if (byteScale)
            {
                for (int i = 0; i < parts.Length; i++)
                    values[i] /= 255f;
            }

            color = new Color(values[0], values[1], values[2], values[3]);
            return true;
        }
    }
}
