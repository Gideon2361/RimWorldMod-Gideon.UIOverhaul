using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using RimWorld;
using Verse;
using Gideon.UIFramework.Helpers;

namespace Gideon.UIOverhaul.Features.ButtonBar
{
    /// <summary>How much of a bar button is shown.</summary>
    public enum UIBarButtonMode
    {
        /// <summary>Icon and text, honoring the def's own <c>minimized</c> flag if it set one.</summary>
        Default,

        /// <summary>Icon only.</summary>
        Minimize,

        /// <summary>Text only, even when an icon is available.</summary>
        TextOnly,

        /// <summary>Icon and full text, overriding a def that asked to be minimized.</summary>
        Maximize
    }

    /// <summary>
    /// One slot on the button bar: a tab, a menu that reveals tabs when clicked, or a widget.
    /// </summary>
    public class UIButtonBarEntry
    {
        /// <summary>
        /// Overrides the tab's own name on the bar. Null uses the def's label, which is what most
        /// entries want; a rename is for a label that is too long or unclear at bar width.
        /// </summary>
        public string label;

        /// <summary>How much of this button to draw.</summary>
        public UIBarButtonMode mode = UIBarButtonMode.Default;

        /// <summary>defName of the MainButtonDef this slot shows. Null on a menu.</summary>
        public string tab;

        /// <summary>Label of the menu this slot is. Null on a tab.</summary>
        public string menu;

        /// <summary>
        /// defName of the <see cref="UIBarWidgetDef"/> this slot draws. Null on a tab or a menu.
        ///
        /// A widget is a readout or a control rather than a way into a tab: the clock, the date, the outdoor
        /// temperature, the weather. It has no label, icon or display mode of its own, because it draws its
        /// own content.
        /// </summary>
        public string widget;

        /// <summary>
        /// Texture path, without extension. On a tab it overrides the def's own icon, which is how a
        /// tab that shipped without one gets an icon. On a menu it is the only icon there is.
        /// </summary>
        public string icon;

        /// <summary>
        /// The slots this menu reveals, in order. Empty on a tab.
        ///
        /// Entries rather than bare defNames, so a tab inside a menu carries the same rename, icon and
        /// display mode as one on the bar and can be edited the same way. It was a list of names while a
        /// menu's contents were only ever a list to click; once they became editable there was nowhere to
        /// put the label. <see cref="ReadEntry"/> still accepts the old name-only form.
        ///
        /// One level only. The bar draws a menu as a single popup column, so a menu inside a menu has
        /// nowhere to appear; the editor refuses to nest one and the reader drops it with a warning.
        /// </summary>
        public List<UIButtonBarEntry> children = new List<UIButtonBarEntry>();

        /// <summary>
        /// Keeps this slot at the end of the bar, after anything appended.
        ///
        /// The bar appends every MainButtonDef the layout does not name, so that installing a mod cannot make
        /// its tab vanish. That is right for a tab and wrong for the pause menu, which belongs at the end no
        /// matter what arrives: without this, every unknown modded tab lands to the *right* of it.
        ///
        /// Naming every tab the game ships also fixed the position, but only for tabs known when the file was
        /// written -- this holds against tabs nobody has heard of yet, which is the actual requirement.
        ///
        /// Honored wherever the entry sits in the list, so a player who drags it around still gets it last.
        /// Several entries may set it; they keep their order relative to each other.
        /// </summary>
        public bool last;

        public bool IsMenu => !menu.NullOrEmpty();

        public bool IsWidget => !widget.NullOrEmpty();

        /// <summary>The MainButtonDef this slot shows, or null if it is a menu, a widget, or the mod is gone.</summary>
        public MainButtonDef Def =>
            tab.NullOrEmpty() ? null : DefDatabase<MainButtonDef>.GetNamedSilentFail(tab);

        /// <summary>
        /// The widget this slot draws, or null if it is not a widget or the mod that supplied it is gone.
        /// </summary>
        public UIBarWidgetDef WidgetDef =>
            widget.NullOrEmpty() ? null : DefDatabase<UIBarWidgetDef>.GetNamedSilentFail(widget);
    }

    /// <summary>
    /// The player's button bar layout: what is on it, in what order, grouped into which menus, with
    /// which icons, and what is hidden.
    ///
    /// Stored as XML in RimWorld's config folder, beside the game's own settings, rather than in this
    /// mod's folder. It is a player preference, not mod content: it has to survive the mod being
    /// updated or reinstalled, and it must not be something a Steam update overwrites.
    ///
    /// Read and written here rather than through ModSettings and Scribe. The layout is a nested tree of
    /// entries and children, which Scribe_Collections handles poorly, and a plain XML file is something
    /// a player can inspect, hand-edit and share.
    /// </summary>
    public class UIButtonBarConfig
    {
        public const string FileName = "UIOverhaul_ButtonBar.xml";

        /// <summary>Bar slots, left to right.</summary>
        public List<UIButtonBarEntry> entries = new List<UIButtonBarEntry>();

        /// <summary>
        /// defNames the player removed from the bar. Kept as a list of names rather than by clearing
        /// MainButtonDef.buttonVisible, so the choice is ours to undo and is not written into another
        /// mod's def.
        /// </summary>
        public List<string> hidden = new List<string>();

        public static string FilePath => Path.Combine(GenFilePaths.ConfigFolderPath, FileName);

        private static UIButtonBarConfig current;

        /// <summary>
        /// The active layout, loaded on first use. Never null: a missing or unreadable file yields an
        /// empty layout, which <see cref="Resolve"/> fills from the game's own button order, so a first
        /// run looks exactly like vanilla until the player changes something.
        /// </summary>
        public static UIButtonBarConfig Current => current ?? (current = Load());

        public static void Reload()
        {
            current = null;
        }

        /// <summary>
        /// Buttons removed from the bar outright, whatever the player's saved layout says.
        ///
        /// <b>The vanilla menu, because this mod replaced it.</b> Escape now opens our own settings window, which
        /// carries saving, loading, options and quitting, so vanilla's menu button is a second door into a room
        /// the player has already been shown.
        ///
        /// <b>And vanilla's two animal tabs, because one tab replaced both.</b> Their buttons would open windows
        /// that <c>Patch_MainTabsRoot_ToggleTab_Animals</c> redirects anyway, so leaving them on the bar would be
        /// three buttons for one list. Pressing F4 or F5 still works and lands on the right half of ours, which
        /// is the reason the redirect exists rather than only this.
        ///
        /// Conditioned on our tab actually being in the def database. Without that check, a mod folder whose XML
        /// failed to load would take the animals screen away entirely rather than falling back to the game's.
        ///
        /// Suppressed rather than hidden by default, and the difference matters: hidden is the player's setting
        /// to change, and a button whose window nothing opens any more should not be something they can put
        /// back and then find inert. Checked everywhere <see cref="IsHidden"/> is, and in the editor's list of
        /// available tabs, so it never appears as a row that can be dragged about to no effect.
        /// </summary>
        public static bool Suppressed(string defName)
        {
            if (defName.NullOrEmpty())
                return false;

            if (string.Equals(defName, "Menu", StringComparison.OrdinalIgnoreCase))
                return true;

            // Colony Hospital's tab, when ours is present: its content moves into ours, so leaving its button on
            // the bar would be two doors into one screen. Conditioned on our tab existing for the same reason the
            // animal ones are, and on their mod being loaded so the name cannot collide with anything else.
            if (string.Equals(defName, Hospital.HospitalIntegrations.ColonyHospitalTabDefName,
                    StringComparison.OrdinalIgnoreCase))
                return Hospital.HospitalIntegrations.ColonyHospitalLoaded && Hospital.HospitalTabs.Available;

            if (!string.Equals(defName, "Animals", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(defName, "Wildlife", StringComparison.OrdinalIgnoreCase))
                return false;

            return Animals.AnimalTabs.Available;
        }

        public bool IsHidden(string defName)
        {
            if (defName.NullOrEmpty())
                return false;

            for (int i = 0; i < hidden.Count; i++)
            {
                if (string.Equals(hidden[i], defName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The layout to draw: configured entries first, then any button the config has never seen.
        ///
        /// Appending the unknown ones is the important part. A newly installed mod adds a MainButtonDef
        /// that no saved layout mentions, and dropping it would make installing a mod look like the mod
        /// was broken. They arrive in the game's own order, after everything the player arranged.
        ///
        /// Entries naming a def that is not loaded are skipped but left in the file, so toggling a mod
        /// off and on again does not cost the player their arrangement.
        /// </summary>
        public List<UIButtonBarEntry> Resolve()
        {
            List<UIButtonBarEntry> result = new List<UIButtonBarEntry>();

            // Slots that asked to stay at the end. Held back until the appended unknowns are in, then added,
            // so a modded tab nobody has heard of cannot get between them and the edge of the bar.
            List<UIButtonBarEntry> trailing = new List<UIButtonBarEntry>();

            HashSet<string> placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (UIButtonBarEntry entry in entries)
            {
                List<UIButtonBarEntry> target = entry.last ? trailing : result;

                if (entry.IsMenu)
                {
                    // A menu whose every child is gone would be an empty button that does nothing.
                    bool anyChild = false;
                    foreach (UIButtonBarEntry child in entry.children)
                    {
                        if (child.tab.NullOrEmpty() || child.Def == null || IsHidden(child.tab)
                            || Suppressed(child.tab))
                            continue;

                        placed.Add(child.tab);
                        anyChild = true;
                    }

                    if (anyChild)
                        target.Add(entry);

                    continue;
                }

                if (entry.IsWidget)
                {
                    // Dropped when the mod that supplied it is gone, and left in the file, so disabling that
                    // mod and enabling it again does not cost the player the slot.
                    //
                    // Deliberately not added to `placed`: that set is about MainButtonDefs, and the append
                    // pass below never appends widgets. A tab that nothing mentions has to appear or a newly
                    // installed mod would look broken; a widget that nothing mentions has to stay off, or
                    // installing a mod would put things on the bar nobody asked for.
                    if (entry.WidgetDef != null)
                        target.Add(entry);

                    continue;
                }

                if (entry.Def == null || IsHidden(entry.tab) || Suppressed(entry.tab))
                    continue;

                // Marked placed either way, so the append pass below does not add a second copy of a slot that
                // is only being held back.
                placed.Add(entry.tab);
                target.Add(entry);
            }

            foreach (MainButtonDef def in DefDatabase<MainButtonDef>.AllDefsListForReading)
            {
                if (placed.Contains(def.defName) || IsHidden(def.defName) || Suppressed(def.defName))
                    continue;

                result.Add(new UIButtonBarEntry { tab = def.defName });
            }

            result.AddRange(trailing);

            return result;
        }

        // ---------------------------------------------------------------------------------------
        // Reading and writing
        // ---------------------------------------------------------------------------------------

        /// <summary>
        /// Where this mod's shipped default layout lives, relative to its own folder.
        ///
        /// Beside the loading screen config rather than in Defs, because it is not a def and a stray XML file in
        /// a Defs folder is parsed as one.
        /// </summary>
        private const string ShippedDefaultRelativePath = "Mods/gideon.uioverhaul/ButtonBar.xml";

        /// <summary>
        /// This mod's packageId, for finding its own folder among the running mods.
        /// </summary>
        private const string OwnPackageId = "gideon.uioverhaul";

        /// <summary>
        /// The shipped default layout, or null if it is missing or unreadable.
        ///
        /// Only this mod's copy is looked for, deliberately. The loading screen config scans every running mod
        /// because several mods contributing a splash image each is sensible; several mods each declaring what
        /// order the button bar should be in is not, and the winner would depend on load order.
        ///
        /// A missing file is not an error. It means the bar falls back to the game's own button order, which is
        /// what happened before a default shipped at all.
        /// </summary>
        /// <summary>
        /// The layout this mod ships, or an empty one if that file is missing.
        ///
        /// Public because "reset to default" in the bar editor has to mean the same default a fresh install
        /// gets. It used to build an empty config and resolve that, which returned the game's own button order
        /// -- so once a shipped default existed, resetting took the player somewhere they had never been.
        /// </summary>
        public static UIButtonBarConfig ShippedDefault()
        {
            return LoadShippedDefault() ?? new UIButtonBarConfig();
        }

        private static UIButtonBarConfig LoadShippedDefault()
        {
            List<ModContentPack> mods = LoadedModManager.RunningModsListForReading;

            if (mods == null)
                return null;

            foreach (ModContentPack mod in mods)
            {
                if (!string.Equals(mod?.PackageId, OwnPackageId, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    string path = Path.Combine(mod.RootDir, ShippedDefaultRelativePath);

                    if (!File.Exists(path))
                        return null;

                    XmlDocument doc = new XmlDocument();
                    doc.Load(path);
                    return Read(doc.DocumentElement);
                }
                catch (Exception ex)
                {
                    // Ours to fix, not the player's, so this goes to the log rather than to the config problems
                    // report they are shown.
                    Log.Error(UILogTag.Prefix + $"Could not read the shipped button bar default.\n{ex}");
                    return null;
                }
            }

            return null;
        }

        private static UIButtonBarConfig Load()
        {
            string path = FilePath;

            try
            {
                // No player layout yet: use the one this mod ships, which puts the tabs in a deliberate order
                // rather than whatever order the defs happened to load in. It is not written to the config
                // folder here -- the player's file is created the first time they arrange the bar themselves, so
                // an absent file keeps meaning "never customized" and a later change to the shipped default
                // still reaches players who never touched it.
                if (!File.Exists(path))
                    return ShippedDefault();

                XmlDocument doc = new XmlDocument();
                doc.Load(path);
                return Read(doc.DocumentElement);
            }
            catch (Exception ex)
            {
                // An unreadable layout must not stop the bar drawing. Vanilla order is a fine fallback,
                // but the player is told, since a discarded layout otherwise looks like lost work.
                Features.Options.UIConfigProblems.Report(path, new List<string>
                {
                    "Could not be read, so the bar is using the game's own button order: " + ex.Message
                });

                return new UIButtonBarConfig();
            }
        }

        private static UIButtonBarConfig Read(XmlElement root)
        {
            UIButtonBarConfig config = new UIButtonBarConfig();
            if (root == null)
                return config;

            foreach (XmlNode node in root.ChildNodes)
            {
                if (!(node is XmlElement element))
                    continue;

                switch (element.Name)
                {
                    case "entry":
                        config.entries.Add(ReadEntry(element));
                        break;

                    case "hidden":
                        foreach (XmlNode child in element.ChildNodes)
                        {
                            if (child is XmlElement item && !item.InnerText.NullOrEmpty())
                                config.hidden.Add(item.InnerText.Trim());
                        }
                        break;

                    default:
                        Log.Warning(UILogTag.Prefix + $"Unknown button bar element <{element.Name}>; ignored.");
                        break;
                }
            }

            return config;
        }

        private static UIButtonBarEntry ReadEntry(XmlElement element)
        {
            UIButtonBarEntry entry = new UIButtonBarEntry();

            foreach (XmlNode node in element.ChildNodes)
            {
                if (!(node is XmlElement field))
                    continue;

                string value = field.InnerText?.Trim();

                switch (field.Name)
                {
                    case "tab": entry.tab = value; break;
                    case "menu": entry.menu = value; break;
                    case "widget": entry.widget = value; break;
                    case "icon": entry.icon = value; break;
                    case "label": entry.label = value; break;

                    case "mode":
                        if (Enum.TryParse(value, true, out UIBarButtonMode parsed))
                            entry.mode = parsed;
                        else
                            Log.Warning(UILogTag.Prefix + $"'{value}' is not a button mode; using Default.");
                        break;

                    case "last":
                        entry.last = value.EqualsIgnoreCase("true");
                        break;

                    case "children":
                        ReadChildren(field, entry);
                        break;

                    default:
                        Log.Warning(UILogTag.Prefix + $"Unknown button bar entry field <{field.Name}>; ignored.");
                        break;
                }
            }

            return entry;
        }

        /// <summary>
        /// A menu's contents, in either form.
        ///
        /// <c>&lt;entry&gt;</c> is what this writes now, and carries a child's label, icon and mode.
        /// <c>&lt;li&gt;SomeTab&lt;/li&gt;</c> is the old name-only form, still read so that a layout written
        /// before children could be edited keeps its menus. Anything else in there names a tab the same way
        /// the old form did, which is why the fallback is by element content rather than by element name.
        ///
        /// A menu among the children is dropped. The bar draws one level of menu, so a nested one would be a
        /// button with nowhere to open, and silently keeping it in the file would mean the editor showing a
        /// row the bar never draws.
        /// </summary>
        private static void ReadChildren(XmlElement field, UIButtonBarEntry entry)
        {
            foreach (XmlNode node in field.ChildNodes)
            {
                if (!(node is XmlElement item))
                    continue;

                UIButtonBarEntry child = item.Name == "entry"
                    ? ReadEntry(item)
                    : new UIButtonBarEntry { tab = item.InnerText?.Trim() };

                if (child.IsMenu)
                {
                    Log.Warning(UILogTag.Prefix + $"Menu '{entry.menu}' contains a menu "
                                + $"('{child.menu}'), which the bar cannot draw; it has been dropped.");
                    continue;
                }

                if (child.tab.NullOrEmpty() && !child.IsWidget)
                    continue;

                entry.children.Add(child);
            }
        }

        /// <summary>
        /// Writes the layout. Reported and swallowed on failure: a read-only config folder is the
        /// player's problem to fix, and it must not take the UI down with it.
        /// </summary>
        public void Save()
        {
            string path = FilePath;

            try
            {
                // So the watcher does not mistake our own write for someone editing the file.
                Features.Options.UIConfigWatcher.NotifySelfWrite();

                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

                XmlWriterSettings settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "  ",
                    Encoding = new UTF8Encoding(false)
                };

                using (XmlWriter writer = XmlWriter.Create(path, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteComment(
                        " Button bar layout for Gideon's UI Overhaul. Written by the in-game bar editor;"
                        + " safe to hand-edit. Order here is the order on the bar. An <entry> is a <tab>"
                        + " naming a MainButtonDef, a <menu> whose <children> it reveals, or a <widget>"
                        + " naming a UIBarWidgetDef. ");

                    writer.WriteStartElement("ButtonBar");

                    foreach (UIButtonBarEntry entry in entries)
                        WriteEntry(writer, entry);

                    if (hidden.Count > 0)
                    {
                        writer.WriteStartElement("hidden");
                        foreach (string defName in hidden)
                            writer.WriteElementString("li", defName);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception ex)
            {
                Log.Error(UILogTag.Prefix + $"Could not write the button bar layout to {path}.\n{ex}");
            }
        }

        /// <summary>
        /// One entry, and its children if it has any.
        ///
        /// A child is written with the same element and the same fields as a top-level slot, so the two
        /// cannot drift apart and a child that gains a rename or an icon keeps it. <c>last</c> is written
        /// either way rather than suppressed inside a menu: it means nothing there, but reading and writing
        /// the same shape is what keeps a hand-edited file predictable.
        /// </summary>
        private static void WriteEntry(XmlWriter writer, UIButtonBarEntry entry)
        {
            writer.WriteStartElement("entry");

            if (entry.IsMenu)
                writer.WriteElementString("menu", entry.menu);
            else if (entry.IsWidget)
                writer.WriteElementString("widget", entry.widget);
            else if (!entry.tab.NullOrEmpty())
                writer.WriteElementString("tab", entry.tab);

            if (!entry.icon.NullOrEmpty())
                writer.WriteElementString("icon", entry.icon);

            if (!entry.label.NullOrEmpty())
                writer.WriteElementString("label", entry.label);

            if (entry.mode != UIBarButtonMode.Default)
                writer.WriteElementString("mode", entry.mode.ToString());

            // Written so a player's own layout keeps the pause menu pinned to the end after they rearrange
            // the bar, rather than losing it the first time they save.
            if (entry.last)
                writer.WriteElementString("last", "true");

            if (entry.children.Count > 0)
            {
                writer.WriteStartElement("children");

                foreach (UIButtonBarEntry child in entry.children)
                    WriteEntry(writer, child);

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }
    }
}
