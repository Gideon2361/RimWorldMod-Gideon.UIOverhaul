using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.GrowZones.UI;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Gideon.UIOverhaul.Features.Animals
{
    /// <summary>
    /// The settings for one taming bill: which animals it wants, how many of each sex, and what it refuses.
    ///
    /// <b>Built to read as the hunting bill's sibling,</b> because they sit in the same list and a player who has
    /// opened one should not have to learn the other. Same two columns, same headings, same footer, same rule that
    /// every control writes straight to the bill and closing is just closing.
    ///
    /// <b>The species list has no add or remove.</b> Every candidate is always listed with two number boxes, and a
    /// species is wanted exactly when one of them is above zero. A separate "add species" step would be a mode to
    /// enter and leave for something the numbers already say, and it is the numbers a player comes here to change.
    /// The bill's target list is created and dropped underneath to match, so the saved data stays as small as it
    /// was.
    ///
    /// <b>Two boxes per species rather than one headcount,</b> which is the whole point of the model: six muffalo
    /// is satisfied by six males, and a bill that satisfies itself that way has done nothing anybody wanted. See
    /// <see cref="TamingTarget"/>.
    /// </summary>
    internal class Dialog_TamingBill : Window
    {
        private static readonly UITextBoxControl Name = new UITextBoxControl
        {
            Placeholder = "Name, optional",
            MaxLength = 40
        };

        private static readonly UITextBoxControl SpeciesSearch = new UITextBoxControl
        {
            Placeholder = "Search species",
            Icon = TexButton.Search,
            MaxLength = 30
        };

        /// <summary>
        /// One number box per species and sex, made on demand.
        ///
        /// <b>Keyed on the species rather than on the row's position,</b> which is what keeps a caret where the
        /// player put it. The list re-sorts as the search box is typed into and scrolls independently, so an
        /// index-keyed pool would hand the box you are typing in to a different animal the moment the list moved.
        /// Text input has to be <see cref="UITextBoxControl"/> here as everywhere: anything else lets the camera
        /// read the keystrokes as hotkeys while you type a number.
        /// </summary>
        private static readonly Dictionary<string, UITextBoxControl> Boxes =
            new Dictionary<string, UITextBoxControl>();

        private readonly TamingBill bill;
        private readonly Map map;

        private Vector2 speciesScroll;
        private Vector2 tamerScroll;

        internal Dialog_TamingBill(TamingBill bill, Map map)
        {
            this.bill = bill;
            this.map = map;

            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            closeOnAccept = false;
            closeOnCancel = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(720f, 660f);

        public override void PostOpen()
        {
            base.PostOpen();

            Seed();
        }

        /// <summary>
        /// Fills the shared boxes from the bill.
        ///
        /// Separate from PostOpen so a template load can reseed without going through it: Window.PostOpen replays
        /// the window appear sound, and a template landing in an already open window is not the window appearing.
        /// </summary>
        private void Seed()
        {
            if (bill == null)
                return;

            Name.Text = bill.label ?? string.Empty;

            SpeciesSearch.Clear();

            // Dropped rather than reseeded one by one. The boxes are static and so survive the window closing,
            // and a box still holding the last bill's number would write that number into this bill the first
            // time it is touched.
            Boxes.Clear();
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Animals.TamingDialog", inRect, () => Contents(inRect),
                "This window failed to draw. The bill is unchanged and can be suspended from the animals tab.");
        }

        /// <summary>The lane on a species row that says how many the colony already has.</summary>
        private const float HeldWidth = 54f;

        private const float BoxWidth = 42f;
        private const float ColumnGap = 16f;
        private const float FooterHeight = 36f;
        private const float RowHeight = 26f;

        private void Contents(Rect inRect)
        {
            if (bill == null || map == null)
                return;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, 32f), "Taming bill");

                // Back to the body font immediately. The title is the only Medium thing in this window, and a
                // control that does not set its own font inherits whatever is left set: the hunting dialog shipped
                // with a checkbox half again the size of its neighbors for exactly this reason.
                Text.Font = GameFont.Small;

                Rect body = new Rect(inRect.x, inRect.y + 38f, inRect.width,
                    Mathf.Max(0f, inRect.height - 38f - FooterHeight));

                float left = Mathf.Round((body.width - ColumnGap) * 0.46f);

                Order(new Rect(body.x, body.y, left, body.height), palette);

                Wants(new Rect(body.x + left + ColumnGap, body.y, body.width - left - ColumnGap, body.height),
                    palette);

                Footer(new Rect(inRect.x, inRect.yMax - FooterHeight + 4f, inRect.width, FooterHeight - 4f),
                    palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        // ---------------------------------------------------------------------------------------
        // Left column: the order
        // ---------------------------------------------------------------------------------------

        private void Order(Rect rect, UIColorPaletteDef palette)
        {
            float y = rect.y;

            y = Heading(rect, y, "THE ORDER", palette);

            y = Field(rect, y, "Name", Name, palette,
                () => bill.label = Name.Text.NullOrEmpty() ? null : Name.Text.Trim(),
                rect.width - 58f, GameFont.Tiny);

            y = Note(rect, y, "Counts the tame animals you already have, calves and chicks included, and orders "
                              + "taming until the numbers on the right are met.", palette);

            y = Heading(rect, y, "LIMITS", palette);

            Text.Font = GameFont.Small;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
                bill.minTameChance <= 0f
                    ? "Any taming chance allowed"
                    : "Skip under " + bill.minTameChance.ToStringPercent() + " taming chance");

            y += 24f;

            float chance = Widgets.HorizontalSlider(new Rect(rect.x, y, rect.width - 6f, 22f),
                bill.minTameChance, 0f, 1f, false, null, null, null, 0.05f);

            if (!Mathf.Approximately(chance, bill.minTameChance))
                bill.minTameChance = chance;

            y += 28f;

            Text.Font = GameFont.Small;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
                bill.maxOutstanding == 1
                    ? "At most one taming at once"
                    : "At most " + bill.maxOutstanding + " tamings at once");

            y += 24f;

            float outstanding = Widgets.HorizontalSlider(new Rect(rect.x, y, rect.width - 6f, 22f),
                bill.maxOutstanding, 1f, 20f, false, null, null, null, 1f);

            bill.maxOutstanding = Mathf.RoundToInt(outstanding);

            y += 30f;

            Tamer(new Rect(rect.x, y, rect.width, Mathf.Max(80f, rect.yMax - y)), palette);
        }

        /// <summary>
        /// Who the bill plans around, as a short list rather than a menu.
        ///
        /// <b>It decides refusals, not who walks out.</b> The minimum chance above is measured against this pawn,
        /// so a bill planned around a novice refuses animals a bill planned around the colony's handler would
        /// take. Which colonist actually does the taming is RimWorld's work priorities, and this does not touch
        /// them. The note says so, because "assign a tamer" reads like a reservation and is not one.
        /// </summary>
        private void Tamer(Rect rect, UIColorPaletteDef palette)
        {
            float y = Heading(rect, rect.y, "PLANNED FOR", palette);

            y = Note(rect, y, "Sets what counts as a good enough chance. It does not reserve the work.", palette);

            List<Pawn> handlers = Handlers();

            Rect list = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            Rect view = new Rect(0f, 0f, list.width - 18f, (handlers.Count + 1) * RowHeight + 4f);

            Widgets.BeginScrollView(list, ref tamerScroll, view);

            float at = 0f;

            if (Choice(new Rect(0f, at, view.width, 24f), "Best available", bill.tamer == null, palette))
                bill.tamer = null;

            at += RowHeight;

            for (int i = 0; i < handlers.Count; i++)
            {
                Pawn handler = handlers[i];

                if (Choice(new Rect(0f, at, view.width, 24f), handler.LabelShortCap, bill.tamer == handler,
                        palette))
                    bill.tamer = handler;

                at += RowHeight;
            }

            Widgets.EndScrollView();
        }

        private bool Choice(Rect rect, string label, bool on, UIColorPaletteDef palette)
        {
            bool picked = UIRadioButtonControl.Draw(rect, on, palette, label);

            return picked && !on;
        }

        /// <summary>Colonists on this map who can do handling work, which is who a taming chance means anything for.</summary>
        private List<Pawn> Handlers()
        {
            return UIGuard.Try("Animals.TamerList", () =>
            {
                List<Pawn> found = new List<Pawn>();

                if (map == null || map.mapPawns == null)
                    return found;

                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;

                for (int i = 0; colonists != null && i < colonists.Count; i++)
                {
                    Pawn colonist = colonists[i];

                    if (colonist == null || colonist.Dead)
                        continue;

                    if (colonist.workSettings == null
                        || !colonist.workSettings.WorkIsActive(WorkTypeDefOf.Handling))
                        continue;

                    found.Add(colonist);
                }

                // The bill's own tamer stays listed even after they are drafted off handling or leave the map, so
                // the choice on screen is the choice that is saved rather than silently reading as "best
                // available".
                if (bill.tamer != null && !found.Contains(bill.tamer))
                    found.Add(bill.tamer);

                found.SortBy(pawn => pawn.LabelShortCap);

                return found;
            }, new List<Pawn>(), null);
        }

        // ---------------------------------------------------------------------------------------
        // Right column: what it wants
        // ---------------------------------------------------------------------------------------

        private void Wants(Rect rect, UIColorPaletteDef palette)
        {
            int chosen = Chosen();

            float y = Heading(rect, rect.y,
                chosen == 0 ? "WANTED: NOTHING YET" : "WANTED: " + chosen + " SPECIES", palette);

            SpeciesSearch.Draw(new Rect(rect.x, y, rect.width, 26f), palette);

            y += 30f;

            y = Columns(rect, y, palette);

            List<ThingDef> candidates = Candidates();

            Rect list = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            Rect view = new Rect(0f, 0f, list.width - 18f, candidates.Count * RowHeight + 4f);

            Widgets.BeginScrollView(list, ref speciesScroll, view);

            float at = 0f;

            for (int i = 0; i < candidates.Count; i++)
            {
                Row(new Rect(0f, at, view.width, 24f), candidates[i], palette);

                at += RowHeight;
            }

            Widgets.EndScrollView();
        }

        /// <summary>The two lane captions, so the pair of boxes on every row does not have to label itself.</summary>
        private float Columns(Rect rect, float y, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = palette.TextDisabled;

                float lane = BoxWidth + HeldWidth;
                float inner = rect.width - 18f;

                Widgets.Label(new Rect(rect.x + inner - lane * 2f, y, lane, 18f), "males");
                Widgets.Label(new Rect(rect.x + inner - lane, y, lane, 18f), "females");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return y + 20f;
        }

        /// <summary>
        /// One species: its name, and how many of each sex are wanted against how many are held.
        ///
        /// The target is created the first time a number goes above zero and dropped when both return to it, so
        /// the list a player scrolls has no bearing on the size of what gets saved.
        /// </summary>
        private void Row(Rect rect, ThingDef def, UIColorPaletteDef palette)
        {
            float lane = BoxWidth + HeldWidth;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;

                TamingTarget target = bill.TargetFor(def);

                GUI.color = target == null || target.Empty ? palette.TextSecondary : palette.TextPrimary;

                Widgets.LabelEllipses(new Rect(rect.x, rect.y, Mathf.Max(40f, rect.width - lane * 2f - 4f),
                    rect.height), def.LabelCap);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            Gender(new Rect(rect.xMax - lane * 2f, rect.y, lane, rect.height), def, Verse.Gender.Male, palette);
            Gender(new Rect(rect.xMax - lane, rect.y, lane, rect.height), def, Verse.Gender.Female, palette);
        }

        private void Gender(Rect rect, ThingDef def, Gender gender, UIColorPaletteDef palette)
        {
            UITextBoxControl box = BoxFor(def, gender);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;

                if (box.Draw(new Rect(rect.x, rect.y, BoxWidth, rect.height), palette))
                    Write(def, gender, box);

                int held = TamingBill.Held(map, def, gender);

                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = held > 0 ? palette.TextSecondary : palette.TextDisabled;

                // Wrapping off: "have 12" is wider than this lane at Tiny, and a label that wraps inside a fixed
                // height row overlaps the row beneath it. The hunting dialog's species count carries the same
                // note for the same reason.
                Text.WordWrap = false;

                Widgets.Label(new Rect(rect.x + BoxWidth + 4f, rect.y, HeldWidth - 6f, rect.height),
                    "have " + held);
            }
            finally
            {
                Text.WordWrap = true;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>The box for one species and sex, seeded from the bill the first time it is asked for.</summary>
        private UITextBoxControl BoxFor(ThingDef def, Gender gender)
        {
            string key = def.defName + (gender == Verse.Gender.Female ? "/f" : "/m");

            UITextBoxControl box;

            if (Boxes.TryGetValue(key, out box))
                return box;

            TamingTarget target = bill.TargetFor(def);
            int wanted = target == null ? 0 : target.Wanted(gender);

            box = new UITextBoxControl
            {
                Placeholder = "0",
                MaxLength = 2,
                ShowClearButton = false,
                Text = wanted > 0 ? wanted.ToString() : string.Empty
            };

            Boxes[key] = box;

            return box;
        }

        /// <summary>
        /// Writes one number back, creating or dropping the species target as needed.
        ///
        /// Anything unparseable reads as zero rather than being refused: a half typed number is a number on its
        /// way somewhere, and rejecting it would fight the person typing it.
        /// </summary>
        private void Write(ThingDef def, Gender gender, UITextBoxControl box)
        {
            int value;

            if (!int.TryParse(box.Text, out value) || value < 0)
                value = 0;

            value = Mathf.Clamp(value, 0, TamingTarget.Ceiling);

            TamingTarget target = bill.TargetFor(def);

            if (target == null)
            {
                if (value <= 0)
                    return;

                bill.Add(def);

                target = bill.TargetFor(def);

                if (target == null)
                    return;

                // Add seeds one of each, which is right for somebody adding a species and wrong here: the other
                // sex has its own box showing zero, and leaving the seeded one would put a number on screen that
                // nobody typed.
                target.males = 0;
                target.females = 0;
            }

            target.Set(gender, value);

            if (target.Empty)
                bill.Remove(def);
        }

        private int Chosen()
        {
            if (bill.targets == null)
                return 0;

            int count = 0;

            for (int i = 0; i < bill.targets.Count; i++)
            {
                if (bill.targets[i] != null && bill.targets[i].species != null && !bill.targets[i].Empty)
                    count++;
            }

            return count;
        }

        // ---------------------------------------------------------------------------------------
        // Footer and shared furniture
        // ---------------------------------------------------------------------------------------

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            bool suspended = bill.suspended;

            if (UICheckboxControl.Draw(new Rect(rect.x, rect.y, 220f, 32f), ref suspended, palette, "Suspended"))
                bill.suspended = suspended;

            // Beside Done rather than at the top, because saving and loading a shape is something you do after
            // setting one up, not before.
            if (GzpPalette.GrayButton(new Rect(rect.xMax - 232f, rect.y, 116f, 32f), "Templates", true, true))
                Find.WindowStack.Add(new Dialog_AnimalBillTemplates(true,
                    name => AnimalBillTemplates.Capture(bill, name),
                    template =>
                    {
                        AnimalBillTemplates.Apply(template, bill);

                        // Reseeded, or the boxes would keep showing what the bill said a moment ago.
                        Seed();
                    }));

            if (GzpPalette.GrayButton(new Rect(rect.xMax - 110f, rect.y, 110f, 32f), "Done", true, true))
                Close();
        }

        private float Heading(Rect rect, float y, string text, UIColorPaletteDef palette)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                GUI.color = palette.Border;

                Widgets.DrawLineHorizontal(rect.x, y, rect.width);

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(rect.x, y + 4f, rect.width, UIFonts.LineHeightOf(GameFont.Tiny)), text);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            return y + UIFonts.LineHeightOf(GameFont.Tiny) + 8f;
        }

        private float Note(Rect rect, float y, string text, UIColorPaletteDef palette,
            GameFont font = GameFont.Tiny)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = font;
                GUI.color = palette.TextSecondary;

                float height = Text.CalcHeight(text, rect.width);

                Widgets.Label(new Rect(rect.x, y, rect.width, height), text);

                return y + height + 8f;
            }
            finally
            {
                GUI.color = previousColor;
                Text.Font = previousFont;
            }
        }

        private float Field(Rect rect, float y, string caption, UITextBoxControl box, UIColorPaletteDef palette,
            Action write, float boxWidth = 92f, GameFont font = GameFont.Small)
        {
            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            boxWidth = Mathf.Clamp(boxWidth, 60f, rect.width - 40f);

            try
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                GUI.color = palette.TextSecondary;

                Widgets.LabelEllipses(new Rect(rect.x, y, rect.width - boxWidth - 6f, 26f), caption);

                Text.Font = font;
                Text.Anchor = TextAnchor.MiddleLeft;

                if (box.Draw(new Rect(rect.xMax - boxWidth, y, boxWidth, 26f), palette))
                    write();
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            return y + 30f;
        }

        // ---------------------------------------------------------------------------------------
        // The species list's contents
        // ---------------------------------------------------------------------------------------

        private static readonly List<ThingDef> Found = new List<ThingDef>();

        /// <summary>
        /// The wildlife on this map, plus anything the bill already wants, filtered by the search box.
        ///
        /// Same source as the hunting dialog's list, so the two windows agree about what is out there. A species
        /// the bill wants stays listed after the herd wanders off, or the numbers a player set would vanish with
        /// no way to see or clear them.
        /// </summary>
        private List<ThingDef> Candidates()
        {
            Found.Clear();

            List<AnimalSection> sections = AnimalRoster.Sections;

            for (int s = 0; s < sections.Count; s++)
            {
                AnimalSection section = sections[s];

                if (section.Kind != AnimalKind.Wild || section.Map != map)
                    continue;

                for (int g = 0; g < section.Groups.Count; g++)
                    Consider(section.Groups[g].Def);
            }

            if (bill.targets != null)
            {
                for (int i = 0; i < bill.targets.Count; i++)
                {
                    if (bill.targets[i] != null)
                        Consider(bill.targets[i].species);
                }
            }

            Found.SortBy(def => def.label);

            return Found;
        }

        private void Consider(ThingDef def)
        {
            if (def == null || Found.Contains(def))
                return;

            if (!SpeciesSearch.IsEmpty && !SpeciesSearch.Matches(def.label))
                return;

            Found.Add(def);
        }
    }
}
