using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Gideon.UIOverhaul.Features.Integrations
{
    /// <summary>
    /// Raises a notification when a Phinix chat message arrives.
    ///
    /// <b>Phinix announces trades and says nothing about chat.</b> A trade raises a letter; an incoming message
    /// plays a small tick and nothing else, so a message that arrives while you are looking at the map is a sound
    /// you may or may not have caught and no way to find out what it was. This adds the missing half.
    ///
    /// <b>No Harmony patch, because none is needed.</b> Phinix exposes
    /// <c>Client.Instance.OnChatMessageReceived</c> as a public event -- an extension point its author put there
    /// deliberately -- so this subscribes to it. That is better than patching in every way that matters: nothing
    /// is rewritten, nothing breaks when their internals change, and the mod keeps working exactly as written
    /// whether or not we are here.
    ///
    /// <b>Reached by reflection all the same.</b> Their assembly cannot be referenced at build time without
    /// making this mod depend on theirs, so the type is found by name and the event bound at runtime. Everything
    /// is resolved once and the whole thing stands down quietly if any piece is missing, which is what a future
    /// Phinix version renaming something looks like.
    ///
    /// <b>The handler does not raise the notification itself, and that is the important part.</b> Phinix's chat
    /// event is raised from its network thread -- their own code queues sounds rather than playing them for
    /// exactly this reason. <c>Messages.Message</c> touches the live message list, the archive and the sound
    /// system, none of which may be touched off the main thread. So the handler queues a string and
    /// <see cref="PhinixChatPump"/> drains it during the game's own update.
    /// </summary>
    internal static class PhinixIntegration
    {
        /// <summary>Longest message body shown. A card is one line; the rest is in the chat tab.</summary>
        private const int MaxBody = 140;

        private static readonly object Lock = new object();
        private static readonly List<string> pending = new List<string>();

        private static bool hooked;

        /// <summary>Whether the event was actually bound, as opposed to merely attempted.</summary>
        private static bool subscribed;

        // Resolved once from Phinix's own assembly. Null means that piece was not found, and every read of them
        // below treats that as "skip this check" rather than "fail".
        private static Type clientType;
        private static FieldInfo instanceField;
        private static PropertyInfo uuidProperty;
        private static PropertyInfo settingsProperty;
        private static FieldInfo blockedUsersField;
        private static PropertyInfo messageProperty;

        /// <summary>Whether the player wants these notifications.</summary>
        private static bool Wanted =>
            UIGuard.Try("Integrations.ReadPhinixSetting",
                () => UIOverhaulSettingsFile.Current?.notifyPhinixChat ?? true, true, null);

        /// <summary>
        /// Subscribes to Phinix's chat event, once.
        ///
        /// Called from a startup class rather than from this mod's constructor, because Phinix's own
        /// <c>Client</c> is a <c>Mod</c> and sets its <c>Instance</c> in its constructor -- and which of two mod
        /// constructors runs first is load order, which is not ours to depend on. By the time static
        /// constructors run, every mod has been built.
        /// </summary>
        internal static void Hook()
        {
            if (hooked || !ModIntegrations.Loaded(ModIntegrations.PhinixPackageId))
                return;

            hooked = true;

            // Read for its side effect before anything below reports anything. The framework's UIDebug cannot
            // see this mod's settings file, so it is told the debug flag when that file is first loaded -- and
            // everything this method logs goes through UIDebug. Without this touch, whether the report appears
            // would depend on whether something else happened to have read the settings first, which is a load
            // order question with no right answer.
            UIGuard.Try("Integrations.PrimeDebugFlag", () => { _ = UIOverhaulSettingsFile.Current; }, null);

            UIGuard.Try("Integrations.HookPhinix", Subscribe,
                "Incoming Phinix chat messages are not announced. Phinix itself is unaffected.");
        }

        /// <summary>
        /// Binds to the event, saying out loud what happened either way.
        ///
        /// <b>Every step reports, and that is a correction rather than a flourish.</b> The first version of this
        /// returned quietly at five different points -- no type, no instance, no event, and so on -- so an
        /// integration that did not work was indistinguishable from one that was switched off, from one whose
        /// messages were all being filtered, and from a mod that had simply changed a name. That is not a thing
        /// to hand to somebody else to test. One line at startup now says which it is.
        ///
        /// <c>Log.Message</c> rather than a warning on the success path, and a warning only when something an
        /// author would want to know about is actually wrong.
        /// </summary>
        private static void Subscribe()
        {
            clientType = AccessTools.TypeByName("PhinixClient.Client");

            if (clientType == null)
            {
                Stood("Phinix is loaded but its PhinixClient.Client type could not be found. It may have been "
                      + "renamed in a newer version.");

                return;
            }

            instanceField = AccessTools.Field(clientType, "Instance");
            uuidProperty = AccessTools.Property(clientType, "Uuid");
            settingsProperty = AccessTools.Property(clientType, "Settings");

            object client = instanceField?.GetValue(null);

            if (client == null)
            {
                Stood("Phinix's Client.Instance was null when this tried to subscribe.");

                return;
            }

            object settings = settingsProperty?.GetValue(client);

            if (settings != null)
                blockedUsersField = AccessTools.Field(settings.GetType(), "BlockedUsers");

            EventInfo chatEvent = clientType.GetEvent("OnChatMessageReceived",
                BindingFlags.Public | BindingFlags.Instance);

            if (chatEvent?.EventHandlerType == null)
            {
                Stood("Phinix's Client.OnChatMessageReceived event could not be found.");

                return;
            }

            // The event is EventHandler<UIChatMessageEventArgs> and that type cannot be named here, so the
            // handler takes object. The CLR allows it: a delegate may bind a method whose parameters are base
            // types of its own, which is exactly this case.
            MethodInfo handler = AccessTools.Method(typeof(PhinixIntegration), nameof(Received));
            Delegate bound = Delegate.CreateDelegate(chatEvent.EventHandlerType, handler);

            chatEvent.AddEventHandler(client, bound);

            subscribed = true;

            UIDebug.Log("Phinix detected; incoming chat messages will raise a notification.");
        }

        /// <summary>
        /// Reports that the integration is not running, and why.
        ///
        /// <b>Behind the debug logging setting, like the success line.</b> A player who has Phinix and this mod
        /// and is not troubleshooting anything does not need a warning on every launch about a feature they may
        /// never have looked for -- and a warning that appears for everybody is one people learn to scroll past,
        /// which costs it exactly the attention it was added to get. It is on when somebody is actually looking.
        /// </summary>
        private static void Stood(string reason)
        {
            UIDebug.Warning("Phinix chat notifications are not active. " + reason
                            + " Phinix itself is unaffected.");
        }

        /// <summary>
        /// Phinix's chat event. <b>Runs on their network thread</b>, so it does nothing but decide and queue.
        ///
        /// Public because it is bound as a delegate by reflection; nothing else calls it.
        /// </summary>
        public static void Received(object sender, object args)
        {
            // This is a callback handed to another mod, on a thread this mod does not own, so an escape here
            // would surface as a fault inside Phinix's networking. Guarded for the same reason UIGuard.Wrap
            // exists.
            UIGuard.Try("Integrations.PhinixMessage", () => Handle(args),
                "One incoming chat message was not announced.");
        }

        private static void Handle(object args)
        {
            // Every decision below can silently produce no notification, and the whole set of them looks
            // identical from outside: nothing happened. With debug logging on, the log says which one it was.
            if (!Wanted)
            {
                UIDebug.Log("Phinix chat message ignored: notifications are switched off in settings.");

                return;
            }

            if (args == null)
                return;

            object message = MessageOf(args);

            if (message == null)
            {
                UIDebug.Warning("Phinix chat message had no readable Message member on "
                                + args.GetType().FullName + ".");

                return;
            }

            string senderUuid = Read<string>(message, "SenderUuid");

            // Our own message coming back from the server. Announcing it would mean a notification every time
            // the player pressed enter.
            if (!senderUuid.NullOrEmpty() && senderUuid == OwnUuid())
            {
                UIDebug.Log("Phinix chat message ignored: it is our own.");

                return;
            }

            if (IsBlocked(senderUuid))
            {
                UIDebug.Log("Phinix chat message ignored: the sender is blocked.");

                return;
            }

            string body = Read<string>(message, "Message");

            if (body.NullOrEmpty())
            {
                UIDebug.Warning("Phinix chat message had no readable text.");

                return;
            }

            object user = Read<object>(message, "User");
            string name = user != null ? Read<string>(user, "DisplayName") : null;

            if (name.NullOrEmpty())
                name = "Someone";

            lock (Lock)
                pending.Add(Plain(name) + ": " + Plain(body));
        }

        /// <summary>
        /// Raises whatever has queued up. Main thread only; called from <see cref="PhinixChatPump"/>.
        /// </summary>
        internal static void Drain()
        {
            if (!subscribed)
                return;

            List<string> ready;

            lock (Lock)
            {
                if (pending.Count == 0)
                    return;

                ready = new List<string>(pending);
                pending.Clear();
            }

            // Dropped rather than held while the chat tab is open: the message is already on screen in the tab
            // the player is looking at, and queuing it to announce later would mean a pile of notifications for
            // messages they have already read the moment they switch away.
            if (ChatTabOpen())
            {
                UIDebug.Log("Phinix chat notification(s) skipped: the chat tab is open.");

                return;
            }

            foreach (string text in ready)
            {
                UIGuard.Try("Integrations.PhinixNotify",
                    () => Messages.Message(text, MessageTypeDefOf.SilentInput, false),
                    "One chat notification was not shown.");
            }
        }

        /// <summary>
        /// Whether the player is already looking at the chat.
        ///
        /// <c>Chat</c> is the defName of the main button Phinix adds. A notification for a message that is
        /// already visible is noise, and this is the one check that stops the feature being annoying to anybody
        /// who actually uses the chat window.
        /// </summary>
        private static bool ChatTabOpen()
        {
            return UIGuard.Try("Integrations.ChatTabCheck",
                () => Find.MainTabsRoot?.OpenTab?.defName == "Chat", false, null);
        }

        private static string OwnUuid()
        {
            object client = instanceField?.GetValue(null);

            return client == null ? null : uuidProperty?.GetValue(client) as string;
        }

        private static bool IsBlocked(string uuid)
        {
            if (uuid.NullOrEmpty() || blockedUsersField == null)
                return false;

            object client = instanceField?.GetValue(null);
            object settings = client == null ? null : settingsProperty?.GetValue(client);
            System.Collections.IEnumerable blocked =
                settings == null ? null : blockedUsersField.GetValue(settings) as System.Collections.IEnumerable;

            if (blocked == null)
                return false;

            foreach (object entry in blocked)
            {
                if (uuid.Equals(entry as string, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        /// <summary>The message object off the event args, resolved once and then reused.</summary>
        private static object MessageOf(object args)
        {
            if (messageProperty == null || messageProperty.DeclaringType != args.GetType())
            {
                // A field on their type rather than a property, so both are tried. Cached against the type it was
                // found on, since the args type is the same object every time.
                messageProperty = AccessTools.Property(args.GetType(), "Message");
            }

            if (messageProperty != null)
                return messageProperty.GetValue(args);

            FieldInfo field = AccessTools.Field(args.GetType(), "Message");

            return field?.GetValue(args);
        }

        private static T Read<T>(object target, string member) where T : class
        {
            if (target == null)
                return null;

            FieldInfo field = AccessTools.Field(target.GetType(), member);

            if (field != null)
                return field.GetValue(target) as T;

            PropertyInfo property = AccessTools.Property(target.GetType(), member);

            return property?.GetValue(target) as T;
        }

        /// <summary>
        /// Strips markup and caps the length.
        ///
        /// <b>This is text another player typed, on another machine, arriving over a network.</b> RimWorld's
        /// labels render rich text, so a remote user could put colour tags, size tags or an unclosed tag into a
        /// notification drawn in this mod's UI. Phinix has the same consideration and offers the player a switch
        /// for it; a notification card is not the place to honour that switch, so markup is simply removed.
        ///
        /// Written out rather than a regular expression: this runs per message on a networked path, and the whole
        /// job is "drop everything between angle brackets".
        /// </summary>
        private static string Plain(string text)
        {
            if (text.NullOrEmpty())
                return string.Empty;

            StringBuilder clean = new StringBuilder(text.Length);
            bool inTag = false;

            foreach (char c in text)
            {
                if (c == '<')
                {
                    inTag = true;
                }
                else if (c == '>')
                {
                    inTag = false;
                }
                else if (!inTag)
                {
                    // Newlines would turn a one-line card into a broken one, so they become spaces.
                    clean.Append(c == '\n' || c == '\r' ? ' ' : c);
                }

                if (clean.Length >= MaxBody)
                {
                    clean.Append("...");

                    break;
                }
            }

            return clean.ToString().Trim();
        }
    }

    /// <summary>
    /// Moves queued chat notifications onto the main thread.
    ///
    /// <b>Phinix raises its chat event from its networking thread</b>, and everything <c>Messages.Message</c>
    /// touches -- the live message list, the archive, the sound system -- is main thread only. Phinix has the
    /// same constraint and solves it the same way for its own sounds. So the handler queues and this drains.
    ///
    /// <b>A patch on <c>UIRootUpdate</c> rather than a GameComponent, and the difference is a bug.</b> The first
    /// version used <c>GameComponentUpdate</c>, which is tidy, idiomatic, and only runs while a game is loaded.
    /// Phinix chat does not require a game -- it connects and receives at the main menu perfectly happily, and
    /// RimWorld draws messages there too -- so a notification that arrived outside a colony was queued and never
    /// shown. <c>UIRoot.UIRootUpdate</c> runs on the main thread every frame in both the menu and play, which is
    /// the actual requirement.
    ///
    /// The base method is patched rather than each subclass: <c>UIRoot_Play</c> and <c>UIRoot_Entry</c> both call
    /// it, so one patch covers both, and draining twice would be harmless anyway since the queue is taken whole
    /// under a lock.
    /// </summary>
    [HarmonyPatch(typeof(UIRoot), nameof(UIRoot.UIRootUpdate))]
    public static class Patch_UIRootUpdate_PhinixChat
    {
        public static void Postfix()
        {
            UIGuard.Try("Integrations.PhinixPump", PhinixIntegration.Drain,
                "Chat notifications are not shown for the rest of the session.");
        }
    }

    /// <summary>
    /// Subscribes to Phinix once every mod has been constructed.
    ///
    /// Static constructors marked this way run after the def database is built, which is well after every mod's
    /// own constructor -- so <c>Client.Instance</c> exists by now whatever the load order was.
    ///
    /// Written out rather than through <c>UIGuard.Try</c> because a static constructor that throws leaves the CLR
    /// marking the type as failed, and every later touch of it throws instead of returning. Same shape as
    /// <c>NotificationIcons</c> and <c>UISliderSkin</c>.
    /// </summary>
    [StaticConstructorOnStartup]
    internal static class PhinixIntegrationStartup
    {
        static PhinixIntegrationStartup()
        {
            try
            {
                PhinixIntegration.Hook();
            }
            catch (Exception ex)
            {
                UIGuard.Report("Integrations.PhinixStartup", ex,
                    "Incoming Phinix chat messages are not announced.");
            }
        }
    }
}
