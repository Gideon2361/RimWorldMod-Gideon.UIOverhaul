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
        private const float ToolbarHeight = 30f;
        private const float ToolbarGap = 6f;
        private const float StripHeight = 62f;

        private const float PortraitSize = 34f;

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

        // ---------------------------------------------------------------------------------------
        // State
        // ---------------------------------------------------------------------------------------

        /// <summary>The patient the pane is drawing, held by pawn so a roster rebuild cannot swap it.</summary>
        private static Pawn paneFor;

        private static bool paneOpen;

        /// <summary>Which columns were built last, so a hospital mod loading mid-session rebuilds them.</summary>
        private static bool builtWithVisitors;

        private static bool builtColumns;

        internal static float WindowWidth
        {
            get
            {
                EnsureColumns();

                float wanted = Grid.RequestedWidth + WindowChrome;

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

            Collect(sections);

            Rect content = inRect.ContractedBy(6f);

            Rect toolbar = new Rect(content.x, content.y, content.width, ToolbarHeight);

            Toolbar(toolbar, palette);

            content = new Rect(content.x, toolbar.yMax + ToolbarGap, content.width,
                Mathf.Max(0f, content.height - ToolbarHeight - ToolbarGap));

            if (HospitalVisitors.Available)
            {
                Rect strip = new Rect(content.x, content.y, content.width, StripHeight);

                Strip(strip, palette);

                content = new Rect(content.x, strip.yMax + ToolbarGap, content.width,
                    Mathf.Max(0f, content.height - StripHeight - ToolbarGap));
            }

            // The pane takes its width off the right before the grid lays out, so the grid draws into what is
            // left rather than under it. The same order the animals and pawns tabs use.
            if (paneOpen)
            {
                HospitalPatient patient = HospitalRoster.PatientFor(paneFor);

                if (patient == null)
                {
                    ClosePane();
                }
                else
                {
                    Rect pane = new Rect(content.xMax - HospitalPatientPane.PaneWidth, content.y,
                        HospitalPatientPane.PaneWidth, content.height);

                    content = new Rect(content.x, content.y,
                        content.width - HospitalPatientPane.PaneWidth - PaneGap, content.height);

                    if (!HospitalPatientPane.Draw(pane, patient, palette, HospitalRoster.Invalidate, ClosePane))
                        ClosePane();
                }
            }

            Grid.Draw(content, palette);

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
        // Toolbar
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The search, the one toggle, and the three readouts that decide whether the colony can cope.
        ///
        /// <b>Beds, doctors and medicine, because those are the three things a ward runs out of.</b> Each is a
        /// fact about the colony rather than about a patient, which is why they sit above the list rather than in
        /// a column of it.
        /// </summary>
        private static void Toolbar(Rect bar, UIColorPaletteDef palette)
        {
            Search.Draw(new Rect(bar.x, bar.y, 240f, ToolbarHeight - 2f), palette);

            bool everybody = HospitalRoster.ShowEverybody;

            if (UICheckboxControl.Draw(new Rect(bar.x + 250f, bar.y, 190f, ToolbarHeight - 2f), ref everybody,
                    palette, "Show everybody"))
            {
                HospitalRoster.ShowEverybody = everybody;

                HospitalRoster.Invalidate();
            }

            Map map = Find.CurrentMap;

            if (map == null)
                return;

            int occupied;
            int total;

            HospitalBeds.Count(map, out occupied, out total);

            float x = bar.xMax;

            x = TabParts.Readout(bar, x, "medicine", Medicine(map), palette,
                "How much medicine and how much herbal is on this map, unforbidden.");

            x = TabParts.Readout(bar, x, "doctors", Doctors(map).ToString(), palette,
                "Colonists who can do doctoring and are not down themselves.");

            TabParts.Readout(bar, x, "medical beds", occupied + " / " + total, palette,
                "Beds marked medical, and how many have somebody in them.");
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

        private static string Medicine(Map map)
        {
            return UIGuard.Try<string>("Hospital.Medicine", () =>
            {
                int normal = HospitalSurgery.Stock(map, ThingDefOf.MedicineIndustrial);
                int herbal = HospitalSurgery.Stock(map, ThingDefOf.MedicineHerbal);
                int glitter = HospitalSurgery.Stock(map, ThingDefOf.MedicineUltratech);

                string text = normal.ToString();

                if (herbal > 0)
                    text += "  herbal " + herbal;

                if (glitter > 0)
                    text += "  glitter " + glitter;

                return text;
            }, "?", null);
        }

        // ---------------------------------------------------------------------------------------
        // The hospital strip, with Colony Hospital installed
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// The map-level controls, which are about the hospital rather than about a patient.
        ///
        /// <b>Only with Colony Hospital installed, and every control is one of their public members.</b> Whether
        /// you are open, the hours you accept arrivals, the medicine visitors get and what they are fed: their
        /// tab's content, in ours, because two tabs for one screen is the thing this merge exists to avoid.
        /// </summary>
        private static void Strip(Rect rect, UIColorPaletteDef palette)
        {
            Map map = Find.CurrentMap;

            if (map == null)
                return;

            Widgets.DrawBoxSolid(rect, palette.PanelBackground);

            Rect inner = rect.ContractedBy(6f);

            bool receiving = HospitalVisitors.Receiving(map);

            bool was = receiving;

            // Measured rather than written down. A literal 170 left the label about 114 pixels after the switch
            // and its gap, which is a little under what "Receiving patients" needs, so it clipped to
            // "Receiving pati...". Same fault as the chips below carry a note about, and as a toolbar hit with
            // "Include buried". The three numbers that decide the answer are private to the control, so WidthFor
            // is the only thing that can get it right, and it keeps getting it right if the label is translated.
            string receivingLabel = "Receiving patients";

            Rect receivingRect = new Rect(inner.x, inner.y, UICheckboxControl.WidthFor(receivingLabel), 24f);

            if (UICheckboxControl.Draw(receivingRect, ref receiving, palette, receivingLabel) && receiving != was)
                HospitalVisitors.SetReceiving(map, receiving);

            Hours(new Rect(inner.x, inner.y + 26f, HoursWidth, 18f), map, palette);

            int occupied;
            int total;

            HospitalVisitors.Beds(map, out occupied, out total);

            float x = inner.xMax;

            x = TabParts.Readout(inner, x, "owed", HospitalVisitors.Owed(map).ToString(), palette,
                "What the current visitors owe between them.");

            x = TabParts.Readout(inner, x, "hospital beds", occupied + " / " + total, palette,
                "Beds designated as hospital beds by Colony Hospital.");

            x = TabParts.Readout(inner, x, "reputation", HospitalVisitors.Reputation(map).ToString(), palette,
                "Colony Hospital's reputation for this colony.");

            // Sized from what the readouts actually left rather than from a literal 220. The chips carry policy
            // names as long as "herbal medicine or worse", and a fixed width for those was the whole of the
            // truncation Aaron screenshotted.
            float chipsX = inner.x + HoursWidth + 12f;

            Care(new Rect(chipsX, inner.y, Mathf.Max(0f, x - 10f - chipsX), inner.height), map, palette);
        }

        /// <summary>How much of the strip the twenty-four hour blocks take.</summary>
        private const float HoursWidth = 360f;

        /// <summary>The twenty-four receiving hours, as a strip of togglable blocks.</summary>
        private static void Hours(Rect rect, Map map, UIColorPaletteDef palette)
        {
            float width = rect.width / 24f;

            for (int hour = 0; hour < 24; hour++)
            {
                Rect block = new Rect(rect.x + hour * width, rect.y, width - 1f, rect.height);

                bool on = HospitalVisitors.ReceivingHour(map, hour);

                Widgets.DrawBoxSolid(block, on ? palette.Accent : palette.SurfaceSunken);

                if (Mouse.IsOver(block))
                    TooltipHandler.TipRegion(block,
                        (TipSignal) (hour + ":00 - " + (on ? "accepting arrivals" : "closed")));

                if (!Widgets.ButtonInvisible(block))
                    continue;

                HospitalVisitors.SetReceivingHour(map, hour, !on);
            }
        }

        /// <summary>
        /// The two policy chips: what visitors are treated with, and what they are fed.
        ///
        /// <b>Caption above, value inside, rather than both on one line.</b> "Default care: herbal medicine or
        /// worse" is a long sentence to fit in a chip, and a chip that has to say what it is every time it says
        /// what it is set to spends most of its width on the half that never changes. Side by side rather than
        /// stacked, because the strip is one row of controls tall.
        /// </summary>
        private static void Care(Rect rect, Map map, UIColorPaletteDef palette)
        {
            // Below this there is not enough room for a policy name to survive, and two chips reading nothing but
            // ellipses are worse than the readouts they would be crowding.
            if (rect.width < 200f)
                return;

            float half = Mathf.Floor((rect.width - 8f) * 0.5f);

            MedicalCareCategory current = HospitalVisitors.DefaultCare();

            Chip(new Rect(rect.x, rect.y, half, rect.height), "default care", current.GetLabel(), palette,
                "Colony Hospital's own setting, which applies to every colony rather than only this one.",
                () => Find.WindowStack.Add(new FloatMenu(CareOptions())));

            FoodPolicy policy = HospitalVisitors.PatientFood(map);

            Chip(new Rect(rect.x + half + 8f, rect.y, half, rect.height), "patient food",
                policy != null ? policy.label : "default", palette,
                "What visiting patients are fed while they are here.",
                () => Find.WindowStack.Add(new FloatMenu(FoodOptions(map))));
        }

        /// <summary>A dim caption over a button carrying the current value.</summary>
        private static void Chip(Rect rect, string caption, string value, UIColorPaletteDef palette, string tip,
            System.Action clicked)
        {
            GameFont previousFont = Text.Font;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            try
            {
                Text.Font = GameFont.Tiny;
                Text.WordWrap = false;
                GUI.color = palette.TextDisabled;

                Widgets.Label(new Rect(rect.x + 2f, rect.y, rect.width - 4f, CaptionHeight), caption);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Font = previousFont;
            }

            if (TabParts.Button(new Rect(rect.x, rect.y + CaptionHeight + 1f, rect.width, 24f), value,
                    palette, true, false, tip))
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
        // Rows
        // ---------------------------------------------------------------------------------------

        private static void Collect(List<HospitalSection> sections)
        {
            Grid.Rows.Clear();

            Grid.SuppressCollapse = !Search.IsEmpty;

            for (int s = 0; s < sections.Count; s++)
            {
                HospitalSection section = sections[s];

                List<HospitalPatient> matching = Matching(section);

                if (matching.Count == 0)
                    continue;

                Grid.Rows.Add(new UIDesignatorTabRow
                {
                    SectionLabel = section.Label,
                    SectionSuffix = matching.Count.ToString()
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
