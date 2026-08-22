using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// The training request checkboxes, drawn the same way for one animal, for a whole species, and in the card.
    ///
    /// <b>The pill is the request, not the achievement.</b> That is vanilla's model and worth keeping: ticking
    /// Guard asks the handlers to teach it, and the animal learns over days. So a lit pill says what has been
    /// asked for, and the underline along its bottom rim says how far the learning has got. One control therefore
    /// answers both questions a player has about a skill.
    ///
    /// <b>A pill rather than a switch with a glyph on top of it.</b> Until 2026-08-22 this painted
    /// <see cref="UICheckboxControl.DrawBox"/> and drew the icon over it. A switch is twice as wide as it is tall,
    /// so its track came out 14 pixels high inside a 28 pixel cell while the glyph wanted 20: the mark hung over
    /// both edges of the control it belonged to. A pill spends its whole area on the thing being identified, which
    /// is what a glyph needs, and dim against lit reads faster at this size than a knob that has slid four pixels.
    ///
    /// <b>Glyph inside the pill rather than a name beside it.</b> Asked for on 2026-08-22 for the species row and
    /// the card, and the row has one column's width for the whole set. A control with its name beside it costs
    /// about 90px per skill; this costs 34px, which is what makes seven fit a column that also carries a decay
    /// warning. The skill's name, its progress and what a click will do are in the tooltip, which is where a grid
    /// of small controls has to put them.
    ///
    /// <b>Recursive writes, through vanilla.</b> Asking for Rescue asks for Guard and Tameness underneath it, and
    /// clearing Guard clears what depends on it. <see cref="AnimalTraining.SetWanted"/> goes through the game's own
    /// recursive setter for that reason, so a box here behaves exactly as the same box does in vanilla's tab.
    ///
    /// <b>Tameness is never offered.</b> It is not a request, it is the state of being tame, and a checkbox that
    /// could untame an animal by accident has no business on a row this small.
    /// </summary>
    internal static class AnimalTrainingBoxes
    {
        /// <summary>
        /// How tall a pill is.
        ///
        /// Raised from 22 to 28 on Aaron's report of 2026-08-22, and kept there when the switch became a pill
        /// later the same day: at 22 the glyph inside was 16 pixels and a crosshair, a heart and a spade were
        /// three grey smudges. The glyph is the thing being read, so it is worth the column width.
        /// </summary>
        internal const float PillHeight = 28f;

        /// <summary>
        /// How wide a pill is.
        ///
        /// Wider than tall, which is what makes it read as a pill rather than as a checkbox. The extra width is
        /// air either side of the glyph rather than a bigger glyph: a mark that touches the rim looks cramped
        /// however large it is.
        /// </summary>
        internal const float PillWidth = 30f;

        private const float PillGap = 4f;

        /// <summary>Space between the pill's rim and the glyph inside it.</summary>
        private const float IconInset = 3f;

        /// <summary>How tall the progress underline is, and how far off the bottom rim it sits.</summary>
        private const float BarHeight = 2f;

        private const float BarInset = 2f;

        /// <summary>Scratch for the kinds a species can take. Rebuilt per call; the UI is single threaded.</summary>
        private static readonly List<TrainableDef> Kinds = new List<TrainableDef>();

        /// <summary>How wide a set of boxes will be, so a caller can lay out around it.</summary>
        internal static float WidthFor(int kinds)
        {
            return kinds <= 0 ? 0f : kinds * PillWidth + (kinds - 1) * PillGap;
        }

        /// <summary>
        /// Whether this skill is worth offering on this animal at all.
        ///
        /// <b>Vanilla's own two part answer, and both parts matter.</b> <c>CanAssignToTrain</c> reports
        /// <c>visible</c> false when the species has no business with the skill: an untrainable tag matches, or it
        /// is one of the special trainables and this race does not list it, or it is not a default trainable. It
        /// reports accepted false when the species could in principle but does not qualify: too small, or not
        /// smart enough. Vanilla's own column draws a checkbox only when both are true, and so does this.
        ///
        /// <b>This replaces <c>CanBeTrained</c>, which was the wrong question and produced the fault Aaron
        /// screenshotted on 2026-08-22:</b> a goat with fifteen boxes, every one of them reading "cannot learn",
        /// including several from other mods. <c>CanBeTrained</c> answers "is there training left to do", so it
        /// says yes to every skill the animal has not finished, whether or not it could ever start. It also says
        /// <i>no</i> to a skill that is fully learned, which would have made a trained husky's Guard box disappear
        /// exactly when it was worth seeing.
        ///
        /// A learned skill stays eligible for that reason, whatever the report says today.
        /// </summary>
        private static bool Eligible(Pawn animal, TrainableDef kind)
        {
            if (animal?.training == null || kind == null)
                return false;

            if (animal.training.HasLearned(kind))
                return true;

            bool visible;
            AcceptanceReport report = animal.training.CanAssignToTrain(kind, out visible);

            return visible && report.Accepted;
        }

        /// <summary>
        /// The kinds worth drawing for one animal, in the game's own list order.
        ///
        /// The returned list is scratch and is valid until the next call.
        /// </summary>
        internal static List<TrainableDef> KindsFor(Pawn animal)
        {
            Kinds.Clear();

            List<TrainableDef> all = TrainableUtility.TrainableDefsInListOrder;

            if (animal?.training == null || all == null)
                return Kinds;

            for (int i = 0; i < all.Count; i++)
            {
                TrainableDef kind = all[i];

                // Tameness is never offered: it is not a request, it is the state of being tame, and a checkbox
                // that could untame an animal by accident has no business on a row this small.
                if (kind == TrainableDefOf.Tameness)
                    continue;

                if (Eligible(animal, kind))
                    Kinds.Add(kind);
            }

            return Kinds;
        }

        /// <summary>
        /// The kinds worth drawing for a whole species: anything at least one member can take.
        ///
        /// <b>A union rather than the first member's answer.</b> Body size is part of eligibility, so a herd whose
        /// first member happens to be a juvenile would otherwise lose the boxes its adults qualify for, and which
        /// animal comes first is decided by alphabetical order.
        ///
        /// The returned list is scratch and is valid until the next call.
        /// </summary>
        internal static List<TrainableDef> KindsFor(AnimalGroup group)
        {
            Kinds.Clear();

            List<TrainableDef> all = TrainableUtility.TrainableDefsInListOrder;

            if (group == null || group.Count == 0 || all == null)
                return Kinds;

            for (int i = 0; i < all.Count; i++)
            {
                TrainableDef kind = all[i];

                if (kind == TrainableDefOf.Tameness)
                    continue;

                for (int m = 0; m < group.Members.Count; m++)
                {
                    if (!Eligible(group.Members[m], kind))
                        continue;

                    Kinds.Add(kind);

                    break;
                }
            }

            return Kinds;
        }

        /// <summary>
        /// Whether losing one more step would make this animal forget this skill.
        ///
        /// <b>Read from the steps rather than from the decay walk,</b> which matters because this is asked per box
        /// per frame. Vanilla only clears a learned skill when its steps reach zero, so a learned skill standing at
        /// one step is one loss from gone, and that is true whether or not the clock happens to be running. The
        /// authoritative countdown, which needs the clock, is on the species row's caption and is computed once per
        /// rebuild rather than once per box.
        /// </summary>
        private static bool OneStepFromLost(Pawn animal, TrainableDef kind)
        {
            return AnimalTraining.Learned(animal, kind) && AnimalTraining.StepsOf(animal, kind) <= 1;
        }

        /// <summary>
        /// One animal's boxes.
        ///
        /// Returns the width used, so a caller drawing a line of them can put something after it.
        /// </summary>
        internal static float DrawForAnimal(Rect rect, Pawn animal, UIColorPaletteDef palette, Action changed)
        {
            List<TrainableDef> kinds = KindsFor(animal);

            // Copied, because the reads below use the same scratch list through AnimalTraining.
            TrainableDef[] set = kinds.ToArray();

            float x = rect.x;

            for (int i = 0; i < set.Length; i++)
            {
                TrainableDef kind = set[i];
                Rect box = new Rect(x, rect.center.y - PillHeight / 2f, PillWidth, PillHeight);

                if (box.xMax > rect.xMax)
                    break;

                AcceptanceReport can = AnimalTraining.CanAsk(animal, kind);
                bool wanted = AnimalTraining.Wanted(animal, kind);
                bool learned = AnimalTraining.Learned(animal, kind);
                bool risk = OneStepFromLost(animal, kind);

                Draw(box, kind, wanted ? MultiCheckboxState.On : MultiCheckboxState.Off,
                    learned ? 1f : Progress(animal, kind), risk, !can.Accepted, palette);

                if (Mouse.IsOver(box))
                    TooltipHandler.TipRegion(box, (TipSignal) Tip(animal, kind, can, learned, risk));

                if (can.Accepted && Widgets.ButtonInvisible(box))
                {
                    AnimalTraining.SetWanted(animal, kind, !wanted);

                    changed?.Invoke();

                    SoundDefOf.Click.PlayOneShotOnCamera();
                }

                x = box.xMax + PillGap;
            }

            return Mathf.Max(0f, x - rect.x);
        }

        /// <summary>
        /// A whole species' boxes, where one click applies to every animal in it.
        ///
        /// <b>Tri-state, because a group can disagree with itself.</b> A partial box says some of these are asked
        /// for and some are not, which is a thing the player needs to know before clicking: the click resolves the
        /// disagreement upwards, asking for it on everybody, and only a fully ticked box clears.
        ///
        /// Animals that cannot take the skill are skipped rather than counted, so a herd with two juveniles does
        /// not read as permanently partial.
        /// </summary>
        internal static float DrawForGroup(Rect rect, AnimalGroup group, UIColorPaletteDef palette, Action changed)
        {
            if (group == null || group.Count == 0)
                return 0f;

            List<TrainableDef> kinds = KindsFor(group);

            // Copied out, because the shared scratch list is reused by the per animal reads below.
            TrainableDef[] set = kinds.ToArray();

            float x = rect.x;

            for (int i = 0; i < set.Length; i++)
            {
                Rect box = new Rect(x, rect.center.y - PillHeight / 2f, PillWidth, PillHeight);

                if (box.xMax > rect.xMax)
                    break;

                Box(box, group, set[i], palette, changed);

                x = box.xMax + PillGap;
            }

            return Mathf.Max(0f, x - rect.x);
        }

        /// <summary>
        /// One skill as a labelled row: the box, the skill's name, and how many have learned it.
        ///
        /// For the card, where there is room for the name and the count that the row's tooltip has to carry.
        /// </summary>
        internal static void DrawForKind(Rect rect, AnimalGroup group, TrainableDef kind,
            UIColorPaletteDef palette, Action changed)
        {
            if (group == null || group.Count == 0 || kind == null)
                return;

            Tally tally = Count(group, kind);

            Rect box = new Rect(rect.x, rect.center.y - PillHeight / 2f, PillWidth, PillHeight);

            Box(box, group, kind, palette, changed);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = tally.Eligible == 0 ? palette.TextDisabled : palette.TextSecondary;

                Widgets.LabelEllipses(new Rect(box.xMax + 8f, rect.y, rect.width * 0.5f, rect.height),
                    kind.LabelCap);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = tally.Learned >= tally.Eligible && tally.Eligible > 0
                    ? palette.Success
                    : palette.TextDisabled;

                Widgets.Label(new Rect(rect.x + rect.width * 0.55f, rect.y, rect.width * 0.45f, rect.height),
                    tally.Eligible == 0
                        ? "cannot learn"
                        : tally.Learned + " of " + tally.Eligible + " trained");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// One skill as a labelled row for one animal: the pill, the skill's name, and how far along it is.
        ///
        /// The individual's version of <see cref="DrawForKind"/>. Where the species row can only say how many of
        /// the herd have learned something, this says which step this animal is on, which is the thing somebody
        /// looking at one animal opened the card for.
        /// </summary>
        internal static void DrawForAnimalKind(Rect rect, Pawn animal, TrainableDef kind,
            UIColorPaletteDef palette, Action changed)
        {
            if (animal?.training == null || kind == null)
                return;

            Rect pill = new Rect(rect.x, rect.center.y - PillHeight / 2f, PillWidth, PillHeight);

            AcceptanceReport can = AnimalTraining.CanAsk(animal, kind);
            bool wanted = AnimalTraining.Wanted(animal, kind);
            bool learned = AnimalTraining.Learned(animal, kind);
            bool risk = OneStepFromLost(animal, kind);

            Draw(pill, kind, wanted ? MultiCheckboxState.On : MultiCheckboxState.Off,
                learned ? 1f : Progress(animal, kind), risk, !can.Accepted, palette);

            if (Mouse.IsOver(pill))
                TooltipHandler.TipRegion(pill, (TipSignal) Tip(animal, kind, can, learned, risk));

            if (can.Accepted && Widgets.ButtonInvisible(pill))
            {
                AnimalTraining.SetWanted(animal, kind, !wanted);

                changed?.Invoke();

                SoundDefOf.Click.PlayOneShotOnCamera();
            }

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.WordWrap = false;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = can.Accepted || learned ? palette.TextSecondary : palette.TextDisabled;

                Widgets.LabelEllipses(new Rect(pill.xMax + 8f, rect.y, rect.width * 0.45f, rect.height),
                    kind.LabelCap);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                GUI.color = risk ? palette.Danger : learned ? palette.TextPrimary : palette.TextDisabled;

                Widgets.Label(new Rect(rect.x + rect.width * 0.5f, rect.y, rect.width * 0.5f, rect.height),
                    Standing(animal, kind, can, learned, risk));
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// Where this animal has got to on one skill, in a few words.
        ///
        /// The decay warning wins over the step count, because it is the one that wants doing something about.
        /// </summary>
        private static string Standing(Pawn animal, TrainableDef kind, AcceptanceReport can, bool learned,
            bool risk)
        {
            if (!can.Accepted && !learned)
                return "cannot learn";

            if (risk)
                return "about to be lost";

            if (learned)
                return "trained";

            int steps = AnimalTraining.StepsOf(animal, kind);

            return steps <= 0 ? "not started" : steps + " of " + kind.steps + " steps";
        }

        /// <summary>How a species stands on one skill.</summary>
        private struct Tally
        {
            internal int Eligible;
            internal int Wanted;
            internal int Learned;
            internal int Risk;

            internal MultiCheckboxState State => Eligible == 0 || Wanted == 0
                ? MultiCheckboxState.Off
                : Wanted >= Eligible ? MultiCheckboxState.On : MultiCheckboxState.Partial;
        }

        /// <summary>
        /// Counts a species' standing on one skill.
        ///
        /// Animals that can neither take the skill nor have already learned it are skipped rather than counted, so
        /// a herd with two juveniles in it does not read as permanently partial.
        /// </summary>
        private static Tally Count(AnimalGroup group, TrainableDef kind)
        {
            Tally tally = new Tally();

            for (int m = 0; m < group.Members.Count; m++)
            {
                Pawn animal = group.Members[m];
                bool learned = AnimalTraining.Learned(animal, kind);

                if (!Eligible(animal, kind))
                    continue;

                tally.Eligible++;

                if (AnimalTraining.Wanted(animal, kind))
                    tally.Wanted++;

                if (learned)
                    tally.Learned++;

                if (OneStepFromLost(animal, kind))
                    tally.Risk++;
            }

            return tally;
        }

        /// <summary>One species box, shared by the row and the card so the two cannot disagree.</summary>
        private static void Box(Rect box, AnimalGroup group, TrainableDef kind, UIColorPaletteDef palette,
            Action changed)
        {
            Tally tally = Count(group, kind);

            Draw(box, kind, tally.State, tally.Eligible == 0 ? 0f : tally.Learned / (float) tally.Eligible,
                tally.Risk > 0, tally.Eligible == 0, palette);

            if (Mouse.IsOver(box))
                TooltipHandler.TipRegion(box, (TipSignal) GroupTip(kind, tally));

            if (tally.Eligible == 0 || !Widgets.ButtonInvisible(box))
                return;

            bool ask = tally.State != MultiCheckboxState.On;

            for (int m = 0; m < group.Members.Count; m++)
                AnimalTraining.SetWanted(group.Members[m], kind, ask);

            changed?.Invoke();

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// One pill: dim when the skill is not asked for, lit when it is, underlined by how much of it has been
        /// learned.
        ///
        /// <b>The glyph takes theme colors, never a meaning color.</b> It used to be tinted green once learned and
        /// amber part way there, which had the icon carrying two jobs at once: Aaron's read of it on 2026-08-22
        /// was that the mark was fighting the palette rather than saying which skill it was. Now the fill says
        /// what has been asked for, the underline says how far along it is, and the glyph is only ever there to be
        /// recognized.
        ///
        /// Red is the one exception, and it is on the rim rather than the glyph: one step from forgetting is the
        /// only state that wants somebody to do something today, and the rim can shout without making the mark
        /// inside it harder to read.
        /// </summary>
        private static void Draw(Rect pill, TrainableDef kind, MultiCheckboxState state, float progress, bool risk,
            bool disabled, UIColorPaletteDef palette)
        {
            palette = palette ?? UIColorPaletteDef.Active;

            bool lit = !disabled && state == MultiCheckboxState.On;
            bool some = !disabled && state == MultiCheckboxState.Partial;

            // Partial is an outline rather than a half fill: a group that disagrees with itself is "asked for on
            // some of these", and an accent rim over the dim ground says that without inventing a third fill
            // nobody could name.
            Color fill = lit ? palette.Accent : palette.SurfaceSunken;
            Color rim = disabled
                ? Faded(palette.Border)
                : risk
                    ? palette.Danger
                    : lit
                        ? Dimmed(palette.Accent)
                        : some
                            ? palette.Accent
                            : palette.Border;

            UIElementPainter.OutlineRounded(pill, rim, fill);

            if (!disabled && Mouse.IsOver(pill))
                UIElementPainter.FillRounded(pill.ContractedBy(1f), palette.HoverOverlay);

            Color previous = GUI.color;

            // On a lit pill the window's own background is the highest contrast the theme actually contains, which
            // is the reading UITagControl arrived at for the same problem.
            GUI.color = disabled
                ? palette.TextDisabled
                : lit
                    ? palette.WindowBackground
                    : some
                        ? palette.Accent
                        : palette.TextSecondary;

            // The underline's lane is taken off the bottom before the glyph is placed, so a skill part way through
            // its training does not have a bar drawn across its own icon.
            Rect inside = new Rect(pill.x + IconInset, pill.y + IconInset, pill.width - IconInset * 2f,
                pill.height - IconInset * 2f - BarHeight);

            Texture2D icon = IconOf(kind);

            if (icon != null)
            {
                GUI.DrawTexture(inside, icon, ScaleMode.ScaleToFit);
            }
            else
            {
                TextAnchor previousAnchor = Text.Anchor;
                GameFont previousFont = Text.Font;

                Text.Anchor = TextAnchor.MiddleCenter;
                Text.Font = GameFont.Tiny;

                Widgets.Label(inside, Initials(kind));

                Text.Font = previousFont;
                Text.Anchor = previousAnchor;
            }

            GUI.color = previous;

            Bar(pill, progress, lit, risk, disabled, palette);
        }

        /// <summary>
        /// The underline that says how much of this skill has actually been learned: none of it, part of it, or
        /// all of it.
        ///
        /// <b>Nothing is drawn at zero.</b> An empty track under every untrained skill is five pieces of furniture
        /// per row saying nothing, and it would make an untrained pill look like a different control from a
        /// trained one rather than the same one further along.
        /// </summary>
        private static void Bar(Rect pill, float progress, bool lit, bool risk, bool disabled,
            UIColorPaletteDef palette)
        {
            if (disabled || progress <= 0f)
                return;

            float lane = pill.width - IconInset * 2f;
            float width = lane * Mathf.Clamp01(progress);

            if (width < 1f)
                return;

            Rect bar = new Rect(pill.x + IconInset, pill.yMax - BarInset - BarHeight, width, BarHeight);

            Widgets.DrawBoxSolid(bar, risk
                ? palette.Danger
                : lit
                    ? palette.WindowBackground
                    : palette.Accent);
        }

        /// <summary>
        /// How much darker a lit pill's rim is than its fill.
        ///
        /// The same factor the framework's switch uses, so the two are edged by one rule rather than by two
        /// opinions that drift apart when the accent changes.
        /// </summary>
        private const float RimFactor = 0.65f;

        private static Color Dimmed(Color color)
        {
            return new Color(color.r * RimFactor, color.g * RimFactor, color.b * RimFactor, color.a);
        }

        /// <summary>
        /// A rim at half strength, for a pill that cannot be clicked.
        ///
        /// Dimmed rather than dropped: a pill with no rim at all stops reading as a control and starts reading as
        /// a gap in the row.
        /// </summary>
        private static Color Faded(Color color)
        {
            return new Color(color.r, color.g, color.b, color.a * 0.5f);
        }

        /// <summary>
        /// Resolved icons, one entry per trainable.
        ///
        /// <b>Ours rather than the def's own <c>Icon</c> property, and that is a bug fix rather than a
        /// preference.</b> That property calls <c>ContentFinder.Get</c> on its <c>icon</c> field with no null
        /// check, and <b>every one of Odyssey's nine trainables ships without an icon path</b>: comfort, forage,
        /// dig, egg spew and the rest. Asking for their icon threw <c>ArgumentNullException</c> out of a content
        /// dictionary, which took the whole animals tab down to a failure notice the moment a species that can
        /// learn one of them was listed. Reported from Aaron's log on 2026-08-22.
        ///
        /// Resolved with failure reporting off, so a modded trainable pointing at a texture that is not there
        /// falls back to letters instead of writing an error per frame.
        /// </summary>
        private static readonly Dictionary<TrainableDef, Texture2D> Icons =
            new Dictionary<TrainableDef, Texture2D>();

        /// <summary>
        /// The art for one kind: its own if it declares any, ours if it is one of the nine that do not, letters
        /// otherwise.
        ///
        /// <b>The def's own icon wins,</b> so a mod that draws its own trainable keeps it and only the ones with
        /// nothing at all are painted by us. See <see cref="AnimalTrainingIcons"/>.
        /// </summary>
        private static Texture2D IconOf(TrainableDef kind)
        {
            if (kind == null)
                return null;

            Texture2D found;

            if (Icons.TryGetValue(kind, out found))
                return found;

            if (kind.icon.NullOrEmpty())
                found = AnimalTrainingIcons.For(kind);
            else
                found = UIGuard.Try("Animals.TrainableIcon",
                    () => ContentFinder<Texture2D>.Get(kind.icon, false), null, null);

            Icons[kind] = found;

            return found;
        }

        /// <summary>
        /// Two letters standing in for a missing icon.
        ///
        /// <b>A box with nothing in it would be unidentifiable,</b> and with Odyssey that would be most of the
        /// boxes on a thrumbo. Two letters tell "Comfort" from "Carry" at a glance, and the tooltip carries the
        /// full name and description either way.
        /// </summary>
        private static string Initials(TrainableDef kind)
        {
            string label = kind.label.NullOrEmpty() ? kind.defName : kind.label;

            if (label.NullOrEmpty())
                return "?";

            return (label.Length <= 2 ? label : label.Substring(0, 2)).ToUpperInvariant();
        }

        private static float Progress(Pawn animal, TrainableDef kind)
        {
            int steps = AnimalTraining.StepsOf(animal, kind);

            if (steps <= 0 || kind.steps <= 0)
                return 0f;

            return Mathf.Clamp01(steps / (float) kind.steps);
        }

        private static string Tip(Pawn animal, TrainableDef kind, AcceptanceReport can, bool learned, bool risk)
        {
            string text = kind.LabelCap + "\n\n" + kind.description;

            if (!can.Accepted)
                return text + "\n\n" + (can.Reason.NullOrEmpty() ? "Cannot be trained." : can.Reason);

            text += "\n\n" + AnimalTraining.StepsOf(animal, kind) + " of " + kind.steps + " steps";

            if (learned)
                text += ", learned";

            if (risk)
                text += "\n\nOne step from being forgotten.";

            return text;
        }

        private static string GroupTip(TrainableDef kind, Tally tally)
        {
            string text = kind.LabelCap + "\n\n" + kind.description;

            if (tally.Eligible == 0)
                return text + "\n\nNone of these can be trained in it.";

            text += "\n\n" + tally.Learned + " of " + tally.Eligible + " have learned it, " + tally.Wanted
                    + " asked for.";

            if (tally.Risk > 0)
                text += "\n\n" + tally.Risk + " are one step from forgetting it.";

            return text + "\n\nClick to "
                        + (tally.State == MultiCheckboxState.On ? "stop asking for it on" : "ask for it on")
                        + " all " + tally.Eligible + ".";
        }
    }
}
