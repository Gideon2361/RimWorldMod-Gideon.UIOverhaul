using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Hospital
{
    /// <summary>
    /// The hospital tab: who needs a doctor, and what is being done about it.
    ///
    /// <b>Treating people is the one job in RimWorld with no screen of its own.</b> Who is hurt lives on the
    /// colonists tab, what is wrong with them lives behind an inspect tab each, and the operations you have queued
    /// live nowhere at all until you go and click the patient. This is the screen that answers the question in one
    /// place, and where surgery is queued and a course of treatment runs on a clock.
    ///
    /// <b>The sections are the triage and they are the whole reason this beats the colonists tab.</b> Critical, in
    /// treatment, awaiting surgery, recovering, animals: each one is a different thing for you to do, in the order
    /// you would do it.
    ///
    /// <b>The Treatment column is the answer, not the diagnosis.</b> The condition column already says what is
    /// wrong; that one says how it is going and what is holding it up. See <see cref="HospitalTreatment"/>.
    ///
    /// <b>Rows are rebuilt every frame from a roster that is not.</b> Layout is cheap and disposable; reading a
    /// patient is real work and happens twice a game second. The same arrangement the animals tab uses.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class HospitalPanel
    {
        // ---------------------------------------------------------------------------------------
        // Layout
        // ---------------------------------------------------------------------------------------

        private const float PatientColumnWidth = 208f;
        private const float ConditionColumnWidth = 132f;
        private const float HealthColumnWidth = 96f;
        private const float PainColumnWidth = 58f;
        private const float TreatmentColumnWidth = 228f;
        private const float BedColumnWidth = 126f;
        private const float OperationsColumnWidth = 112f;
        private const float StatusColumnWidth = 118f;
        private const float BillColumnWidth = 96f;

        private const float WindowChrome = 24f;
        private const float PaneGap = 8f;
        private const float Pad = 12f;

        /// <summary>The header block, sized as every other restyled tab sizes its own.</summary>
        private const float HeaderHeight = 66f;

        /// <summary>Side of the header glyph, and the air between it and the title.</summary>
        private const float GlyphSize = 34f;

        private const float GlyphGap = 10f;

        /// <summary>The strip under the header: the search, and the visitor policies beside it.</summary>
        private const float StripHeight = 26f;

        private const float StripGap = 6f;

        /// <summary>Width of the rail down the left, the same as every other restyled tab's.</summary>
        private const float RailWidth = 200f;

        private const float PortraitSize = 34f;

        /// <summary>Height of a block's own heading bar.</summary>
        private const float BlockHeadHeight = 20f;

        /// <summary>Height of the admissions block: a toggle, a schedule and its axis.</summary>
        private const float AdmissionsHeight = 140f;

        /// <summary>
        /// Widest the twenty-four hour blocks are allowed to get.
        ///
        /// A day stretched across a two thousand pixel window is a cell a hundred wide holding one hour, which
        /// reads as a bar chart of nothing. The mockup's proportion, kept.
        /// </summary>
        private const float HoursWidth = 560f;

        private static float CaptionHeight
        {
            get { return UIFonts.LineHeightOf(GameFont.Tiny); }
        }

        private static float ValueHeight
        {
            get { return UIFonts.LineHeightOf(GameFont.Small); }
        }

        /// <summary>
        /// A row tall enough for a caption over a value, whatever the font situation is.
        ///
        /// Derived rather than a constant, because Tiny is not always Tiny: a player who has turned tiny text off
        /// gets Small for both lines and needs the extra height.
        /// </summary>
        private static float RowHeight
        {
            get { return Mathf.Max(42f, CaptionHeight + ValueHeight + 6f); }
        }

        private static readonly UICardControl RowCard = new UICardControl { Padding = 0f, AccentWidth = 3f };

        private static readonly UIDesignatorTabControl Grid = new UIDesignatorTabControl
        {
            HasHeaderRow = true,
            RowGap = 2f,
            SectionHeaderHeight = 30f,
            AlternatingColumnBands = false,
            HeaderLabelOrientation = UIHeaderAngle.Horizontal
        };

        private static readonly UITextBoxControl Search = new UITextBoxControl
        {
            Placeholder = "Search patients",
            Icon = TexButton.Search,
            MaxLength = 30
        };

        /// <summary>Scratch for a bed menu. Never held past the click that built it.</summary>
        private static readonly List<Building_Bed> Beds = new List<Building_Bed>();

        /// <summary>The tab's own mark, the same texture its button on the bar uses.</summary>
        private static readonly Texture2D Glyph;

        static HospitalPanel()
        {
            // Through a local, because a readonly field can only be assigned in the constructor itself and the
            // guard does its work in a closure.
            Texture2D glyph = null;

            UIGuard.Try("Hospital.Glyph",
                () => glyph = ContentFinder<Texture2D>.Get("UI/MainButtonIcons/Medical", false),
                "The header has no glyph this session. Everything on the tab still reads.");

            Glyph = glyph;
        }

        // ---------------------------------------------------------------------------------------
        // State
        // ---------------------------------------------------------------------------------------

        /// <summary>The patient the pane is drawing, held by pawn so a roster rebuild cannot swap it.</summary>
        private static Pawn paneFor;

        private static bool paneOpen;

        /// <summary>Which columns were built last, so a hospital mod loading mid-session rebuilds them.</summary>
        private static bool builtWithVisitors;

        private static bool builtColumns;

        /// <summary>Which rail entry is chosen. Never null: the tab opens on every patient it has.</summary>
        private static string railKey = AllKey;

        private static Vector2 railScroll;

        private static bool railDragging;

        private static float railOffset;

        private static readonly List<UIRailElement> RailItems = new List<UIRailElement>();

        /// <summary>
        /// Sections this panel folded because nothing in them was wrong.
        ///
        /// Kept apart from the grid's own fold set so the two can be told apart: a section in here was folded by
        /// us and will be unfolded by us the moment somebody in it stops being well, while one the player folded
        /// by hand stays folded until they say otherwise.
        /// </summary>
        private static readonly HashSet<string> AutoCollapsed = new HashSet<string>();

        /// <summary>
        /// What a drag across the receiving hours is painting: on, off, or nothing in progress.
        ///
        /// Held for the length of the drag so every cell the pointer crosses takes the value the first one took,
        /// rather than each toggling itself and leaving a stripe.
        /// </summary>
        private static bool? hourPaint;

        // ---------------------------------------------------------------------------------------
        // Rail keys
        // ---------------------------------------------------------------------------------------

        private const string AllKey = "*all";
        private const string CareKey = "*care";
        private const string SurgeryKey = "*surgery";
        private const string RecoveringKey = "*recovering";
        private const string ColonistsKey = "*colonists";
        private const string AnimalsKey = "*animals";
        private const string AdmissionsKey = "*admissions";

        internal static float WindowWidth
        {
            get
            {
                EnsureColumns();

                float wanted = Grid.RequestedWidth + WindowChrome + RailWidth + Pad;

                if (paneOpen)
                    wanted += HospitalPatientPane.PaneWidth + PaneGap;

                return Mathf.Min(wanted, UI.screenWidth - 16f);
            }
        }

        internal static float WindowHeight
        {
            get { return Mathf.Min(760f, UI.screenHeight * 0.8f); }
        }

        /// <summary>Width held back for the pane, so a resized tab keeps its columns.</summary>
        internal static float PaneReservation
        {
            get { return paneOpen ? HospitalPatientPane.PaneWidth + PaneGap : 0f; }
        }

        // ---------------------------------------------------------------------------------------
        // Drawing
        // ---------------------------------------------------------------------------------------

        internal static void Draw(Rect inRect)
        {
            EnsureColumns();

            Grid.RowHeight = RowHeight;

            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            List<HospitalSection> sections = HospitalRoster.Sections;

            Rect content = inRect.ContractedBy(6f);

            float top = Header(new Rect(content.x, content.y, content.width, HeaderHeight), palette, sections);

            Rect body = new Rect(content.x, top, content.width, Mathf.Max(0f, content.yMax - top));

            Rail(new Rect(body.x, body.y, RailWidth, body.height), palette, sections);

            Rect rest = new Rect(body.x + RailWidth + Pad, body.y,
                Mathf.Max(0f, body.width - RailWidth - Pad), body.height);

            if (railKey == AdmissionsKey && HospitalVisitors.Available)
            {
                Admissions(rest, palette);
            }
            else
            {
                Rect strip = new Rect(rest.x, rest.y, rest.width, StripHeight);

                Strip(strip, palette);

                rest = new Rect(rest.x, strip.yMax + StripGap, rest.width,
                    Mathf.Max(0f, rest.height - StripHeight - StripGap));

                // The pane takes its width off the right before the grid lays out, so the grid draws into what
                // is left rather than under it. The same order the animals and pawns tabs use.
                if (paneOpen)
                {
                    HospitalPatient patient = HospitalRoster.PatientFor(paneFor);

                    if (patient == null)
                    {
                        ClosePane();
                    }
                    else
                    {
                        Rect pane = new Rect(rest.xMax - HospitalPatientPane.PaneWidth, rest.y,
                            HospitalPatientPane.PaneWidth, rest.height);

                        rest = new Rect(rest.x, rest.y,
                            rest.width - HospitalPatientPane.PaneWidth - PaneGap, rest.height);

                        if (!HospitalPatientPane.Draw(pane, patient, palette, HospitalRoster.Invalidate,
                                ClosePane))
                            ClosePane();
                    }
                }

                Collect(sections);

                Grid.Draw(rest, palette);
            }

            // After the grid, so any scroll view a click happened inside has been closed out.
            PawnCameraJump.Resolve();
        }

        private static void ClosePane()
        {
            paneOpen = false;
            paneFor = null;
        }

        private static void Open(HospitalPatient patient)
        {
            if (patient == null || patient.Pawn == null)
                return;

            if (paneOpen && paneFor == patient.Pawn)
            {
                ClosePane();

                return;
            }

            paneOpen = true;
            paneFor = patient.Pawn;
        }

        // ---------------------------------------------------------------------------------------
        // Header
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The block that names the screen, with the colony's medical figures seated in it.
        ///
        /// <b>The same shape every restyled tab uses.</b> What was a toolbar of a search box, a checkbox and
        /// three readouts is now a header saying where you are and what the ward has, a rail saying what you are
        /// looking at, and a strip holding nothing but controls.
        /// </summary>
        private static float Header(Rect rect, UIColorPaletteDef palette, List<HospitalSection> sections)
        {
            // SurfaceSunken, the same fill the rail beside it uses: header and rail are both chrome framing the
            // content, so they share a surface and the blocks between them sit above it.
            UIElementPainter.OutlineRounded(rect, palette.Border, palette.SurfaceSunken);

            Rect inner = rect.ContractedBy(10f);

            float text = inner.x;

            if (Glyph != null)
            {
                Rect mark = new Rect(inner.x, inner.y + (inner.height - GlyphSize) * 0.5f, GlyphSize, GlyphSize);

                Color previous = GUI.color;

                GUI.color = HospitalFaces.AccentOf(palette);
                GUI.DrawTexture(mark, Glyph);
                GUI.color = previous;

                text = mark.xMax + GlyphGap;
            }

            TabParts.RowLabel(new Rect(text, inner.y + 2f, 320f, 26f), "Hospital",
                HospitalFaces.AccentOf(palette), GameFont.Medium, HospitalFaces.Display,
                HospitalFaces.Size.Title);

            TabParts.RowLabel(new Rect(text, inner.y + 28f, 460f, 18f), Subtitle(), palette.TextSecondary,
                GameFont.Tiny, HospitalFaces.Condensed, HospitalFaces.Size.Subtitle);

            Readouts(inner, palette, sections);

            return rect.yMax + 6f;
        }

        /// <summary>
        /// The line under the title.
        ///
        /// <b>With Colony Hospital installed it is the three settings, because they are what the screen is
        /// currently doing.</b> The two policies and the receiving switch used to be a band of controls
        /// announcing themselves; the controls are still reachable, but what they are set to belongs here, where
        /// a subtitle would otherwise be repeating the tab's own name back at the player.
        /// </summary>
        private static string Subtitle()
        {
            Map map = Find.CurrentMap;

            if (!HospitalVisitors.Available || map == null)
                return "Who needs a doctor, and what is being done about it";

            return UIGuard.Try<string>("Hospital.Subtitle", () =>
            {
                FoodPolicy food = HospitalVisitors.PatientFood(map);

                return HospitalVisitors.DefaultCare().GetLabel()
                       + "  -  " + (food != null ? food.label : "default") + " patient food"
                       + "  -  " + (HospitalVisitors.Receiving(map)
                           ? "admitting patients"
                           : "not admitting patients");
            }, "Who needs a doctor, and what is being done about it", null);
        }

        /// <summary>
        /// The figures, right to left: what the ward has, and what it is failing to cover.
        ///
        /// <b>Medicine is two readouts rather than one string.</b> It was "2  herbal 546" in a single cell,
        /// which cannot be scanned, cannot be colored separately, and was using the gap between two numbers as a
        /// label. Glitterworld joins them only when there is any, because a permanent nought for something most
        /// colonies never see is a column of nothing.
        ///
        /// <b>Doctors reads in the danger color at nought,</b> whatever else is true. Two people with heatstroke
        /// and nobody set to treat them is the whole story of that screen, and it was a grey nought among five
        /// other grey figures.
        /// </summary>
        private static void Readouts(Rect area, UIColorPaletteDef palette, List<HospitalSection> sections)
        {
            Map map = Find.CurrentMap;

            if (map == null)
                return;

            int occupied;
            int total;

            HospitalBeds.Count(map, out occupied, out total);

            int doctors = Doctors(map);
            int needing = Needing(sections);

            float x = area.xMax;

            x = TabParts.Readout(area, x, "need care", needing.ToString(), palette,
                "Patients who are bleeding, downed, or carrying something a doctor should be treating.",
                needing > 0 ? palette.Warning : palette.TextPrimary);

            x = TabParts.Readout(area, x, "doctors", doctors.ToString(), palette,
                "Colonists who can do doctoring and are not down themselves.",
                doctors == 0 ? palette.Danger : palette.TextPrimary);

            x = TabParts.Readout(area, x, "medical beds", occupied + " / " + total, palette,
                "Beds marked medical, and how many have somebody in them.");

            x = TabParts.Readout(area, x, "medicine", Stock(map, ThingDefOf.MedicineIndustrial).ToString(),
                palette, "Industrial medicine on this map, unforbidden.");

            int glitter = Stock(map, ThingDefOf.MedicineUltratech);

            if (glitter > 0)
            {
                x = TabParts.Readout(area, x, "glitter", glitter.ToString(), palette,
                    "Glitterworld medicine on this map, unforbidden.");
            }

            TabParts.Readout(area, x, "herbal", Stock(map, ThingDefOf.MedicineHerbal).ToString(), palette,
                "Herbal medicine on this map, unforbidden.");
        }

        private static int Stock(Map map, ThingDef medicine)
        {
            return UIGuard.Try("Hospital.Stock", () => HospitalSurgery.Stock(map, medicine), 0, null);
        }

        private static int Doctors(Map map)
        {
            return UIGuard.Try("Hospital.Doctors", () =>
            {
                List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;

                if (colonists == null)
                    return 0;

                int count = 0;

                for (int i = 0; i < colonists.Count; i++)
                {
                    Pawn pawn = colonists[i];

                    if (pawn == null || pawn.Downed || pawn.InMentalState)
                        continue;

                    if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor))
                        continue;

                    if (pawn.workSettings != null && pawn.workSettings.EverWork
                                                  && pawn.workSettings.WorkIsActive(WorkTypeDefOf.Doctor))
                        count++;
                }

                return count;
            }, 0, null);
        }

        // ---------------------------------------------------------------------------------------
        // Rail
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The rail: what you are looking at, and how wide the question is.
        ///
        /// <b>It replaces the section headings as the way to get somewhere.</b> Five triage groups is a lot of
        /// scrolling to reach the one you came for; as entries with counts, and a warning-colored count on the
        /// group that needs a doctor, the trip is one click. The headings stay in the list, because they are
        /// still what a patient belongs to.
        ///
        /// <b>Everyone is the once-a-quadrum question of who is fit to travel,</b> and it is a filter like the
        /// rest rather than a switch: the roster lists the whole colony either way and the tab folds the groups
        /// where nothing is wrong. The toolbar checkbox that used to widen the list is gone with it.
        /// </summary>
        private static void Rail(Rect rect, UIColorPaletteDef palette, List<HospitalSection> sections)
        {
            RailItems.Clear();

            int needing = Needing(sections);

            RailItems.Add(Head("Care", palette));

            RailItems.Add(Entry(AllKey, "All patients", Listed(sections), false, palette,
                "Everybody the colony's doctors would be asked about."));

            RailItems.Add(Entry(CareKey, "Needs care", needing, needing > 0, palette,
                "Critical and in treatment together: the people something should be happening to."));

            RailItems.Add(Entry(SurgeryKey, "Awaiting surgery", Count(sections, HospitalTriage.AwaitingSurgery),
                false, palette, "Operations queued and waiting on a surgeon."));

            RailItems.Add(Entry(RecoveringKey, "Recovering", Count(sections, HospitalTriage.Recovering), false,
                palette, "In a bed, healing, with nothing holding it up."));

            RailItems.Add(new UIRailDividerControl { Color = palette.Border });
            RailItems.Add(Head("Everyone", palette));

            RailItems.Add(Entry(ColonistsKey, "Colonists", HospitalRoster.Colonists, false, palette,
                "Everybody on the map who is not an animal, whether or not anything is wrong with them."));

            RailItems.Add(Entry(AnimalsKey, "Animals", HospitalRoster.Animals, false, palette,
                "Every colony animal, whether or not anything is wrong with them."));

            if (HospitalVisitors.Available)
            {
                RailItems.Add(new UIRailDividerControl { Color = palette.Border });
                RailItems.Add(Head("Guests", palette));

                RailItems.Add(Entry(AdmissionsKey, "Admissions", HospitalRoster.Visiting, false, palette,
                    "The hospital itself: whether you are open, the hours you accept arrivals, and what your "
                    + "visitors owe."));
            }

            string picked = UIRailControl.Draw(rect, RailItems, railKey, ref railScroll, ref railDragging,
                ref railOffset, palette);

            if (picked == null || picked == railKey)
                return;

            railKey = picked;

            Grid.Scroll = Vector2.zero;
        }

        private static UIRailSectionHeaderControl Head(string label, UIColorPaletteDef palette)
        {
            return new UIRailSectionHeaderControl
            {
                Label = label,
                Uppercase = true,
                Face = HospitalFaces.Mono,
                Points = HospitalFaces.Size.RailHead,
                Color = palette.TextDisabled
            };
        }

        /// <summary>
        /// One rail entry. <paramref name="alarm"/> colors the count, which is how a group in difficulty is
        /// spotted without opening it.
        /// </summary>
        private static UIRailClickableEntry Entry(string key, string label, int count, bool alarm,
            UIColorPaletteDef palette, string tip)
        {
            bool on = railKey == key;

            return new UIRailClickableEntry(key, label)
            {
                Count = count,
                Tooltip = tip,
                Face = HospitalFaces.Condensed,
                Points = HospitalFaces.Size.RailName,
                CountFace = HospitalFaces.Mono,
                CountPoints = HospitalFaces.Size.RailCount,
                TextColor = on ? HospitalFaces.AccentOf(palette) : (Color?) null,
                CountColor = alarm
                    ? palette.Warning
                    : on ? HospitalFaces.AccentOf(palette) : (Color?) null
            };
        }

        /// <summary>Patients in one triage section.</summary>
        private static int Count(List<HospitalSection> sections, HospitalTriage triage)
        {
            for (int i = 0; i < sections.Count; i++)
            {
                if (sections[i].Triage == triage)
                    return sections[i].Count;
            }

            return 0;
        }

        /// <summary>Critical and in treatment together, which is the figure the header leads with.</summary>
        private static int Needing(List<HospitalSection> sections)
        {
            return Count(sections, HospitalTriage.Critical) + Count(sections, HospitalTriage.InTreatment);
        }

        /// <summary>Everybody currently on the list, whatever the scope happens to be.</summary>
        private static int Listed(List<HospitalSection> sections)
        {
            int total = 0;

            for (int i = 0; i < sections.Count; i++)
                total += sections[i].Count;

            return total;
        }

        // ---------------------------------------------------------------------------------------
        // Strip
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The search, and the two visitor policies beside it.
        ///
        /// The policies are Colony Hospital's and are absent without it. Chips rather than dropdowns because
        /// they change once a season and the header already says what they are set to; they only need to be
        /// reachable, not announced.
        /// </summary>
        private static void Strip(Rect rect, UIColorPaletteDef palette)
        {
            Search.Draw(new Rect(rect.x, rect.y, 240f, rect.height), palette);

            Map map = Find.CurrentMap;

            if (!HospitalVisitors.Available || map == null)
                return;

            float x = rect.x + 248f;

            // Sized from what is actually left rather than from a literal. The chips carry policy names as long
            // as "herbal medicine or worse", and a fixed width for those was the whole of the truncation Aaron
            // screenshotted on the old strip.
            float width = Mathf.Min(240f, (rect.xMax - x - 8f) * 0.5f);

            // Below this a policy name cannot survive, and two chips reading nothing but ellipses are worse than
            // the space they would be taking.
            if (width < 130f)
                return;

            MedicalCareCategory care = HospitalVisitors.DefaultCare();

            Chip(new Rect(x, rect.y, width, rect.height), "DEFAULT CARE", care.GetLabel(), palette,
                "Colony Hospital's own setting, which applies to every colony rather than only this one.",
                () => Find.WindowStack.Add(new FloatMenu(CareOptions())));

            FoodPolicy policy = HospitalVisitors.PatientFood(map);

            Chip(new Rect(x + width + 8f, rect.y, width, rect.height), "PATIENT FOOD",
                policy != null ? policy.label : "default", palette,
                "What visiting patients are fed while they are here.",
                () => Find.WindowStack.Add(new FloatMenu(FoodOptions(map))));
        }

        /// <summary>
        /// A chip: what the setting is, in small caps, then what it is set to.
        ///
        /// <b>Both on one line rather than a caption above a button.</b> The strip is one control tall now that
        /// the readouts have moved into the header, and a caption stacked over a value would make it two.
        /// </summary>
        private static void Chip(Rect rect, string caption, string value, UIColorPaletteDef palette, string tip,
            System.Action clicked)
        {
            bool over = Mouse.IsOver(rect);

            UIElementPainter.OutlineRounded(rect, over ? HospitalFaces.AccentOf(palette) : palette.Border,
                over ? palette.SurfaceRaised : palette.SurfaceSunken);

            float label = UITextControl.Width(caption, HospitalFaces.Mono, HospitalFaces.Size.RailHead) + 8f;

            TabParts.RowLabel(new Rect(rect.x + 8f, rect.y, label, rect.height), caption, palette.TextDisabled,
                GameFont.Tiny, HospitalFaces.Mono, HospitalFaces.Size.RailHead);

            TabParts.RowLabel(new Rect(rect.x + 8f + label, rect.y,
                    Mathf.Max(0f, rect.width - 16f - label), rect.height), value, palette.TextPrimary,
                GameFont.Small, HospitalFaces.Condensed, HospitalFaces.Size.RailName);

            TooltipHandler.TipRegion(rect, (TipSignal) tip);

            if (Widgets.ButtonInvisible(rect))
                clicked();
        }

        private static List<FloatMenuOption> CareOptions()
        {
            List<FloatMenuOption> found = new List<FloatMenuOption>();

            for (int i = 0; i < (int) MedicalCareCategory.Best + 1; i++)
            {
                MedicalCareCategory care = (MedicalCareCategory) i;

                found.Add(new FloatMenuOption(care.GetLabel(), () => HospitalVisitors.SetDefaultCare(care)));
            }

            return found;
        }

        private static List<FloatMenuOption> FoodOptions(Map map)
        {
            List<FloatMenuOption> found = new List<FloatMenuOption>();

            UIGuard.Try("Hospital.FoodPolicies", () =>
            {
                List<FoodPolicy> policies = Current.Game.foodRestrictionDatabase.AllFoodRestrictions;

                if (policies == null)
                    return;

                for (int i = 0; i < policies.Count; i++)
                {
                    FoodPolicy policy = policies[i];

                    found.Add(new FloatMenuOption(policy.label,
                        () => HospitalVisitors.SetPatientFood(map, policy)));
                }
            }, null);

            return found;
        }

        // ---------------------------------------------------------------------------------------
        // Admissions, with Colony Hospital installed
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The hospital itself: whether you are open, what you are owed, and the hours you accept arrivals.
        ///
        /// <b>A second business that shares this tab, so it gets a rail entry rather than a band above the
        /// list.</b> Reputation, owed and hospital beds are about the hospital; medicine, doctors and medical
        /// beds are about the ward. They are not the same question, and they were sitting in two rows of
        /// readouts competing to be read as one.
        ///
        /// <b>Every control here is a public member of Colony Hospital's.</b> Absent entirely without that mod,
        /// which is why the rail entry is conditional too.
        /// </summary>
        private static void Admissions(Rect rect, UIColorPaletteDef palette)
        {
            Map map = Find.CurrentMap;

            if (map == null)
                return;

            Rect block = new Rect(rect.x, rect.y, rect.width, Mathf.Min(AdmissionsHeight, rect.height));

            UIElementPainter.OutlineRounded(block, palette.Border, palette.PanelBackground);

            Rect bar = new Rect(block.x + 1f, block.y + 1f, block.width - 2f, BlockHeadHeight);

            Widgets.DrawBoxSolid(bar, palette.SurfaceSunken);
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.yMax, bar.width, 1f), palette.Border);

            TabParts.RowLabel(new Rect(bar.x + 12f, bar.y, bar.width * 0.5f, bar.height), "ADMISSIONS",
                palette.TextSecondary, GameFont.Tiny, HospitalFaces.Mono, HospitalFaces.Size.RailHead);

            Trailing(bar, "Colony Hospital", palette);

            Rect body = new Rect(block.x + 12f, bar.yMax + 12f, block.width - 24f, 24f);

            bool receiving = HospitalVisitors.Receiving(map);
            bool was = receiving;

            // Measured rather than written down: a literal width left "Receiving patients" clipped to
            // "Receiving pati...", and the three numbers that decide the answer are private to the control.
            string label = "Receiving patients";

            Rect toggle = new Rect(body.x, body.y, UICheckboxControl.WidthFor(label), body.height);

            if (UICheckboxControl.Draw(toggle, ref receiving, palette, label) && receiving != was)
                HospitalVisitors.SetReceiving(map, receiving);

            int occupied;
            int total;

            HospitalVisitors.Beds(map, out occupied, out total);

            float x = body.xMax;

            x = TabParts.Readout(body, x, "reputation", HospitalVisitors.Reputation(map).ToString(), palette,
                "Colony Hospital's reputation for this colony.");

            x = TabParts.Readout(body, x, "hospital beds", occupied + " / " + total, palette,
                "Beds designated as hospital beds by Colony Hospital.");

            TabParts.Readout(body, x, "owed", HospitalVisitors.Owed(map).ToString(), palette,
                "What the current visitors owe between them.");

            float width = Mathf.Min(HoursWidth, body.width);

            TabParts.RowLabel(new Rect(body.x, body.yMax + 14f, width, 14f),
                "ACCEPTING ARRIVALS  -  DRAG TO CHANGE", palette.TextDisabled, GameFont.Tiny,
                HospitalFaces.Mono, HospitalFaces.Size.RailHead);

            Hours(new Rect(body.x, body.yMax + 32f, width, 20f), map, palette);

            Ticks(new Rect(body.x, body.yMax + 54f, width, 12f), palette);
        }

        /// <summary>The right-hand note on a block's heading bar.</summary>
        private static void Trailing(Rect bar, string text, UIColorPaletteDef palette)
        {
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Anchor = TextAnchor.MiddleRight;
                Text.WordWrap = false;
                GUI.color = palette.TextDisabled;

                UITextControl.LabelEllipses(new Rect(bar.x, bar.y, bar.width - 12f, bar.height), text,
                    HospitalFaces.Mono, HospitalFaces.Size.RailHead);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
            }
        }

        /// <summary>
        /// The twenty-four receiving hours, as blocks you can drag across.
        ///
        /// <b>Drawn in the tab's own color rather than the accent.</b> Inside a restyled tab the tab color
        /// already means the thing you have chosen, because it is what lights the rail selection, so the hours
        /// you have picked belong in it and the accent stays free for the meaning it carries everywhere else.
        ///
        /// <b>A drag paints one value.</b> The first cell decides whether the drag is opening or closing and
        /// every cell the pointer crosses takes that; each cell toggling itself would leave a stripe wherever
        /// the run you dragged over was not uniform to begin with.
        /// </summary>
        private static void Hours(Rect rect, Map map, UIColorPaletteDef palette)
        {
            float width = rect.width / 24f;

            Color chosen = HospitalFaces.AccentOf(palette);

            if (Event.current.type == EventType.MouseUp)
                hourPaint = null;

            for (int hour = 0; hour < 24; hour++)
            {
                Rect block = new Rect(rect.x + hour * width, rect.y, Mathf.Max(1f, width - 2f), rect.height);

                bool on = HospitalVisitors.ReceivingHour(map, hour);

                UIElementPainter.OutlineRounded(block, on ? chosen : palette.Border,
                    on ? chosen : palette.SurfaceSunken);

                if (!Mouse.IsOver(block))
                    continue;

                TooltipHandler.TipRegion(block,
                    (TipSignal) (hour + ":00  -  " + (on ? "accepting arrivals" : "closed")));

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
                {
                    hourPaint = !on;

                    HospitalVisitors.SetReceivingHour(map, hour, !on);

                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseDrag && hourPaint.HasValue
                         && on != hourPaint.Value)
                {
                    HospitalVisitors.SetReceivingHour(map, hour, hourPaint.Value);

                    Event.current.Use();
                }
            }
        }

        /// <summary>
        /// The hour axis under the blocks.
        ///
        /// Without it a run of color is a shape rather than a time: you can see that something is on without
        /// being able to say when it starts. Every sixth hour, which is as many marks as the cells have room for.
        /// </summary>
        private static void Ticks(Rect rect, UIColorPaletteDef palette)
        {
            float width = rect.width / 24f;

            for (int hour = 0; hour < 24; hour += 6)
            {
                TabParts.RowLabel(new Rect(rect.x + hour * width, rect.y, width * 3f, rect.height),
                    hour.ToString(), palette.TextDisabled, GameFont.Tiny, HospitalFaces.Mono,
                    HospitalFaces.Size.RailHead);
            }
        }

        // ---------------------------------------------------------------------------------------
        // Rows
        // ---------------------------------------------------------------------------------------

        private static void Collect(List<HospitalSection> sections)
        {
            Grid.Rows.Clear();

            Grid.SuppressCollapse = !Search.IsEmpty;

            for (int s = 0; s < sections.Count; s++)
            {
                HospitalSection section = sections[s];

                if (!Shown(section))
                    continue;

                List<HospitalPatient> matching = Matching(section);

                if (matching.Count == 0)
                    continue;

                bool calm = Calm(matching);

                Fold(section, calm);

                Grid.Rows.Add(new UIDesignatorTabRow
                {
                    SectionLabel = section.Label,
                    SectionSuffix = Suffix(section, matching.Count, calm)
                });

                for (int i = 0; i < matching.Count; i++)
                {
                    Grid.Rows.Add(new UIDesignatorTabRow
                    {
                        Payload = matching[i],
                        DrawBackground = DrawRowBackground
                    });
                }
            }
        }

        /// <summary>Whether the rail's current entry wants this section at all.</summary>
        private static bool Shown(HospitalSection section)
        {
            switch (railKey)
            {
                case CareKey:
                    return section.Triage == HospitalTriage.Critical
                           || section.Triage == HospitalTriage.InTreatment;

                case SurgeryKey:
                    return section.Triage == HospitalTriage.AwaitingSurgery;

                case RecoveringKey:
                    return section.Triage == HospitalTriage.Recovering;

                case AnimalsKey:
                    return section.Triage == HospitalTriage.Animals;

                case ColonistsKey:
                    return section.Triage != HospitalTriage.Animals;

                default:
                    return true;
            }
        }

        /// <summary>
        /// Whether nothing in this section is wrong: everybody whole, nobody in pain, nothing queued.
        ///
        /// <b>This is what stops a healthy colony being thirty rows of dashes.</b> Eight people reading
        /// "Healthy, 100%, none, -, -, none, -" is eight columns saying nothing, eight times over, and the two
        /// who do need something end up as items four and ten.
        /// </summary>
        private static bool Calm(List<HospitalPatient> patients)
        {
            for (int i = 0; i < patients.Count; i++)
            {
                HospitalPatient patient = patients[i];

                if (patient.Health < 0.999f || patient.Pain > 0f || patient.Operations > 0
                    || patient.Doses > 0 || patient.Treatment.Active)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Folds a section where nothing is wrong, and unfolds it the moment something is.
        ///
        /// <b>Only sections this panel folded are reopened by it.</b> The player folding a section by hand is a
        /// decision and stays; one we closed because it was quiet reopens the instant it stops being quiet,
        /// which is the whole point of having closed it.
        /// </summary>
        private static void Fold(HospitalSection section, bool calm)
        {
            if (calm == AutoCollapsed.Contains(section.Label))
                return;

            if (calm)
            {
                AutoCollapsed.Add(section.Label);
                Grid.CollapsedSections.Add(section.Label);
            }
            else
            {
                AutoCollapsed.Remove(section.Label);
                Grid.CollapsedSections.Remove(section.Label);
            }
        }

        /// <summary>
        /// What the heading says on its right. A count, unless there is nothing to report, in which case it says
        /// so in words: a folded row has to read as an answer rather than as a row somebody hid.
        /// </summary>
        private static string Suffix(HospitalSection section, int count, bool calm)
        {
            if (!calm)
                return count.ToString();

            return count + (section.Triage == HospitalTriage.Animals
                ? " at full health"
                : " at full health, nothing scheduled");
        }

        private static readonly List<HospitalPatient> Filtered = new List<HospitalPatient>();

        private static List<HospitalPatient> Matching(HospitalSection section)
        {
            Filtered.Clear();

            for (int i = 0; i < section.Patients.Count; i++)
            {
                HospitalPatient patient = section.Patients[i];

                if (patient.Pawn == null)
                    continue;

                if (!Search.IsEmpty && !Search.Matches(patient.Pawn.LabelShortCap))
                    continue;

                Filtered.Add(patient);
            }

            return Filtered;
        }

        /// <summary>
        /// The row's card and the click that opens it.
        ///
        /// <b>The stripe is the condition colour,</b> which is the one distinction worth having at a glance down
        /// a long list, and it is the same colour the pawns tab and the colonist bar already use for that person.
        /// </summary>
        private static void DrawRowBackground(Rect row, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            HospitalPatient patient = data.Payload as HospitalPatient;

            if (patient == null)
                return;

            RowCard.AccentColor = patient.Summary.Color(palette);
            RowCard.BackgroundColor = palette.PanelBackground;
            RowCard.DrawChrome(row, palette);

            if (paneOpen && paneFor == patient.Pawn)
                Widgets.DrawBoxSolid(row, palette.SelectionOverlay);

            // The bed column is cut out of the row's hit target by geometry rather than by draw order: this
            // background is painted before any cell, so its button would otherwise swallow every bed click. The
            // same fault the pawns tab's area column had.
            float cut = BedColumnWidth + OperationsColumnWidth;

            float right = Mathf.Min(row.xMax, row.x + Grid.ColumnsWidth - cut);

            Rect click = new Rect(row.x, row.y, Mathf.Max(0f, right - row.x), row.height);

            if (Widgets.ButtonInvisible(click))
                Open(patient);
        }

        // ---------------------------------------------------------------------------------------
        // Columns
        // ---------------------------------------------------------------------------------------

        private static void EnsureColumns()
        {
            bool visitors = HospitalVisitors.Available;

            if (builtColumns && builtWithVisitors == visitors)
                return;

            builtColumns = true;
            builtWithVisitors = visitors;

            Grid.Columns.Clear();
            Grid.RowHeight = RowHeight;

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Patient", Width = PatientColumnWidth, Bandable = false, DrawCell = PatientCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Condition", Width = ConditionColumnWidth, DrawCell = ConditionCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Health", Width = HealthColumnWidth, DrawCell = HealthCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Pain", Width = PainColumnWidth, DrawCell = PainCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Treatment", Width = TreatmentColumnWidth, DrawCell = TreatmentCell
            });

            if (visitors)
            {
                Grid.Columns.Add(new UIDesignatorTabColumn
                {
                    Label = "Status", Width = StatusColumnWidth, DrawCell = StatusCell
                });

                Grid.Columns.Add(new UIDesignatorTabColumn
                {
                    Label = "Bill", Width = BillColumnWidth, DrawCell = BillCell
                });
            }

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Bed", Width = BedColumnWidth, DrawCell = BedCell
            });

            Grid.Columns.Add(new UIDesignatorTabColumn
            {
                Label = "Operations", Width = OperationsColumnWidth, DrawCell = OperationsCell
            });
        }

        private static HospitalPatient Of(UIDesignatorTabRow data)
        {
            return data == null ? null : data.Payload as HospitalPatient;
        }

        private static void PatientCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            HospitalPatient patient = Of(data);

            if (patient == null)
                return;

            Rect portrait = new Rect(cell.x + 4f, cell.y + (RowHeight - PortraitSize) * 0.5f, PortraitSize,
                PortraitSize);

            PawnPortraitCell.Draw(portrait, patient.Pawn, palette, palette.SurfaceSunken);

            if (PawnPortraitCell.IsOver(portrait) && Widgets.ButtonInvisible(portrait))
                PawnCameraJump.Request(patient.Pawn);

            Rect text = new Rect(portrait.xMax + 6f, cell.y + 3f, cell.xMax - portrait.xMax - 10f, RowHeight);

            float y = TabParts.Line(text, text.y, patient.Pawn.LabelShortCap, palette.TextPrimary);

            TabParts.Line(text, y, Subline(patient), palette.TextDisabled, GameFont.Tiny);
        }

        private static string Subline(HospitalPatient patient)
        {
            return UIGuard.Try<string>("Hospital.Subline", () =>
            {
                Pawn pawn = patient.Pawn;

                if (patient.Animal)
                    return pawn.def.LabelCap + ", " + pawn.gender.GetLabel();

                string role = pawn.story != null ? pawn.story.TitleShortCap.ToString() : null;

                string who = role.NullOrEmpty() ? string.Empty : role + " - ";

                who += pawn.gender.GetLabel() + ", " + pawn.ageTracker.AgeBiologicalYears;

                if (patient.Visiting && pawn.Faction != null)
                    who += " - " + pawn.Faction.Name;

                return who;
            }, null, null);
        }

        private static void ConditionCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            HospitalPatient patient = Of(data);

            if (patient == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y, cell.width - 8f, RowHeight);

            float y = band.y + (RowHeight - UIFonts.LineHeightOf(GameFont.Small)) * 0.5f;

            string badge = patient.Summary.Tag;
            float x = band.x;

            if (!badge.NullOrEmpty())
            {
                Rect pill = TabParts.Pill(band, x, y + 1f, badge, patient.Summary.TagColor(palette), palette);

                x = pill.xMax + 4f;
            }

            TabParts.Line(new Rect(x, y, Mathf.Max(20f, band.xMax - x), 0f), y, patient.Summary.Label,
                patient.Summary.Color(palette));

            if (!patient.Summary.Detail.NullOrEmpty())
                TooltipHandler.TipRegion(band, (TipSignal) patient.Summary.Detail);
        }

        /// <summary>
        /// Health as a bar, because you are comparing people.
        ///
        /// Four numbers in a column is arithmetic; four bars is a glance. The colour is the four-step vital scale
        /// the inspect pane uses, so the same percentage is the same colour wherever it appears.
        /// </summary>
        private static void HealthCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            HospitalPatient patient = Of(data);

            if (patient == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y + 6f, cell.width - 10f, RowHeight - 12f);

            float y = band.y;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(band.x, y, band.width, CaptionHeight),
                    Mathf.RoundToInt(patient.Health * 100f) + "%");
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            Rect lane = new Rect(band.x, y + CaptionHeight + 2f, band.width, 6f);

            UIProgressBarControl.Draw(lane, patient.Health, palette,
                Gideon.UIOverhaul.Features.Inspector.InspectPaneParts.Vital(patient.Health, palette));
        }

        /// <summary>
        /// Pain as a number, because pain is read against itself rather than against the others.
        /// </summary>
        private static void PainCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            HospitalPatient patient = Of(data);

            if (patient == null)
                return;

            string text = patient.Pain <= 0f ? "none" : Mathf.RoundToInt(patient.Pain * 100f) + "%";

            Color color = patient.Pain <= 0f
                ? palette.TextDisabled
                : patient.Pain >= 0.6f
                    ? palette.Danger
                    : patient.Pain >= 0.3f
                        ? palette.Warning
                        : palette.TextSecondary;

            Rect band = new Rect(cell.x + 4f, cell.y, cell.width - 8f, RowHeight);

            TabParts.Line(band, band.y + (RowHeight - ValueHeight) * 0.5f, text, color);
        }

        private static void TreatmentCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            HospitalPatient patient = Of(data);

            if (patient == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y + 3f, cell.width - 8f, RowHeight);

            float y = TabParts.Line(band, band.y, patient.Treatment.Label,
                patient.Treatment.Color(palette));

            TabParts.Line(band, y, patient.Treatment.Note, palette.TextDisabled, GameFont.Tiny);
        }

        private static void StatusCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            HospitalPatient patient = Of(data);

            if (patient == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y, cell.width - 8f, RowHeight);

            TabParts.Line(band, band.y + (RowHeight - ValueHeight) * 0.5f,
                patient.VisitStatus ?? "-",
                patient.VisitStatus == null ? palette.TextDisabled : palette.TextSecondary);
        }

        private static void BillCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            HospitalPatient patient = Of(data);

            if (patient == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y, cell.width - 8f, RowHeight);

            TabParts.Line(band, band.y + (RowHeight - ValueHeight) * 0.5f,
                patient.VisitBill < 0 ? "-" : patient.VisitBill.ToString(),
                patient.VisitBill < 0 ? palette.TextDisabled : palette.TextPrimary);
        }

        /// <summary>
        /// The bed, as a control.
        ///
        /// <b>It says "none" in grey when somebody critical is lying on the floor,</b> which is the single most
        /// common reason a colonist dies of something survivable. Clicking it picks a medical bed and sends the
        /// nearest able colonist to carry them there.
        /// </summary>
        private static void BedCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            HospitalPatient patient = Of(data);

            if (patient == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y + 8f, cell.width - 8f, RowHeight - 16f);

            string label = patient.Bed != null ? patient.Bed.LabelShortCap.ToString() : "none";

            bool floor = patient.Bed == null && (patient.Pawn.Downed || patient.Triage == HospitalTriage.Critical);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                bool over = Mouse.IsOver(band);

                UIElementPainter.OutlineRounded(band, floor ? palette.Danger : palette.Border,
                    over ? palette.SurfaceRaised : palette.SurfaceSunken);

                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;

                GUI.color = patient.Bed == null
                    ? floor ? palette.Danger : palette.TextDisabled
                    : patient.InMedicalBed
                        ? palette.TextPrimary
                        : palette.TextSecondary;

                Widgets.Label(band, label);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            TooltipHandler.TipRegion(band, (TipSignal) (patient.Bed == null
                ? "Not in a bed. Click to send somebody to carry them to one."
                : patient.InMedicalBed
                    ? "In a medical bed. Click to move them to another."
                    : "In an ordinary bed. Click to move them to a medical one."));

            if (!Widgets.ButtonInvisible(band))
                return;

            Find.WindowStack.Add(new FloatMenu(BedOptions(patient)));
        }

        private static List<FloatMenuOption> BedOptions(HospitalPatient patient)
        {
            List<FloatMenuOption> found = new List<FloatMenuOption>();

            HospitalBeds.Medical(patient.Map, patient.Pawn, Beds);

            for (int i = 0; i < Beds.Count; i++)
            {
                Building_Bed bed = Beds[i];

                if (bed == patient.Bed)
                    continue;

                string label = bed.LabelShortCap
                               + (bed.AnyOccupants ? "  (occupied)" : string.Empty);

                found.Add(new FloatMenuOption(label, () => HospitalBeds.Assign(patient.Pawn, bed)));
            }

            Beds.Clear();

            if (found.Count == 0)
                found.Add(new FloatMenuOption("There is no medical bed on this map they could use.", null));

            return found;
        }

        private static void OperationsCell(Rect cell, UIDesignatorTabRow data, UIColorPaletteDef palette)
        {
            HospitalPatient patient = Of(data);

            if (patient == null)
                return;

            Rect band = new Rect(cell.x + 4f, cell.y + 8f, cell.width - 8f, RowHeight - 16f);

            string label = patient.Operations == 0
                ? patient.Doses > 0 ? patient.Doses + " dose" + (patient.Doses == 1 ? string.Empty : "s") : "-"
                : patient.Operations + " queued";

            bool over = Mouse.IsOver(band);

            UIElementPainter.OutlineRounded(band, palette.Border,
                over ? palette.SurfaceRaised : palette.SurfaceSunken);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Text.WordWrap = false;
                GUI.color = patient.Operations > 0 ? palette.Warning : palette.TextDisabled;

                Widgets.Label(band, label);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            TooltipHandler.TipRegion(band, (TipSignal) "Add or change this patient's operations.");

            if (Widgets.ButtonInvisible(band))
                Find.WindowStack.Add(new Dialog_AddOperation(patient.Pawn));
        }
    }
}
