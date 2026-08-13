using System;
using System.Collections.Generic;
using Verse;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>
    /// A readout or control that can sit on the button bar beside the tabs: the time speed buttons, the
    /// date, the outdoor temperature, the weather.
    ///
    /// <b>Why a def.</b> The four this mod ships could have been an enum, and that is how the bar's display
    /// modes work. A widget is different: it is a piece of UI with behavior, and the interesting ones are
    /// the ones nobody has thought of yet. A def with a <c>workerClass</c> lets another mod add a widget by
    /// shipping XML and a class, with no patch and no dependency on this assembly beyond the base type,
    /// which is the same bargain RimWorld offers for designators and main button tabs.
    ///
    /// <b>Never placed automatically.</b> <see cref="UIButtonBarConfig.Resolve"/> appends any
    /// <c>MainButtonDef</c> the layout does not name, because a newly installed mod whose tab did not
    /// appear would look broken. Widgets get the opposite treatment: installing a mod must not put things
    /// on the bar the player never asked for, so a widget is drawn only where the layout names it. That is
    /// also why there is no <c>hidden</c> list for widgets, as there is for tabs. Absence is the default,
    /// so absence needs no record.
    /// </summary>
    public class UIBarWidgetDef : Def
    {
        /// <summary>
        /// The <see cref="UIBarWidgetWorker"/> subclass that draws this widget.
        ///
        /// Required. A def without one is reported in <see cref="ConfigErrors"/> and never draws, rather
        /// than leaving a slot on the bar that occupies space and shows nothing.
        /// </summary>
        public Type workerClass;

        /// <summary>
        /// Floor for the width the worker measures, so a widget whose text is briefly short does not
        /// collapse to nothing.
        /// </summary>
        public float minWidth = 40f;

        /// <summary>
        /// Sort order in the bar editor's list of widgets. Lower first; ties fall back to the label.
        ///
        /// Presentation only. Where a widget sits on the bar is the player's business and is stored in
        /// their layout, not here.
        /// </summary>
        public int order;

        private UIBarWidgetWorker workerInt;

        /// <summary>Set once instantiation has failed, so a broken widget is reported once and then left alone.</summary>
        private bool workerFailed;

        /// <summary>
        /// The instance that draws this widget, created on first use. Null when <see cref="workerClass"/>
        /// is missing, is not a widget worker, or threw on construction.
        ///
        /// Callers must tolerate the null. A widget can come from any mod, and one that cannot be built is
        /// not a reason for the button bar to stop drawing.
        /// </summary>
        public UIBarWidgetWorker Worker
        {
            get
            {
                if (workerInt != null || workerFailed)
                    return workerInt;

                if (workerClass == null || !typeof(UIBarWidgetWorker).IsAssignableFrom(workerClass))
                {
                    // Already reported by ConfigErrors, which runs at load. Latched silently here so a bad
                    // def does not add a log line per frame on top of it.
                    workerFailed = true;
                    return null;
                }

                try
                {
                    workerInt = (UIBarWidgetWorker) Activator.CreateInstance(workerClass);
                    workerInt.def = this;
                }
                catch (Exception ex)
                {
                    workerFailed = true;
                    Log.Error($"[Gideon.UIOverhaul] Could not create the worker for bar widget "
                              + $"'{defName}' ({workerClass.FullName}). It will not be drawn.\n{ex}");
                }

                return workerInt;
            }
        }

        /// <summary>
        /// The worker if one has already been built, without building one.
        ///
        /// For housekeeping that wants to touch the widgets currently in use -- resetting measured widths
        /// after a setting changed what they show. Going through <see cref="Worker"/> for that would
        /// instantiate every widget the player has never put on the bar, and would report the broken ones as
        /// failures on the way.
        /// </summary>
        public UIBarWidgetWorker WorkerIfCreated => workerInt;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
                yield return error;

            if (workerClass == null)
                yield return "workerClass is not set, so this widget cannot draw anything.";
            else if (!typeof(UIBarWidgetWorker).IsAssignableFrom(workerClass))
                yield return $"workerClass {workerClass.FullName} does not derive from "
                             + nameof(UIBarWidgetWorker) + ".";

            if (minWidth <= 0f)
                yield return "minWidth must be greater than zero.";
        }
    }
}
