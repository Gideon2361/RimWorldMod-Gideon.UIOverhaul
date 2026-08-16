using System;
using System.Collections.Generic;
using System.Reflection;
using System.Xml;
using Gideon.UIFramework.Helpers;
using HarmonyLib;
using Verse;

namespace Gideon.UIOverhaul.Features.Diagnostics
{
    /// <summary>What running one patch operation did.</summary>
    internal struct PatchSimulation
    {
        /// <summary>Whether the operation could be read at all. Everything below is meaningless if false.</summary>
        public bool Parsed;

        /// <summary>Why it could not be read, or why it could not be run.</summary>
        public string Error;

        /// <summary>The operation class RimWorld resolved it to.</summary>
        public string Operation;

        /// <summary>The xpath it targets, when it is the kind of operation that has one.</summary>
        public string Xpath;

        /// <summary>What <c>Apply</c> returned: whether the game would consider this patch to have worked.</summary>
        public bool Applied;

        public int MatchedBefore;
        public int MatchedAfter;

        /// <summary>The targeted nodes as they were, and as the patch left them.</summary>
        public List<string> Before;

        public List<string> After;
    }

    /// <summary>
    /// Runs a patch operation against the rebuilt document and reports what it did, without shipping it.
    ///
    /// <b>The question this answers.</b> A patch that matches nothing fails silently: RimWorld logs at most a
    /// terse complaint, the def is left as it was, and the author is left comparing an xpath against a mental
    /// model of a document they cannot see. That is most of what writing a patch consists of. Here the document
    /// is real, the operation is the game's own class doing the game's own work, and the answer is the nodes
    /// before and after.
    ///
    /// <b>The operation is built by RimWorld, not by us.</b> <c>DirectXmlToObject.ObjectFromXml</c> is what
    /// <c>ModContentPack</c> uses to turn a <c>&lt;Operation Class="..."&gt;</c> block into an object, so a
    /// custom operation from any mod works here exactly as it will at load, including the ones that take nested
    /// operations. Reimplementing the parse would produce a simulator that agreed with the game right up until
    /// it mattered.
    ///
    /// <b>Cross-references are deliberately not resolved.</b> <c>ObjectFromXml</c> is called with
    /// <c>doPostLoad: false</c>, matching how patches are read during loading: at that point the def database
    /// does not exist yet, and asking for it would fail on exactly the patches worth testing.
    ///
    /// <b>It runs against the whole document, and nothing is copied.</b> See
    /// <see cref="XmlWorkbench.Journaled{T}"/>: the edits a patch makes are recorded as it makes them and
    /// reversed afterwards, so the operation can read every definition while the document ends up untouched.
    /// </summary>
    internal static class XmlPatchSimulator
    {
        /// <summary>How many affected nodes are captured. A patch matching thousands is a mistake, not a case.</summary>
        private const int MaxCaptured = 25;

        private static readonly FieldInfo XpathField =
            AccessTools.Field(typeof(PatchOperationPathed), "xpath");

        /// <summary>
        /// Reads an operation and runs it.
        /// </summary>
        /// <param name="xml">The text of a single <c>&lt;Operation&gt;</c> element.</param>
        internal static PatchSimulation Run(string xml)
        {
            PatchSimulation result = new PatchSimulation
            {
                Before = new List<string>(),
                After = new List<string>()
            };

            if (xml.NullOrEmpty())
            {
                result.Error = "Nothing to simulate. Copy an Operation element and paste it here.";

                return result;
            }

            XmlNode node = Parse(xml, ref result);

            if (node == null)
                return result;

            PatchOperation operation = Build(node, ref result);

            if (operation == null)
                return result;

            result.Parsed = true;
            result.Operation = operation.GetType().Name;
            result.Xpath = XpathOf(operation);

            Apply(operation, ref result);

            return result;
        }

        private static XmlNode Parse(string xml, ref PatchSimulation result)
        {
            try
            {
                XmlDocument document = new XmlDocument();
                document.LoadXml(xml.Trim());

                return document.DocumentElement;
            }
            catch (Exception ex)
            {
                // Malformed XML is the ordinary case here, not a fault: this is text somebody is editing. The
                // parser's own message says which line and character, which is the useful part.
                result.Error = "That is not well formed XML.\n\n" + ex.Message;

                return null;
            }
        }

        private static PatchOperation Build(XmlNode node, ref PatchSimulation result)
        {
            try
            {
                PatchOperation operation = DirectXmlToObject.ObjectFromXml<PatchOperation>(node, false);

                if (operation == null || operation.GetType() == typeof(PatchOperation))
                {
                    // The base class comes back when Class names something that is not a loaded operation. Its
                    // own ApplyWorker only logs and fails, so running it would report a useless failure rather
                    // than the real problem, which is the attribute.
                    result.Error = "No patch operation matches that Class attribute. Check the spelling, and "
                                   + "that the mod providing it is loaded.";

                    return null;
                }

                return operation;
            }
            catch (Exception ex)
            {
                result.Error = "That operation could not be built.\n\n" + ex.Message;

                return null;
            }
        }

        /// <summary>
        /// Runs the operation against the whole document and puts the document back afterwards.
        ///
        /// <b>No copy is made.</b> An earlier version cloned the combined XML so the patch had something
        /// disposable to edit, which allocated the largest object graph in the game on every press and left it
        /// stuttering for minutes. <see cref="XmlWorkbench.Journaled{T}"/> instead records what the patch
        /// changes and reverses it, so the operation sees every definition -- which is what it needs to count
        /// siblings or check that something is absent -- while the document ends up untouched.
        /// </summary>
        private static void Apply(PatchOperation operation, ref PatchSimulation result)
        {
            string xpath = result.Xpath;
            List<string> before = result.Before;
            List<string> after = result.After;

            int matchedBefore = 0;
            int matchedAfter = 0;
            bool applied = false;
            string threw = null;

            string failure;

            XmlWorkbench.Journaled<bool>(document =>
            {
                matchedBefore = Capture(document, xpath, before);

                try
                {
                    applied = operation.Apply(document);
                }
                catch (Exception ex)
                {
                    // A throwing operation is a finding, not a crash: this is exactly the failure the author
                    // wants to see here rather than in somebody else's log. It still returns normally, so the
                    // journal is rewound and the document is left clean.
                    threw = ex.Message;

                    return false;
                }

                matchedAfter = Capture(document, xpath, after);

                return true;
            }, out failure);

            result.Applied = applied;
            result.MatchedBefore = matchedBefore;
            result.MatchedAfter = matchedAfter;

            if (!threw.NullOrEmpty())
            {
                result.Error = "The operation threw while running.\n\n" + threw;

                return;
            }

            // Reported rather than swallowed. The document has been rebuilt by this point, so the workbench is
            // usable again, but the reader should know this particular answer came from a run that could not be
            // fully unwound.
            if (!failure.NullOrEmpty())
                result.Error = failure + "\n\nThe document has been rebuilt from disk.";
        }

        /// <summary>
        /// The nodes an xpath selects, as text.
        /// </summary>
        /// <returns>How many matched, which can exceed what was captured.</returns>
        private static int Capture(XmlDocument document, string xpath, List<string> into)
        {
            if (xpath.NullOrEmpty())
                return 0;

            try
            {
                XmlNodeList matched = document.SelectNodes(xpath);

                if (matched == null)
                    return 0;

                foreach (XmlNode node in matched)
                {
                    if (into.Count >= MaxCaptured)
                        break;

                    into.Add(XmlWorkbench.PrettyPrint(node));
                }

                return matched.Count;
            }
            catch
            {
                // A malformed xpath is reported by the operation itself when it runs; failing here as well would
                // replace that message with a worse one.
                return 0;
            }
        }

        /// <summary>
        /// The xpath an operation targets, or null when it is not that kind of operation.
        ///
        /// Sequences, conditionals and the find-mod operations have no single path of their own, so there is
        /// nothing to show before and after; those are reported by whether they applied.
        /// </summary>
        private static string XpathOf(PatchOperation operation)
        {
            return UIGuard.Try("Diagnostics.PatchXpath", () =>
            {
                if (XpathField == null || !(operation is PatchOperationPathed))
                    return null;

                return XpathField.GetValue(operation) as string;
            }, null, null);
        }
    }
}
