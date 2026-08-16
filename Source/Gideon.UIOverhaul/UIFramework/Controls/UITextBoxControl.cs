using System;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using UnityEngine;
using Verse;

namespace Gideon.UIFramework.Controls
{
    /// <summary>
    /// A text box in the theme, and the only one of our controls that owns keyboard focus.
    ///
    /// An object rather than a static helper, unlike <see cref="UICheckboxControl"/>: a text box holds three
    /// things between frames -- its text, its control name, and whether the player means it to be focused --
    /// and the last of those is the reason this type exists at all.
    ///
    /// <b>Why not vanilla's QuickSearchWidget.</b> Two reasons, one of which took a while to see.
    ///
    /// The visible one is that focus in IMGUI is fragile in a way <c>GUI.SetNextControlName</c> does not fix.
    /// Unity keys keyboard focus on <c>GUIUtility.keyboardControl</c>, an integer derived from draw order;
    /// naming a control only hangs a name on whichever id it happened to receive that frame. If the id shifts
    /// -- because a control ahead of it appeared, disappeared, or was reordered -- <c>keyboardControl</c> still
    /// points at the old integer, <c>GUI.GetNameOfFocusedControl()</c> stops returning our name, and the field
    /// silently goes dead mid-word. Vanilla's widget has no defense against this; it only ever asks whether it
    /// is focused, and believes the answer.
    ///
    /// This one repairs it. Each frame it compares the focused control's name against its own, and when it
    /// believes it should be focused but <i>nothing</i> is, it re-asserts focus by name. That fixes the fault
    /// without needing to know which control shifted, which is the point -- the cause is in Unity's id
    /// allocation, where we cannot see it. It deliberately does not fight for focus: if some <i>other</i> named
    /// control holds it, the player clicked away and this box concedes. See <see cref="ResolveFocus"/>.
    ///
    /// The subtler reason is that a search field is not the only thing a mod needs a text box for, and
    /// <c>QuickSearchWidget</c> hard-codes a magnifier, a filter object and a 30-character cap.
    ///
    /// <b>Key bindings.</b> Every <c>KeyBindingDef</c> in the game -- camera dolly included -- is suppressed
    /// only while <c>WindowStack.AnySearchWidgetFocused</c> is true, and that walks the window stack looking
    /// for a <c>QuickSearchWidget</c>. A control of ours is invisible to it, so W and A would pan the map as
    /// the player typed. <see cref="AnyFocused"/> is what
    /// <c>Gideon.UIFramework.Patches.UIElements.Patch_WindowStack_AnySearchWidgetFocused</c> ORs into that
    /// gate, which gives every text box of ours the same protection vanilla gives its own, anywhere in the
    /// game, with no per-window wiring.
    ///
    /// <code>
    /// private static readonly UITextBoxControl Search = new UITextBoxControl
    /// {
    ///     Placeholder = "Search",
    ///     Icon = TexButton.Search
    /// };
    ///
    /// if (Search.Draw(rect))
    ///     scroll = Vector2.zero;
    /// </code>
    /// </summary>
    public class UITextBoxControl
    {
        /// <summary>Height that matches the rest of the theme's inputs. The caller sets the real rect.</summary>
        public const float DefaultHeight = 26f;

        private const float IconSize = 18f;
        private const float EdgePad = 4f;

        /// <summary>
        /// Distinguishes one box from another in Unity's focus table.
        ///
        /// Per instance and never reused, the same approach vanilla takes for its widget. Two boxes sharing a
        /// name would each believe the other's focus was their own.
        /// </summary>
        private static int instanceCounter;

        /// <summary>
        /// The box that currently holds focus, for <see cref="AnyFocused"/>.
        ///
        /// A reference rather than a bool, so that a second box taking focus displaces the first rather than
        /// leaving two of them each believing they have it.
        /// </summary>
        private static UITextBoxControl focusedBox;

        /// <summary>
        /// The frame <see cref="focusedBox"/> last drew on.
        ///
        /// Without this, closing a window while its box was focused would leave the key-binding gate held shut
        /// forever -- the box stops drawing, so nothing ever clears the reference. Staleness is judged rather
        /// than trusted. See <see cref="AnyFocused"/>.
        /// </summary>
        private static int focusedFrame = -1;

        private readonly string controlName;

        private string text = "";

        /// <summary>
        /// Whether the player means this box to be focused, as opposed to whether Unity currently agrees.
        ///
        /// The gap between those two is the bug this control exists to close.
        /// </summary>
        private bool wantFocus;

        public UITextBoxControl()
        {
            controlName = "Gideon_UITextBox_" + instanceCounter++;
        }

        /// <summary>Never null. Assigning does not raise a change, since the caller already knows.</summary>
        public string Text
        {
            get => text;
            set => text = value ?? "";
        }

        public bool IsEmpty => text.NullOrEmpty();

        /// <summary>Drawn in place of the text when the box is empty and unfocused. Null draws nothing.</summary>
        public string Placeholder;

        /// <summary>Longest text accepted. Vanilla's search widget uses 30; this is for prose as well.</summary>
        public int MaxLength = 60;

        /// <summary>
        /// Whether the box accepts more than one line.
        ///
        /// <b>Why this belongs here rather than in a second control.</b> Everything that makes this class worth
        /// having is about focus, not about line count: the id-shift repair, the tracked reference behind
        /// <see cref="AnyFocused"/>, and the camera gate that reads it. A separate multi-line box would have to
        /// carry all of that again, and the day the two copies drifted apart would be the day typing into one of
        /// them started driving the map.
        ///
        /// What changes is small: a text area instead of a field, the text sitting at the top of the box rather
        /// than centred in it, and no clear button or icon -- both are single-line furniture that would sit
        /// oddly beside a paragraph, and a multi-line box is nearly always tall enough that a stray click on a
        /// corner button is a real risk.
        ///
        /// <b>Enter inserts a newline rather than committing.</b> That is what a text area is for, and it means
        /// a caller wanting Enter to mean "go" must use a single-line box or provide a button. The workbench
        /// does the latter.
        /// </summary>
        public bool Multiline;

        /// <summary>Drawn at the left, inside the box. <c>TexButton.Search</c> makes it a search field.</summary>
        public Texture2D Icon;

        /// <summary>A small x at the right, once there is something to clear.</summary>
        public bool ShowClearButton = true;

        /// <summary>Whether this box believes it holds keyboard focus.</summary>
        public bool Focused => wantFocus;

        /// <summary>
        /// Whether any text box of ours holds focus, for the key-binding gate.
        ///
        /// Read from <c>CameraDriver.Update</c> by way of the patched gate, which runs outside OnGUI where
        /// <c>GUI.GetNameOfFocusedControl</c> is not dependable -- hence the tracked reference rather than a
        /// live query.
        ///
        /// The frame tolerance covers the ordering between Unity's Update and OnGUI, which run in that order
        /// for a given frame: at Update time the last draw is always one frame behind. Two frames of slack
        /// costs a few tens of milliseconds of suppressed keys after a window closes, which no player can
        /// perceive, and buys a gate that cannot get stuck shut.
        /// </summary>
        public static bool AnyFocused => focusedBox != null && Time.frameCount - focusedFrame <= 2;

        /// <summary>Asks for focus. Takes effect on the next draw.</summary>
        public void Focus()
        {
            wantFocus = true;
            GUI.FocusControl(controlName);
        }

        /// <summary>Gives up focus, if this box had it.</summary>
        public void Unfocus()
        {
            wantFocus = false;

            if (GUI.GetNameOfFocusedControl() == controlName)
                UI.UnfocusCurrentControl();

            if (focusedBox == this)
                focusedBox = null;
        }

        /// <summary>Empties the box without disturbing focus, so typing can continue.</summary>
        public void Clear()
        {
            text = "";
        }

        /// <summary>
        /// Case-insensitive substring match, for the common case of a box that filters a list.
        ///
        /// An empty box matches everything, so a caller can pass every candidate through this without first
        /// testing whether the filter is active.
        /// </summary>
        public bool Matches(string candidate)
        {
            return IsEmpty || (!candidate.NullOrEmpty()
                               && candidate.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// Draws the box and reports whether the text changed this frame.
        ///
        /// The order inside is deliberate. Focus is resolved before the field is drawn, because a repair has to
        /// be in place by the time Unity allocates the field's id. The clear button is drawn after the field
        /// for the same reason in reverse: a control that comes and goes ahead of the field is exactly what
        /// shifts its id, which is the fault this control is here to survive rather than to cause.
        /// </summary>
        public bool Draw(Rect rect, UIColorPaletteDef palette = null)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            ResolveFocus(rect);

            Widgets.DrawBoxSolid(rect, palette.SurfaceSunken);

            GameFont previousFont = Text_Font;
            TextAnchor previousAnchor = Verse.Text.Anchor;
            Color previousColor = GUI.color;

            GUI.color = wantFocus ? palette.BorderFocused : palette.Border;
            Widgets.DrawBox(rect, 1);
            GUI.color = previousColor;

            Rect inner = rect.ContractedBy(EdgePad);

            // Both are single-line furniture. See the note on Multiline.
            bool showIcon = Icon != null && !Multiline;
            bool showClear = ShowClearButton && !Multiline;

            if (showIcon)
            {
                float size = Mathf.Min(IconSize, inner.height);

                GUI.color = palette.TextSecondary;
                GUI.DrawTexture(new Rect(inner.x, inner.y + (inner.height - size) * 0.5f, size, size), Icon);
                GUI.color = previousColor;

                inner.xMin += size + EdgePad;
            }

            // The clear button's lane is reserved whether or not it is drawn, so the text does not reflow the
            // moment the first character arrives.
            if (showClear)
                inner.xMax -= IconSize + EdgePad;

            Verse.Text.Anchor = Multiline ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;

            string before = text;

            // Gated on the launch-latched flag, never on UIDebug.Enabled directly: this allocates a control id,
            // and an id that appears or disappears mid-session is itself a draw-order shift -- the exact fault
            // being investigated. It must not be possible to cause it by flipping a setting. It is also not
            // behind the armed check, so the id stays put after the probe has reported and stopped.
            int sentinel = UIDebug.InstrumentControlIds ? GUIUtility.GetControlID(FocusType.Passive) : -1;

            // Vanilla's field style, so the caret and selection look like the rest of the game's; only the
            // color is ours. A themed caret would mean reimplementing text editing, which is not worth it.
            GUI.SetNextControlName(controlName);
            GUI.color = palette.TextPrimary;

            // The shared style carries a fill and a border for every text field in the game, which is what
            // vanilla's own fields rely on to have an edge at all. This control already drew its frame, around
            // a rect wider than the editable area so it encloses the icon and the clear button -- so the style
            // is asked to stand down for the length of this one call, or the field would sit in a second border
            // inside the first. try/finally because leaving it off would silently un-border the whole game.
            string edited;

            UISkinRestyler.SetFieldChrome(false);
            try
            {
                // GUI.TextArea rather than Widgets.TextArea, because vanilla's wrapper takes no length limit and
                // this control promises one. The style is vanilla's own either way, so the caret and the
                // selection match every other field in the game.
                edited = Multiline
                    ? GUI.TextArea(inner, text, MaxLength, Verse.Text.CurTextAreaStyle)
                    : Widgets.TextField(inner, text, MaxLength);
            }
            finally
            {
                UISkinRestyler.SetFieldChrome(true);
            }

            GUI.color = previousColor;

            bool changed = edited != text;
            if (changed)
                text = edited;

            Diagnose(sentinel);   // TEMPORARY

            // After the field, so it draws over nothing, and only when the field is not showing text of its
            // own -- a placeholder under real text would just be two strings on top of each other.
            if (before.NullOrEmpty() && !Placeholder.NullOrEmpty() && !wantFocus)
            {
                GUI.color = palette.TextDisabled;
                Widgets.Label(inner, Placeholder);
                GUI.color = previousColor;
            }

            Verse.Text.Anchor = previousAnchor;
            Text_Font = previousFont;

            if (showClear && !text.NullOrEmpty())
            {
                Rect clear = new Rect(inner.xMax + EdgePad, rect.y + (rect.height - IconSize) * 0.5f,
                    IconSize, IconSize);

                if (Widgets.ButtonImage(clear, TexButton.CloseXSmall, palette.TextSecondary,
                        palette.TextPrimary))
                {
                    Clear();
                    changed = true;

                    // Focus is kept rather than dropped: clearing is usually the start of a new search, not
                    // the end of one.
                    Focus();
                }
            }

            return changed;
        }

        /// <summary>
        /// Reconciles what the player means with what Unity thinks, once per draw.
        ///
        /// Four cases, and the third is the whole point of this control:
        ///
        /// Escape and a click outside both mean "done", and are handled first so a blur beats a repair in the
        /// same frame. The click test uses <c>OriginalEventUtility.EventType</c> rather than
        /// <c>Event.current.type</c> because by the time this runs some other control may already have
        /// consumed the event; the original type is what vanilla's own widget consults for this.
        ///
        /// If Unity agrees this box is focused, that is recorded and nothing else happens -- including on the
        /// frame the player first clicks in, since <c>GUI.TextField</c> takes focus by itself.
        ///
        /// If this box believes it is focused and <i>nothing</i> is, focus was lost rather than moved: that is
        /// the id shift, and it is repaired by name.
        ///
        /// If some other named control holds focus, the player moved on and this box concedes. Without this
        /// case a repair would fight every other text field in the game for focus, every frame.
        /// </summary>
        private void ResolveFocus(Rect rect)
        {
            if (wantFocus && Event.current.type == EventType.KeyDown
                          && Event.current.keyCode == KeyCode.Escape)
            {
                Unfocus();
                Event.current.Use();
                return;
            }

            if (OriginalEventUtility.EventType == EventType.MouseDown
                && !rect.Contains(Event.current.mousePosition))
            {
                Unfocus();
                return;
            }

            string current = GUI.GetNameOfFocusedControl();

            if (current == controlName)
            {
                wantFocus = true;
            }
            else if (wantFocus && current.NullOrEmpty())
            {
                GUI.FocusControl(controlName);
                repaired = true;
            }
            else if (wantFocus)
            {
                wantFocus = false;
                conceded = true;
                concededTo = current;
            }

            if (wantFocus)
            {
                focusedBox = this;
                focusedFrame = Time.frameCount;
            }
            else if (focusedBox == this)
            {
                focusedBox = null;
            }
        }

        // -----------------------------------------------------------------------------------------------
        // Focus diagnostics
        //
        // Answers one question when focus goes wrong: had the text field's control id changed, and if so, were
        // the added ids allocated by the consumer or by something upstream of it?
        //
        // IMGUI derives a control's id from draw order, so if the number of ids allocated ahead of the field
        // changes between frames, the field's own id changes, keyboardControl is left pointing at the old one,
        // and focus is silently gone. Reading the draw order by hand does not reveal this -- the allocation
        // happens inside Unity, where a mod cannot see it -- which is why the answer has to be measured.
        //
        // The measurement is the sentinel id allocated immediately before the field in Draw. Its value *is* the
        // count of ids allocated ahead of the field in that pass, which is exactly the quantity in question. The
        // field's own id is not directly readable; this stands in for it exactly. DiagnosticUpstreamSentinel is
        // the same trick at the start of the consumer's drawing, which is what attributes the change.
        //
        // Three triggers, covering every way this control can end up not focused when it thought it was:
        //
        //   - repaired, sentinel moved  -> a draw-order shift, attributed upstream or to the consumer.
        //   - repaired, sentinel held   -> focus was lost for some other reason entirely.
        //   - conceded                  -> another named control took focus. Silent otherwise, and the one case
        //                                  the repair deliberately does not fight.
        //
        // Firing on those events rather than sampling continuously means it reports on exactly the frame the
        // fault bit, and cannot burn its one shot on benign startup churn before anyone has typed.
        //
        // Kept rather than removed, because the fault is intermittent and this is the only thing that can
        // explain it when it recurs. It costs nothing when switched off: see UIDebug, and note that the sentinel
        // follows the launch-latched flag rather than the live one, so toggling the setting cannot shift ids.
        // -----------------------------------------------------------------------------------------------

        /// <summary>Disarms after one report, so a per-frame fault cannot flood the log.</summary>
        private static bool diagnosticsArmed = true;

        /// <summary>Optional extra detail from the consumer, e.g. a row count that might explain a shift.</summary>
        public static Func<string> DiagnosticContext;

        /// <summary>
        /// A control id the consumer allocates at the very start of its own drawing, for attribution.
        ///
        /// The first run of these diagnostics proved the field's id moves, but not <i>whose</i> ids moved. This
        /// is the discriminator, and it works by comparing two deltas rather than one:
        ///
        ///   - upstream moved by the same amount as the field -> every added id was allocated before the
        ///     consumer's drawing began. Nothing the consumer can reorder; the by-name repair is the only fix.
        ///   - upstream held while the field moved -> the ids were allocated <i>inside</i> the consumer, between
        ///     the start of its drawing and this field, and there is a real draw-order bug to find.
        ///
        /// The consumer must allocate it unconditionally, for the same reason the field's own sentinel is
        /// unconditional: an id allocated only sometimes is itself a draw-order shift.
        /// </summary>
        public static int DiagnosticUpstreamSentinel = -1;

        private bool repaired;

        /// <summary>
        /// Set when another named control took focus, which is the path that was previously silent: the repair
        /// deliberately concedes there, so nothing was reported and the box would simply stop accepting input.
        /// </summary>
        private bool conceded;

        private string concededTo;

        private int lastSentinel = -1;

        private int lastUpstream = -1;

        /// <summary>
        /// The previous sample's context.
        ///
        /// The first run logged the context only for the frame the repair happened on, which left the earlier
        /// frame's state unknown -- and with it whether something like a row count had changed between the two.
        /// Both are recorded now so the comparison is actually readable.
        /// </summary>
        private string lastContext;

        private void Diagnose(int sentinel)
        {
            if (!diagnosticsArmed || !UIDebug.InstrumentControlIds)
                return;

            // One event type only. Ids are allocated per pass, and Layout/Repaint/key passes are not required
            // to agree with each other -- comparing across pass types would report shifts that are not real.
            // Repaint is used because it happens every frame regardless of input.
            if (Event.current.type != EventType.Repaint)
                return;

            // Consumed here rather than at the top of the method, because these are usually detected on a key
            // pass: clearing the flags on every pass would throw them away before this Repaint could read them.
            // Left set across passes, they mean "this happened since the last sample", which is the question.
            bool wasRepaired = repaired;
            bool wasConceded = conceded;
            repaired = false;
            conceded = false;

            int upstream = DiagnosticUpstreamSentinel;
            string context = DiagnosticContext != null ? DiagnosticContext() : "none";

            if ((wasRepaired || wasConceded) && lastSentinel >= 0)
            {
                int fieldDelta = sentinel - lastSentinel;
                int upstreamDelta = upstream >= 0 && lastUpstream >= 0 ? upstream - lastUpstream : 0;

                // The attribution, stated in the log rather than left to be worked out later.
                string verdict;
                if (wasConceded)
                    verdict = $"CONCEDED -- focus was taken by '{concededTo}', so this box stopped accepting "
                              + "input rather than repairing. The repair does not fight another named control "
                              + "on purpose; if that control is not one the player clicked, the concede rule is "
                              + "too permissive";
                else if (fieldDelta == 0)
                    verdict = "REFUTED -- the field's id held steady, so a draw-order shift is not the cause";
                else if (upstream < 0 || lastUpstream < 0)
                    verdict = "CONFIRMED, UNATTRIBUTED -- no upstream sentinel was supplied by the consumer";
                else if (upstreamDelta == fieldDelta)
                    verdict = "CONFIRMED, UPSTREAM -- the ids were allocated before the consumer began drawing, "
                              + "so there is nothing in our own draw order to reorder and the by-name repair is "
                              + "the fix";
                else if (upstreamDelta == 0)
                    verdict = "CONFIRMED, INSIDE THE CONSUMER -- the ids were allocated between the start of the "
                              + "consumer's drawing and this field, so there is a real draw-order bug to find";
                else
                    verdict = "CONFIRMED, SPLIT -- ids moved both upstream and inside the consumer";

                UIDebug.Warning($"Focus diagnostics: {verdict}.\n"
                                + $"  field '{controlName}': {lastSentinel} -> {sentinel} (delta {fieldDelta})\n"
                                + $"  upstream (consumer start): {lastUpstream} -> {upstream} "
                                + $"(delta {upstreamDelta})\n"
                                + $"  keyboardControl={GUIUtility.keyboardControl}\n"
                                + $"  context before: {lastContext ?? "none"}\n"
                                + $"  context now:    {context}\n"
                                + "  Monitoring is now off until the next launch.");

                diagnosticsArmed = false;
                return;
            }

            lastSentinel = sentinel;
            lastUpstream = upstream;
            lastContext = context;
        }

        /// <summary>
        /// Shorthand for the ambiguous <c>Text</c>: this type has a <see cref="Text"/> property of its own, so
        /// <c>Verse.Text.Font</c> cannot be written unqualified inside it.
        /// </summary>
        private static GameFont Text_Font
        {
            get => Verse.Text.Font;
            set => Verse.Text.Font = value;
        }
    }
}
