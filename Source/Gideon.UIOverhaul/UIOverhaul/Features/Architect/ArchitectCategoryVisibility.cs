using System;
using System.Collections.Generic;
using System.Xml;
using Verse;

namespace Gideon.UIOverhaul.Features.Architect
{
    /// <summary>
    /// Adds a <c>hidden</c> field to <see cref="DesignationCategoryDef"/>, so an architect category can be taken
    /// off the panel outright from XML.
    ///
    /// <code>
    /// &lt;li Class="PatchOperationAdd"&gt;
    ///   &lt;xpath&gt;Defs/DesignationCategoryDef[defName="Ship"]&lt;/xpath&gt;
    ///   &lt;value&gt;&lt;hidden&gt;true&lt;/hidden&gt;&lt;/value&gt;
    /// &lt;/li&gt;
    /// </code>
    ///
    /// <b>The field is not really on the def, because it cannot be.</b> DesignationCategoryDef is compiled into
    /// Assembly-CSharp and .NET has no way to add an instance field to a loaded type; Harmony rewrites method
    /// bodies, not object layouts. So this reads the value out of the XML before RimWorld parses it, keeps it in a
    /// set of its own, and answers from there. From an XML author's side that is indistinguishable from a real
    /// field, which is the point.
    ///
    /// <b>The node is removed once read.</b> Vanilla's loader reports every element it does not recognize as
    /// "XML error: &lt;hidden&gt;true&lt;/hidden&gt; doesn't correspond to any field in type
    /// DesignationCategoryDef". Taking the node out is what keeps a supported field from filling the log with
    /// errors about itself.
    ///
    /// <b>Where the value has to be read.</b> <c>LoadedModManager.LoadAllActiveMods</c> runs
    /// <c>ApplyPatches</c> and then <c>ParseAndProcessXML</c>, both against one unified document. Reading in a
    /// prefix on the second means every patch from every mod has already run, so this sees
    /// <c>&lt;hidden&gt;</c> whether it was written into a def directly or added to someone else's def by a
    /// patch. Reading any earlier would only see defs as their authors shipped them.
    ///
    /// <b>Two known limits, both from that timing.</b> XML inheritance is resolved after this, so
    /// <c>&lt;hidden&gt;</c> on an abstract parent does not reach its children: set it on the concrete def.
    /// And it is a load-time value, not a runtime one, so nothing can flip it while a game is running.
    /// </summary>
    internal static class ArchitectCategoryVisibility
    {
        /// <summary>The element name this reads. Named to match what a real field would have been called.</summary>
        private const string FieldName = "hidden";

        private const string DefElementName = "DesignationCategoryDef";

        /// <summary>
        /// The defNames to hide.
        ///
        /// By name rather than by def instance because the defs do not exist yet when this is filled, and the
        /// lookup afterwards is a string hash against a set holding a handful of entries, on a property read a
        /// few times per frame. Resolving to instances later would save almost nothing and would need another
        /// hook to do it in.
        /// </summary>
        private static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Whether anything is hidden at all, so the common case costs one integer compare.</summary>
        internal static bool AnyHidden => Hidden.Count > 0;

        internal static bool IsHidden(DesignationCategoryDef category)
        {
            if (Hidden.Count == 0 || category?.defName == null)
                return false;

            return Hidden.Contains(category.defName);
        }

        /// <summary>
        /// Reads and strips every <c>hidden</c> element on a category def in the unified document.
        ///
        /// Starts by clearing, because this runs again on a def reload: a category that has stopped being hidden
        /// between one load and the next has to stop being hidden here too.
        /// </summary>
        internal static void Ingest(XmlDocument document)
        {
            Hidden.Clear();

            XmlNode root = document?.DocumentElement;

            if (root == null)
                return;

            // Direct iteration of the root's children rather than an XPath query. These are the def nodes, and on
            // a large mod list there are tens of thousands of them; walking them once and comparing a name beats
            // asking the XPath engine to do the same thing.
            foreach (XmlNode defNode in root.ChildNodes)
            {
                if (defNode.NodeType != XmlNodeType.Element || defNode.Name != DefElementName)
                    continue;

                Read(defNode);
            }
        }

        private static void Read(XmlNode defNode)
        {
            List<XmlNode> fields = null;
            string defName = null;

            foreach (XmlNode child in defNode.ChildNodes)
            {
                if (child.NodeType != XmlNodeType.Element)
                    continue;

                if (child.Name == FieldName)
                {
                    // A list, because two patches can each add one. Two mods both hiding the same category is
                    // ordinary, and so is one mod doing it twice: a targeted patch for a named mod and a general
                    // sweep can easily overlap. Reading only the last and removing only that one would leave the
                    // others behind for vanilla to report as unknown fields, which is the one thing stripping is
                    // supposed to prevent.
                    if (fields == null)
                        fields = new List<XmlNode>();

                    fields.Add(child);
                }
                else if (child.Name == "defName")
                {
                    defName = child.InnerText?.Trim();
                }
            }

            if (fields == null)
                return;

            // Last wins, matching how RimWorld treats a repeated element: whichever patch ran last is the one
            // whose intent stands.
            XmlNode field = fields[fields.Count - 1];

            // Removed whether or not the value is usable, and whether or not there is a defName to attach it to.
            // Leaving any behind would mean vanilla reporting them as unknown fields.
            foreach (XmlNode duplicate in fields)
                defNode.RemoveChild(duplicate);

            string raw = field.InnerText?.Trim();

            if (defName.NullOrEmpty())
            {
                // An abstract def, which has a Name and no defName. Worth saying out loud: the author wrote
                // something that looks like it should work and silently will not, for the inheritance reason in
                // the class comment.
                Log.Warning($"[Gideon.UIOverhaul] <{FieldName}> was set on a {DefElementName} with no defName, "
                            + "which is an abstract def, and abstract defs are resolved after this is read. Set "
                            + $"<{FieldName}> on the concrete def instead.");
                return;
            }

            if (!bool.TryParse(raw, out bool value))
            {
                Log.Warning($"[Gideon.UIOverhaul] <{FieldName}> on {DefElementName} '{defName}' is '{raw}', "
                            + "which is not true or false. The category is left visible.");
                return;
            }

            // False is meaningful, not merely the absence of true: it lets a later patch un-hide a category that
            // an earlier one hid, which is the only way to override another mod's decision from XML.
            if (value)
                Hidden.Add(defName);
            else
                Hidden.Remove(defName);
        }
    }
}
