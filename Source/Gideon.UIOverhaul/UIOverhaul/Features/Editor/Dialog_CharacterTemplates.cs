using System;
using System.Collections.Generic;
using Gideon.UIFramework.Controls;
using Gideon.UIFramework.Defs;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Shared;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Editor
{
    /// <summary>
    /// Saving a character out and reading one back in.
    ///
    /// <b>One window for both directions, because they are the same list.</b> Asked for 2026-08-23: save a pawn as
    /// a template, apply a template to a pawn, and bring a character into another save. A template and an export
    /// file are the same artifact, so splitting them across two dialogs would have been two windows listing the
    /// same folder.
    ///
    /// <b>Three things can be done with a saved character, and they are genuinely different.</b> Apply writes it
    /// over the pawn currently open in the editor, which is revertible. Add as new generates a colonist and writes
    /// it onto them, which is not -- a pawn who now exists cannot be un-created. Delete removes the file.
    ///
    /// <b>Open folder is the export button.</b> Sharing a character means handing somebody a file, and the
    /// shortest path from this window to that file is the folder it lives in.
    /// </summary>
    internal sealed class Dialog_CharacterTemplates : Window
    {
        private const float HeaderHeight = 30f;

        private const float SaveRowHeight = 56f;

        private const float RowHeight = 52f;

        private const float FooterHeight = 34f;

        private const float Pad = 8f;

        private readonly Pawn pawn;

        private readonly EditorChanges changes;

        /// <summary>Called when a pawn is created from a file, so the editor can move to them.</summary>
        private readonly Action<Pawn> created;

        private readonly UITextBoxControl nameBox = new UITextBoxControl
        {
            Placeholder = "Name this character",
            MaxLength = 60
        };

        private List<CharacterTemplate> templates;

        private Vector2 scroll;

        private Dialog_CharacterTemplates(Pawn pawn, EditorChanges changes, Action<Pawn> created)
        {
            this.pawn = pawn;
            this.changes = changes;
            this.created = created;

            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
            draggable = true;
            drawShadow = true;

            if (pawn != null)
                nameBox.Text = UIGuard.Try<string>("Template.SeedName", () => pawn.LabelShortCap, null, null)
                               ?? string.Empty;

            Refresh();
        }

        internal static void Open(Pawn pawn, EditorChanges changes, Action<Pawn> created)
        {
            Find.WindowStack.Add(new Dialog_CharacterTemplates(pawn, changes, created));
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(520f, 560f); }
        }

        private void Refresh()
        {
            templates = CharacterTemplateStore.All();
        }

        public override void DoWindowContents(Rect inRect)
        {
            UIGuardedPanel.Draw("Editor.Templates", inRect, () => Contents(inRect),
                "The saved characters window could not finish drawing. No file was changed.");
        }

        private void Contents(Rect inRect)
        {
            UIColorPaletteDef palette = UIColorPaletteDef.Active;

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;

            try
            {
                Text.Font = GameFont.Medium;
                GUI.color = palette.TextPrimary;

                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 30f, HeaderHeight),
                    "Saved characters");

                Text.Font = GameFont.Small;

                float y = inRect.y + HeaderHeight + 4f;

                y = SaveRow(new Rect(inRect.x, y, inRect.width, SaveRowHeight), palette);

                Rect list = new Rect(inRect.x, y, inRect.width,
                    Mathf.Max(0f, inRect.yMax - FooterHeight - Pad - y));

                Rect view = new Rect(0f, 0f, list.width - 18f, templates.Count * (RowHeight + 4f) + 4f);

                Widgets.BeginScrollView(list, ref scroll, view);

                float rowY = 0f;

                for (int i = 0; i < templates.Count; i++)
                {
                    Row(new Rect(0f, rowY, view.width, RowHeight), templates[i], palette);

                    rowY += RowHeight + 4f;
                }

                Widgets.EndScrollView();

                if (templates.Count == 0)
                {
                    Text.Font = GameFont.Tiny;
                    GUI.color = palette.TextDisabled;

                    Widgets.Label(new Rect(list.x + 4f, list.y + 4f, list.width - 8f, 60f),
                        "Nothing saved yet. Name a character above and press Save.");

                    Text.Font = GameFont.Small;
                    GUI.color = palette.TextPrimary;
                }

                Footer(new Rect(inRect.x, inRect.yMax - FooterHeight, inRect.width, FooterHeight), palette);
            }
            finally
            {
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }
        }

        /// <summary>
        /// Naming and saving whoever the editor has open.
        ///
        /// <b>It says outright that it will overwrite.</b> The button changes word rather than throwing a
        /// confirmation dialog at somebody who almost certainly meant it: saving over a character you are
        /// iterating on is the normal case, not the dangerous one.
        /// </summary>
        private float SaveRow(Rect rect, UIColorPaletteDef palette)
        {
            if (pawn == null)
                return rect.y;

            Rect box = new Rect(rect.x, rect.y + 2f, rect.width - 116f, 26f);

            nameBox.Draw(box, palette);

            bool named = !nameBox.IsEmpty;

            bool over = named && CharacterTemplateStore.Exists(nameBox.Text);

            if (TabParts.Button(new Rect(box.xMax + 6f, box.y, 110f, 26f), over ? "Overwrite" : "Save", palette,
                    named, true,
                    named
                        ? over
                            ? "Replaces the saved character of that name."
                            : "Writes " + pawn.LabelShortCap + " out as a file you can keep or send."
                        : "Give it a name first."))
                Store();

            // Measured rather than given a height, and the measurement is what the caller gets back. This was a
            // three line paragraph drawn into a twenty-two pixel rect, so two thirds of it simply was not there:
            // it read "...and the durable half of" and stopped mid-sentence. Returning rect.yMax on top of that
            // meant the list below started at a fixed place regardless, so even a taller rect would have been
            // drawn over.
            float bottom = TabParts.Note(rect, box.yMax + 2f,
                "Saves looks, name, age, backstory, traits, skills, genes, gear, and the durable half of their "
                + "health: implants, missing parts, scars and chronic conditions. Not fresh wounds, needs, "
                + "thoughts or relationships.", palette);

            return bottom + 4f;
        }

        private void Store()
        {
            UIGuard.Try("Template.Store", () =>
            {
                CharacterTemplate template = CharacterTemplate.Capture(pawn, nameBox.Text.Trim());

                if (!CharacterTemplateStore.Save(template))
                {
                    EditorParts.Warn("That character could not be saved. The log says why.");

                    return;
                }

                Messages.Message("Saved " + template.Name + ".", MessageTypeDefOf.TaskCompletion, false);

                Refresh();
            }, "The character could not be saved.");
        }

        private void Row(Rect rect, CharacterTemplate template, UIColorPaletteDef palette)
        {
            UIElementPainter.OutlineRounded(rect, palette.Border,
                Mouse.IsOver(rect) ? palette.SurfaceRaised : palette.PanelBackground);

            GameFont previousFont = Text.Font;
            TextAnchor previousAnchor = Text.Anchor;
            Color previousColor = GUI.color;
            bool previousWrap = Text.WordWrap;

            float buttons = 250f;

            try
            {
                Text.WordWrap = false;
                Text.Anchor = TextAnchor.UpperLeft;

                Text.Font = GameFont.Small;
                GUI.color = palette.TextPrimary;

                UIRichText.Label(new Rect(rect.x + 8f, rect.y + 5f,
                    Mathf.Max(20f, rect.width - buttons - 12f), 22f), template.Name ?? "?");

                Text.Font = GameFont.Tiny;
                GUI.color = palette.TextDisabled;

                UIRichText.Label(new Rect(rect.x + 8f, rect.y + 26f,
                    Mathf.Max(20f, rect.width - buttons - 12f), 20f), template.Subtitle ?? string.Empty);
            }
            finally
            {
                Text.WordWrap = previousWrap;
                GUI.color = previousColor;
                Text.Anchor = previousAnchor;
                Text.Font = previousFont;
            }

            float x = rect.xMax - buttons;

            if (TabParts.Button(new Rect(x, rect.y + 12f, 74f, 26f), "Apply", palette, pawn != null, false,
                    pawn != null
                        ? "Writes this character over " + pawn.LabelShortCap
                          + ". Every field is logged, so Revert all undoes it."
                        : "No pawn is open in the editor."))
                Apply(template);

            x += 80f;

            bool canSpawn = Find.CurrentMap != null;

            if (TabParts.Button(new Rect(x, rect.y + 12f, 92f, 26f), "Add as new", palette, canSpawn, false,
                    canSpawn
                        ? "Generates a colonist, writes this character onto them, and lands them near the "
                          + "colony. Creating somebody cannot be undone."
                        : "There is no map to put them on."))
                Spawn(template);

            x += 98f;

            if (Widgets.ButtonImage(new Rect(x + 14f, rect.y + 18f, 16f, 16f), TexButton.Delete))
                Remove(template);
        }

        private void Apply(CharacterTemplate template)
        {
            UIGuard.Try("Template.ApplyTo", () =>
            {
                int missing = template.ApplyTo(pawn, changes);

                Report(template, pawn, missing);

                Close();
            }, "The template could not be applied.");
        }

        /// <summary>
        /// Generates a colonist and writes the character onto them.
        ///
        /// <b>This creates a pawn, which the proposal said the editor would never do.</b> Overruled on 2026-08-23,
        /// and the reason it was avoided still stands: somebody has to decide which faction, which cell, and what
        /// the storyteller thinks. The answers taken here are the ordinary ones -- a player colonist, at the cell
        /// the game uses for anything that arrives by drop, and no relations generated, since a character imported
        /// from another save has no history with anybody here.
        /// </summary>
        private void Spawn(CharacterTemplate template)
        {
            UIGuard.Try("Template.Spawn", () =>
            {
                Map map = Find.CurrentMap;

                if (map == null)
                    return;

                Pawn made = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    PawnKindDefOf.Colonist, Faction.OfPlayer,
                    forceGenerateNewPawn: true,
                    canGeneratePawnRelations: false,
                    colonistRelationChanceFactor: 0f,
                    allowFood: false,
                    allowAddictions: false,
                    forceNoIdeo: true,
                    developmentalStages: DevelopmentalStage.Adult));

                if (made == null)
                {
                    EditorParts.Warn("A colonist could not be generated.");

                    return;
                }

                IntVec3 cell = DropCellFinder.TradeDropSpot(map);

                GenSpawn.Spawn(made, cell, map);

                // A fresh log, because the pawn this creates is not the one the editor's log is about and
                // "Revert all" must never try to un-create somebody.
                EditorChanges own = new EditorChanges();

                int missing = template.ApplyTo(made, own);

                changes.RecordPermanent("added " + made.LabelShortCap);

                Report(template, made, missing);

                Messages.Message(made.LabelShortCap + " has arrived.", made, MessageTypeDefOf.PositiveEvent,
                    false);

                if (created != null)
                    created(made);

                Close();
            }, "The character could not be added.");
        }

        private static void Report(CharacterTemplate template, Pawn onto, int missing)
        {
            if (missing <= 0)
                return;

            EditorParts.Warn(missing + " of what " + (template.Name ?? "that character")
                                     + " names does not exist in this install, so " + onto.LabelShortCap
                                     + " got everything else. Usually a mod that was loaded when it was saved.");
        }

        private void Remove(CharacterTemplate template)
        {
            Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                "Delete the saved character " + template.Name + "? The file goes with it.",
                () =>
                {
                    if (CharacterTemplateStore.Delete(template))
                        Refresh();
                }, true));
        }

        private void Footer(Rect rect, UIColorPaletteDef palette)
        {
            if (TabParts.Button(new Rect(rect.x, rect.y, 130f, 28f), "Open folder", palette, true, false,
                    "Where the files live. Copy one out to share it, or drop one in to import it."))
                UIGuard.Try("Template.OpenFolder", () =>
                {
                    System.IO.Directory.CreateDirectory(CharacterTemplateStore.FolderPath);

                    Application.OpenURL(CharacterTemplateStore.FolderPath);
                }, "The folder could not be opened. It is at " + CharacterTemplateStore.FolderPath + ".");

            if (TabParts.Button(new Rect(rect.x + 136f, rect.y, 90f, 28f), "Refresh", palette, true, false,
                    "Reads the folder again, for a file you have just dropped in."))
                Refresh();

            if (TabParts.Button(new Rect(rect.xMax - 90f, rect.y, 90f, 28f), "Close", palette))
                Close();
        }
    }
}
