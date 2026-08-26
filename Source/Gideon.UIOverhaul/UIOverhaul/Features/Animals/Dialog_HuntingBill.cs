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
    /// The settings for one hunting bill: what it keeps, how much of it, and what may be shot to keep it there.
    ///
    /// <b>Two columns because there are two questions.</b> The left is the order itself: what it is called, whether
    /// it works to a stock level or runs forever, and the four limits that stop a standing order doing something
    /// nobody asked for. The right is what it acts on: the items to keep stocked, and the species it may take.
    ///
    /// <b>No float menus.</b> Asked for on 2026-08-22, and it is the better arrangement anyway: choosing what to
    /// stock is a filter, and a filter is a tree with a search box, not a list that vanishes when the mouse moves.
    /// The items go through the game's own filter window, which this mod has already replaced with a modern panel,
    /// so the control is the one players know from storage and workbench bills. The species are a searchable list
    /// of checkboxes with the count standing on the map beside each one, which a float menu could never show.
    ///
    /// <b>Forever hides the stock half rather than greying it.</b> A target count means nothing to a culling
    /// order, and a disabled number box next to a live one invites the reader to work out which applies. The
    /// column says what the mode does instead.
    ///
    /// <b>Nothing is applied on a Save button.</b> Every control writes straight to the bill, the way a bill's own
    /// settings do, and closing is just closing. There is no draft state to lose.
    /// </summary>
    internal class Dialog_HuntingBill : Window
    {
        private static readonly UITextBoxControl Name = new UITextBoxControl
        {
            Placeholder = "Name, optional",
            MaxLength = 40
        };

        private static readonly UITextBoxControl Target = new UITextBoxControl
        {
            Placeholder = "300",
            MaxLength = 6,
            ShowClearButton = false
        };

        private static readonly UITextBoxControl Resume = new UITextBoxControl
        {
            Placeholder = "same",
            MaxLength = 6,
            ShowClearButton = false
        };

        private static readonly UITextBoxControl Keep = new UITextBoxControl
        {
            Placeholder = "2",
            MaxLength = 3,
            ShowClearButton = false
        };

        private static readonly UITextBoxControl Population = new UITextBoxControl
        {
            Placeholder = "6",
            MaxLength = 4,
            ShowClearButton = false
        };

        private static readonly UITextBoxControl SpeciesSearch = new UITextBoxControl
        {
            Placeholder = "Search species",
            Icon = TexButton.Search,
            MaxLength = 30
        };

        private readonly HuntingBill bill;
        private readonly Map map;

        /// <summary>
        /// Scroll and search state for the items filter.
        ///
        /// One per window rather than one shared static, because the filter panel hangs its own per window state
        /// off this object: two bills open at once would otherwise share a scroll position and a search box.
        /// </summary>
        private readonly ThingFilterUI.UIState filterState = new ThingFilterUI.UIState();

        private Vector2 speciesScroll;

        internal Dialog_HuntingBill(HuntingBill bill, Map map)
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

        /// <summary>
        /// Seeds the shared boxes from the bill.
        ///
        /// The text controls are static so that focus and caret state belong to one instance of each, which means
        /// they carry whatever the last bill left in them. Seeding here is what makes opening a second bill show
        /// the second bill's numbers.
        /// </summary>
        public override void PostOpen()
        {
            base.PostOpen();

            Seed();
        }

        /// <summary>
        /// Fills the shared boxes from the bill.
        ///
        /// Separate from <see cref="PostOpen"/> so a template load can reseed without going through it:
        /// <c>Window.PostOpen</c> replays the window's appear sound, and a template landing in a window that is
        /// already open is not the window appearing.
        /// </summary>
        private void Seed()
        {
            if (bill == null)
                return;

            // Once, here: a bill saved before the meats-only restriction may allow rows the window will no
            // longer show, and an invisible row cannot be turned off. See HuntingBill.ConfineToMeat.
            bill.ConfineToMeat();

            Name.Text = bill.label ?? string.Empty;
            Target.Text = bill.targetCount.ToString();
            Resume.Text = bill.resumeAt < 0 ? string.Empty : bill.resumeAt.ToString();
            Keep.Text = bill.keepAlive.ToString();
            Population.Text = bill.maxPopulation.ToString();

            SpeciesSearch.Clear();
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Animals.BillDialog", inRect, () => Contents(inRect),
                "This window failed to draw. The bill is unchanged and can be suspended from the animals tab.");
        }

        /// <summary>The lane on the right of a species row that says how many are standing on the map.</summary>
        private const float CountWidth = 62f;

        private const float ColumnGap = 16f;
        private const float FooterHeight = 36f;

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

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 40f, 32f), "Hunting bill");

                // Back to the body font before anything else draws. The title is the only Medium thing in this
                // window, and leaving it set made the ambient font Medium for every control that does not set its
                // own: "Hunt predators too" came out half again the size of the rows around it, which is what
                // Aaron screenshotted on 2026-08-22. A control inheriting the font is normal, so the window has
                // to leave a sane one behind rather than expecting each one to defend itself.
                Text.Font = GameFont.Small;

                Rect body = new Rect(inRect.x, inRect.y + 38f, inRect.width,
                    Mathf.Max(0f, inRect.height - 38f - FooterHeight));

                float left = Mathf.Round((body.width - ColumnGap) * 0.46f);

                Order(new Rect(body.x, body.y, left, body.height), palette);

                Acts(new Rect(body.x + left + ColumnGap, body.y, body.width - left - ColumnGap, body.height),
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

            // Wider and smaller than the number fields beside it, reported by Aaron on 2026-08-22 with "Preda"
            // clipped inside a 92 pixel box. A name is free text and the numbers are three digits, so sizing them
            // alike was the mistake: this takes everything the caption does not need, at the smaller font.
            y = Field(rect, y, "Name", Name, palette,
                () => bill.label = Name.Text.NullOrEmpty() ? null : Name.Text.Trim(),
                rect.width - 58f, GameFont.Tiny);

            y = Mode(rect, y, palette);

            if (bill.Forever)
            {
                y = Note(rect, y, "Keeps ordering hunts for as long as it is running, whatever is in store. For a "
                                  + "species you want gone rather than a larder you want full.", palette);
            }
            else if (bill.mode == HuntingBillMode.MaxPopulation)
            {
                y = Field(rect, y, "No more than, each", Population, palette, () =>
                {
                    int value;

                    if (int.TryParse(Population.Text, out value) && value >= 0)
                        bill.maxPopulation = value;
                });

                y = Note(rect, y, "Hunts whatever is over that count, per species, and stops when the herd is back "
                                  + "to it. The stockpiles are not consulted, so it keeps thinning a species that "
                                  + "is eating your crops even with a full larder.", palette);
            }
            else
            {
                y = Field(rect, y, "Keep in stock", Target, palette, () =>
                {
                    int value;

                    if (int.TryParse(Target.Text, out value) && value >= 0)
                        bill.targetCount = value;
                });

                y = Field(rect, y, "Start again below", Resume, palette, () =>
                {
                    int value;

                    bill.resumeAt = int.TryParse(Resume.Text, out value) && value >= 0 ? value : -1;
                });

                y = Note(rect, y, "Blank starts again the moment the stock is short by anything, which sends a "
                                  + "hunter out for one hare. A number a little under the target is usually "
                                  + "better.", palette);
            }

            y = Heading(rect, y, "LIMITS", palette);

            bool predators = bill.allowPredators;

            if (UICheckboxControl.Draw(new Rect(rect.x, y, rect.width, 26f), ref predators, palette,
                    "Hunt predators too",
                    "A wounded predator comes looking for whoever shot it. Off by default."))
                bill.allowPredators = predators;

            y += 28f;

            Text.Font = GameFont.Small;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
                bill.maxManhunterChance >= 1f
                    ? "Any manhunter risk allowed"
                    : "Skip over " + bill.maxManhunterChance.ToStringPercent() + " manhunter risk");

            y += 24f;

            float chance = Widgets.HorizontalSlider(new Rect(rect.x, y, rect.width - 6f, 22f),
                bill.maxManhunterChance, 0f, 1f, false, null, null, null, 0.05f);

            if (!Mathf.Approximately(chance, bill.maxManhunterChance))
                bill.maxManhunterChance = chance;

            y += 28f;

            // Absent in the over population mode, where the headcount above already says how many to leave: two
            // numbers for one floor is how a player sets a floor above their own ceiling and gets a bill that
            // never acts.
            if (bill.mode != HuntingBillMode.MaxPopulation)
            {
                y = Field(rect, y, "Leave alive, per species", Keep, palette, () =>
                {
                    int value;

                    bill.keepAlive = int.TryParse(Keep.Text, out value) && value >= 0 ? value : 0;
                });
            }

            Text.Font = GameFont.Small;
            GUI.color = palette.TextSecondary;

            Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
                bill.maxOutstanding == 1 ? "At most one hunt at once" : "At most " + bill.maxOutstanding + " hunts at once");

            y += 24f;

            float outstanding = Widgets.HorizontalSlider(new Rect(rect.x, y, rect.width - 6f, 22f),
                bill.maxOutstanding, 1f, 20f, false, null, null, null, 1f);

            bill.maxOutstanding = Mathf.RoundToInt(outstanding);
        }

        /// <summary>
        /// The three modes, as segments rather than a menu.
        ///
        /// The choice governs half the column under it, so it has to be visible at a glance rather than hidden
        /// behind a button that says what was chosen last. Three across a 330 pixel column is tight, which is why
        /// the labels are two words at most and the explanation sits underneath rather than inside them.
        /// </summary>
        private float Mode(Rect rect, float y, UIColorPaletteDef palette)
        {
            Rect row = new Rect(rect.x, y, rect.width, 26f);
            float third = Mathf.Floor((row.width - 8f) / 3f);

            Segment(new Rect(row.x, row.y, third, row.height), "Stock up",
                bill.mode == HuntingBillMode.UntilStocked, palette, () => bill.mode = HuntingBillMode.UntilStocked);

            Segment(new Rect(row.x + third + 4f, row.y, third, row.height), "Cull if over",
                bill.mode == HuntingBillMode.MaxPopulation, palette,
                () => bill.mode = HuntingBillMode.MaxPopulation);

            Segment(new Rect(row.x + third * 2f + 8f, row.y, row.xMax - row.x - third * 2f - 8f, row.height),
                "Forever", bill.mode == HuntingBillMode.Forever, palette,
                () => bill.mode = HuntingBillMode.Forever);

            return row.yMax + 8f;
        }

        /// <summary>
        /// One segment of the stock-or-cull switch: the mod's button, with the selected one toggled on.
        ///
        /// <b>The chosen segment used to be filled at full accent,</b> which is the primary treatment -- the one
        /// reserved for the single button a window exists to press. Two of those on one row, one of them a mode
        /// switch, is emphasis spent on the wrong control. Toggled is what "this one is selected" looks like
        /// everywhere else in the mod, and now here. Changed 2026-08-25.
        /// </summary>
        private void Segment(Rect rect, string label, bool on, UIColorPaletteDef palette, Action chosen)
        {
            if (UIActionButtonControl.Draw(rect, label, palette, false, true, GameFont.Small, null, on) && !on)
                chosen();
        }

        // ---------------------------------------------------------------------------------------
        // Right column: what it acts on
        // ---------------------------------------------------------------------------------------

        private void Acts(Rect rect, UIColorPaletteDef palette)
        {
            float y = rect.y;

            // Neither culling mode has a stock to keep, so the whole filter goes and the species list takes the
            // room. Drawing it disabled would leave the reader deciding which half of the window applies.
            if (bill.Stocked)
            {
                y = Heading(rect, y, "ITEMS TO MAINTAIN", palette);

                float height = Mathf.Min(260f, Mathf.Max(120f, rect.height * 0.5f));

                Filter(new Rect(rect.x, y, rect.width, height));

                y += height + 8f;
            }

            Species(new Rect(rect.x, y, rect.width, Mathf.Max(80f, rect.yMax - y)), palette);
        }

        /// <summary>
        /// The items filter.
        ///
        /// <b>Vanilla's own filter window, which reaches our panel through the patch that replaces it.</b> Calling
        /// the game's method rather than ours directly is deliberate: the panel is installed as a replacement for
        /// every filter in the game, so a bill's filter looks and behaves like a stockpile's, and if that patch
        /// ever retires itself after a failure this window follows it back to vanilla's rendering instead of
        /// breaking.
        ///
        /// The mask of 8 is what a storage tab passes, which opens the tree at the level a player expects to start
        /// reading rather than fully collapsed or fully expanded.
        /// </summary>
        private void Filter(Rect rect)
        {
            if (bill.filter == null)
                bill.filter = new ThingFilter();

            // The meat universe as the parent, so the tree opens on Meat and nothing else can be ticked.
            ThingFilterUI.DoThingFilterConfigWindow(rect, filterState, bill.filter, HuntingBill.Meats, 8);
        }

        /// <summary>
        /// Which species the bill may take, as a searchable list with the count on the map beside each name.
        ///
        /// <b>Nothing ticked means anything huntable,</b> which is stated on the heading rather than left to be
        /// discovered: an empty list looks like a bill that can do nothing, and this one can do everything.
        ///
        /// The list is the wildlife on this map plus anything the bill already names that is not here today, so a
        /// choice made in summer survives the herd wandering off.
        /// </summary>
        private void Species(Rect rect, UIColorPaletteDef palette)
        {
            int ticked = bill.species?.Count ?? 0;

            float y = Heading(rect, rect.y,
                ticked == 0 ? "SPECIES: ANY HUNTABLE" : "SPECIES: " + ticked + " CHOSEN", palette);

            Rect tools = new Rect(rect.x, y, rect.width, 26f);
            float buttons = 128f;

            SpeciesSearch.Draw(new Rect(tools.x, tools.y, Mathf.Max(60f, tools.width - buttons - 6f), 26f),
                palette);

            if (Button(new Rect(tools.xMax - buttons, tools.y, 60f, 26f), "All", palette))
                All(true);

            if (Button(new Rect(tools.xMax - 62f, tools.y, 62f, 26f), "None", palette))
                All(false);

            y = tools.yMax + 4f;

            List<ThingDef> candidates = Candidates();
            Rect list = new Rect(rect.x, y, rect.width, Mathf.Max(0f, rect.yMax - y));
            Rect view = new Rect(0f, 0f, list.width - 18f, candidates.Count * 26f + 4f);

            Widgets.BeginScrollView(list, ref speciesScroll, view);

            float at = 0f;

            for (int i = 0; i < candidates.Count; i++)
            {
                ThingDef def = candidates[i];
                Rect row = new Rect(0f, at, view.width, 24f);

                bool on = bill.species != null && bill.species.Contains(def);

                if (UICheckboxControl.Draw(new Rect(row.x, row.y, row.width - CountWidth - 4f, row.height),
                        ref on, palette, def.LabelCap))
                    Set(def, on);

                int here = OnMap(def);

                GameFont previousFont = Text.Font;
                TextAnchor previousAnchor = Text.Anchor;
                Color previousColor = GUI.color;
                bool previousWrap = Text.WordWrap;

                try
                {
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleRight;
                    GUI.color = here > 0 ? palette.TextSecondary : palette.TextDisabled;

                    // Wrapping off, which is the fix for what Aaron screenshotted on 2026-08-22: "none here" is
                    // wider than this lane at Tiny, so it wrapped onto a second line and overlapped the row
                    // beneath it. Labels default to wrapping, and a fixed height lane is exactly where that is
                    // wrong.
                    Text.WordWrap = false;

                    Widgets.Label(new Rect(row.xMax - CountWidth, row.y, CountWidth - 2f, row.height),
                        here > 0 ? here + " here" : "none");
                }
                finally
                {
                    Text.WordWrap = previousWrap;
                    GUI.color = previousColor;
                    Text.Anchor = previousAnchor;
                    Text.Font = previousFont;
                }

                at += 26f;
            }

            Widgets.EndScrollView();
        }

        private void Set(ThingDef def, bool on)
        {
            if (bill.species == null)
                bill.species = new List<ThingDef>();

            if (on)
            {
                if (!bill.species.Contains(def))
                    bill.species.Add(def);
            }
            else
            {
                bill.species.Remove(def);
            }
        }

        /// <summary>
        /// Ticks or clears every species the search is currently showing.
        ///
        /// The visible ones rather than all of them, so "All" after typing "beaver" means the beavers. Clearing
        /// everything is also how you get back to "any huntable", which the heading then says.
        /// </summary>
        private void All(bool on)
        {
            List<ThingDef> candidates = Candidates();

            for (int i = 0; i < candidates.Count; i++)
                Set(candidates[i], on);

            SoundDefOf.Click.PlayOneShotOnCamera();
        }

        /// <summary>
        /// This window's All and None, which are the mod's button and nothing local.
        ///
        /// <b>It was a hand drawn one and its twin in the taming dialog is not.</b> The two windows sit in the
        /// same list and are meant to read as siblings, so a pair of buttons that looked alike but only one of
        /// which clicked audibly or showed a pressed state was the kind of difference a player feels without
        /// being able to name. Converted with the rest of the mod on 2026-08-25.
        /// </summary>
        private bool Button(Rect rect, string label, UIColorPaletteDef palette)
        {
            return UIActionButtonControl.Draw(rect, label, palette);
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
            if (UIActionButtonControl.Draw(new Rect(rect.xMax - 232f, rect.y, 116f, 32f), "Templates", true, true))
                Find.WindowStack.Add(new Dialog_AnimalBillTemplates(false,
                    name => AnimalBillTemplates.Capture(bill, name),
                    template =>
                    {
                        AnimalBillTemplates.Apply(template, bill);

                        // Reseeded, or the boxes would keep showing what the bill said a moment ago.
                        Seed();
                    }));

            if (UIActionButtonControl.Draw(new Rect(rect.xMax - 110f, rect.y, 110f, 32f), "Done", true, true))
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

        /// <summary>
        /// A paragraph of explanation under a control, as tall as the paragraph actually is.
        ///
        /// <b>Measured rather than guessed.</b> Each of these carried its own literal height, and a literal is
        /// right until the wording grows: Aaron screenshotted the cull mode's note on 2026-08-22 with its last
        /// line and a half cut off, four lines of text drawn into a box sized for three. <c>Text.CalcHeight</c>
        /// asks the same layout engine that is about to draw it, at the same font and the same width, so the
        /// measurement and the drawing cannot disagree.
        /// </summary>
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

        /// <summary>
        /// A caption on the left and a text box on the right, writing through on change.
        ///
        /// <paramref name="boxWidth"/> and <paramref name="font"/> are per field because the fields are not alike:
        /// a target count is three digits and a name is a sentence. The box draws in whatever font is set when it
        /// is asked, which is what lets one control serve both.
        /// </summary>
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

        /// <summary>The wildlife on this map, plus anything the bill names, filtered by the search box.</summary>
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

            if (bill.species != null)
            {
                for (int i = 0; i < bill.species.Count; i++)
                    Consider(bill.species[i]);
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

        /// <summary>How many of this species are standing on the map, for the count beside its name.</summary>
        private int OnMap(ThingDef def)
        {
            List<AnimalSection> sections = AnimalRoster.Sections;

            for (int s = 0; s < sections.Count; s++)
            {
                AnimalSection section = sections[s];

                if (section.Kind != AnimalKind.Wild || section.Map != map)
                    continue;

                for (int g = 0; g < section.Groups.Count; g++)
                {
                    if (section.Groups[g].Def == def)
                        return section.Groups[g].Count;
                }
            }

            return 0;
        }
    }
}
