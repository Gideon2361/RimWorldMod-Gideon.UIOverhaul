using System;
using System.Collections.Generic;
using Gideon.UIFramework.Helpers;
using Gideon.UIOverhaul.Features.Options;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>
    /// Playback: what is on, what is next, and who is choosing.
    ///
    /// <b>Two modes, and only one of them touches the game.</b> With the source set to
    /// <see cref="MusicStore.SourceGame"/> -- which is where a new install starts -- this class chooses nothing,
    /// patches nothing and merely reads what vanilla is doing so the strip has something to say. Pick any other
    /// source and it takes over: vanilla's picker is skipped and the queue here decides.
    ///
    /// <b>It borrows RimWorld's audio source rather than making one.</b> That source is parented into the sound
    /// root's camera container, is bypassed past effects and reverb, and is what <c>Prefs.VolumeMusic</c> and the
    /// game's own fade handling already act on. A second source of ours would be a second thing to keep in step
    /// with all of that, and the first bug would be music playing over music.
    ///
    /// <b>Every frame comes from the manager we patched.</b> <see cref="Tick"/> is called by the prefix on
    /// <c>MusicUpdate</c>, so there is no MonoBehaviour, no coroutine and nothing to shut down: when the game
    /// stops calling its music manager we stop being called too.
    ///
    /// <b>Nothing here fades.</b> A track change is a cut. Crossfading means two audio sources and a mix, and
    /// vanilla only fades on its own transitions, which are the thing this mode replaces.
    /// </summary>
    internal static class MusicEngine
    {
        /// <summary>
        /// Reads the private audio source out of the in-game music manager.
        ///
        /// Field access rather than a copy: the manager creates the source lazily and may in principle replace
        /// it, and a reference cached once would then be pointing at a dead GameObject.
        /// </summary>
        private static readonly AccessTools.FieldRef<MusicManagerPlay, AudioSource> PlayManagerSource =
            ResolveFieldRef<MusicManagerPlay>("audioSource");

        private static readonly AccessTools.FieldRef<MusicManagerEntry, AudioSource> EntrySource =
            ResolveFieldRef<MusicManagerEntry>("audioSource");

        /// <summary>
        /// Vanilla's two clocks, so pausing its music does not cost it the rest of the song.
        ///
        /// Both are <c>Time.time</c> stamps: one says when the current song ends, the other when the next should
        /// begin. Skipping <c>MusicUpdate</c> while paused leaves them in the past, so vanilla's first frame back
        /// would decide the song had finished and cut to another. Pushed forward by however long the pause lasted
        /// instead.
        /// </summary>
        private static readonly AccessTools.FieldRef<MusicManagerPlay, float> SongEndTime =
            ResolveFloatRef("songEndTime");

        private static readonly AccessTools.FieldRef<MusicManagerPlay, float> NextSongStartTime =
            ResolveFloatRef("nextSongStartTime");

        /// <summary>Track ids in play order for the current source.</summary>
        private static readonly List<string> queue = new List<string>();

        /// <summary>What has actually played, newest last, so Previous means previous rather than random.</summary>
        private static readonly List<string> history = new List<string>();

        /// <summary>Which source <see cref="queue"/> was built for, so it is rebuilt when that changes.</summary>
        private static string queueSource;

        private static bool queueShuffled;

        private static int index = -1;

        private static MusicClipRequest request;

        private static MusicTrack current;

        private static bool paused;

        private static float pausedAt;

        /// <summary>When the current track started, so a clip still spinning up is not mistaken for a finished one.</summary>
        private static float startedAt;

        /// <summary>The last thing that went wrong, shown on the strip until something plays.</summary>
        private static string problem;

        /// <summary>Whether the player has run out of queue with repeat off.</summary>
        private static bool stopped;

        /// <summary>
        /// Whether the whole feature is switched on: the setting says so and no other music mod is loaded.
        ///
        /// Read every frame rather than cached, because the setting is a checkbox the player can turn off while
        /// the game is running and the answer has to change with it.
        /// </summary>
        internal static bool Enabled
        {
            get
            {
                return UIGuard.Try("Music.Enabled", () =>
                {
                    UIOverhaulSettingsFile settings = UIOverhaulSettingsFile.Current;

                    return (settings == null || settings.musicPlayer) && !MusicRivals.Any;
                }, false, null);
            }
        }

        /// <summary>Whether we are choosing the music, as opposed to watching the game choose it.</summary>
        internal static bool Intercepting
        {
            get { return Enabled && MusicStore.Source != MusicStore.SourceGame; }
        }

        internal static MusicTrack NowPlaying
        {
            get
            {
                if (Intercepting)
                    return current;

                SongDef song = UIGuard.Try("Music.VanillaSong",
                    () => Current.ProgramState == ProgramState.Playing && Find.MusicManagerPlay != null
                        ? Find.MusicManagerPlay.CurrentSong
                        : null, null, null);

                return song != null ? MusicLibrary.Track("def:" + song.defName) : null;
            }
        }

        internal static bool Paused => paused;

        /// <summary>True when the queue has run out and repeat is off.</summary>
        internal static bool Stopped => Intercepting && stopped;

        internal static bool Loading => request != null && !request.Done;

        internal static string Problem => problem;

        /// <summary>
        /// Seconds until vanilla starts its next song, or zero when that is not what is happening.
        ///
        /// Vanilla's gap is eighty five to a hundred and five seconds in peacetime, which is a long silence to
        /// sit through with nothing on screen saying it is deliberate. Our own playlists play back to back and
        /// never show this.
        /// </summary>
        internal static float SilenceRemaining
        {
            get
            {
                if (Intercepting)
                    return 0f;

                return UIGuard.Try("Music.SilenceRemaining", () =>
                {
                    if (Current.ProgramState != ProgramState.Playing)
                        return 0f;

                    MusicManagerPlay manager = Find.MusicManagerPlay;

                    if (manager == null || manager.IsPlaying)
                        return 0f;

                    return Mathf.Max(0f, manager.NextSongTimer);
                }, 0f, null);
            }
        }

        internal static float Position
        {
            get
            {
                return UIGuard.Try("Music.Position", () =>
                {
                    AudioSource source = Source();

                    return source != null && source.clip != null ? source.time : 0f;
                }, 0f, null);
            }
        }

        internal static float Duration
        {
            get
            {
                return UIGuard.Try("Music.Duration", () =>
                {
                    AudioSource source = Source();

                    return source != null && source.clip != null ? source.clip.length : 0f;
                }, 0f, null);
            }
        }

        /// <summary>Whether the seek bar can be dragged. See <see cref="Seek"/> for why it cannot always.</summary>
        internal static bool CanSeek => Intercepting && current != null && !Loading;

        // -------------------------------------------------------------------------------------------
        // The frame
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Called by the prefix on the in-game music manager. Returns true when vanilla should be skipped.
        ///
        /// <b>False on the first frame, always.</b> <c>MusicUpdate</c> is where the manager creates its audio
        /// source, so skipping it before that has happened would leave nothing to play through. Vanilla gets that
        /// one frame -- and starts one song, which we replace immediately -- rather than us reflecting into a
        /// private initializer.
        /// </summary>
        internal static bool Tick()
        {
            return UIGuard.Try("Music.Tick", () =>
            {
                if (!Intercepting)
                {
                    // Still ours to do while the game is choosing: hold the pause, if the player asked for one.
                    return HoldVanillaPause();
                }

                AudioSource source = Source();

                if (source == null)
                    return false;

                Advance(source);
                ApplyVolume(source);
                Ambience(source);

                return true;
            }, false, "The music player stood down for this frame; the game is choosing instead.");
        }

        /// <summary>
        /// The same for the main menu, where a different manager plays one looping song.
        ///
        /// Worth supporting rather than skipping: the menu is where somebody sets a library up before loading
        /// anything, and a player that goes quiet the moment they leave a colony would look broken.
        /// </summary>
        internal static bool TickEntry()
        {
            return UIGuard.Try("Music.TickEntry", () =>
            {
                if (!Intercepting)
                    return false;

                AudioSource source = Source();

                if (source == null)
                    return false;

                // Vanilla's entry source loops one song forever. Ours moves on at the end of each track, so the
                // flag has to come off or nothing would ever finish.
                source.loop = false;

                Advance(source);
                ApplyVolume(source);

                return true;
            }, false, "The music player stood down for this frame; the menu music is the game's own.");
        }

        /// <summary>
        /// Starts, finishes and moves between tracks.
        ///
        /// The order matters: a request that has just failed is dealt with before anything asks whether the
        /// source has stopped, so a broken file skips to the next track rather than counting as the end of one.
        /// </summary>
        private static void Advance(AudioSource source)
        {
            if (request != null && !request.Started)
            {
                MusicClips.Poll(request);

                if (!request.Done)
                    return;

                if (request.Failed)
                {
                    problem = request.Problem;

                    MusicTrack failed = request.Track;

                    if (failed != null)
                        failed.Missing = failed.Kind == MusicTrackKind.File;

                    MusicClips.Release(request);
                    request = null;
                    current = null;

                    // One step, not a loop. A source where every file is missing would otherwise spin through
                    // hundreds of failures inside one frame; a track per frame gets to the same place without
                    // stalling the game, and each failure is recorded on its own row.
                    Step(1, source);

                    return;
                }

                Begin(source, request);

                return;
            }

            if (paused || stopped)
                return;

            if (current == null)
            {
                Step(1, source);

                return;
            }

            // Not believed for the first half second of a track, and this is not a precaution. A streamed clip
            // can report itself as not playing for a frame or two after Play, so taking that at face value would
            // read as the track having finished -- and the whole queue would tear through itself in half a
            // second, which is not a subtle bug but is a mystifying one.
            if (Time.time - startedAt < 0.5f)
                return;

            if (!source.isPlaying)
                Step(MusicStore.Repeat == MusicRepeat.One ? 0 : 1, source);
        }

        /// <summary>Hands a loaded clip to the source and starts it.</summary>
        private static void Begin(AudioSource source, MusicClipRequest ready)
        {
            // First, so nothing below can leave this unset and put us back here next frame.
            ready.Started = true;

            current = ready.Track;
            problem = null;

            source.clip = ready.Clip;
            source.loop = false;
            source.spatialBlend = 0f;
            source.time = 0f;
            source.volume = Volume();
            source.Play();

            startedAt = Time.time;

            if (current != null)
            {
                if (current.Kind == MusicTrackKind.File && ready.Clip != null && ready.Clip.length > 0f)
                {
                    current.Length = ready.Clip.length;
                    MusicStore.LearnLength(current.Id, ready.Clip.length);
                }

                current.Missing = false;

                if (history.Count == 0 || history[history.Count - 1] != current.Id)
                    history.Add(current.Id);

                // Bounded, because a long session would otherwise grow this forever. Twenty is far more than
                // anybody presses Previous.
                while (history.Count > 20)
                    history.RemoveAt(0);
            }

            // The request stays, holding the clip, until the next track replaces it. Releasing it here would
            // destroy the clip currently playing.
        }

        /// <summary>
        /// Moves the queue position and starts loading whatever it lands on.
        /// </summary>
        /// <param name="offset">1 for the next track, -1 for the previous, 0 to replay this one.</param>
        private static void Step(int offset, AudioSource source)
        {
            EnsureQueue();

            if (queue.Count == 0)
            {
                Silence(source);
                stopped = true;
                problem = "Nothing in this source can be played.";

                return;
            }

            if (offset != 0 || index < 0)
            {
                int next = index + offset;

                if (next >= queue.Count)
                {
                    if (MusicStore.Repeat != MusicRepeat.All)
                    {
                        Silence(source);
                        stopped = true;

                        return;
                    }

                    next = 0;
                }

                if (next < 0)
                    next = MusicStore.Repeat == MusicRepeat.All ? queue.Count - 1 : 0;

                index = next;
            }

            stopped = false;

            Load(MusicLibrary.Track(queue[index]));
        }

        private static void Load(MusicTrack track)
        {
            // Stopped before the old clip is let go of, because releasing it destroys it, and destroying a clip
            // an audio source is still reading from is asking Unity to read freed memory.
            UIGuard.Try("Music.StopBeforeLoad", () =>
            {
                AudioSource playing = Source();

                if (playing != null && playing.isPlaying)
                    playing.Stop();
            }, null);

            MusicClips.Release(request);

            request = MusicClips.Begin(track);

            // A song's clip is ready on the spot, so the first frame of it can start now rather than one frame
            // late. Everything else comes back through Advance.
            if (request.Done && !request.Failed)
            {
                AudioSource source = Source();

                if (source != null)
                    Begin(source, request);
            }
        }

        private static void Silence(AudioSource source)
        {
            MusicClips.Release(request);
            request = null;
            current = null;

            if (source == null)
                return;

            UIGuard.Try("Music.Silence", () =>
            {
                source.Stop();
                source.clip = null;
            }, null);
        }

        // -------------------------------------------------------------------------------------------
        // Volume and ambience
        // -------------------------------------------------------------------------------------------

        private static float Volume()
        {
            return UIGuard.Try("Music.Volume",
                () => AudioSourceUtility.GetSanitizedVolume(Prefs.VolumeMusic, "Gideon.Music"), 0.5f, null);
        }

        private static void ApplyVolume(AudioSource source)
        {
            // Every frame, because the player can drag RimWorld's own music slider while this is playing and the
            // one thing worse than not honouring that slider is honouring it a minute later.
            source.volume = Volume();
        }

        /// <summary>
        /// Keeps the subtle ambience in step, which vanilla's <c>MusicUpdate</c> would have done.
        ///
        /// Reproduced rather than reflected into: it is four lines and a public field, where the private method
        /// around it also reads state we have taken over. Getting this wrong is not subtle -- the ambient bed
        /// under the map would either never come back or never go away.
        /// </summary>
        private static void Ambience(AudioSource source)
        {
            UIGuard.Try("Music.Ambience", () =>
            {
                MusicManagerPlay manager = Find.MusicManagerPlay;

                if (manager == null)
                    return;

                bool audible = source.isPlaying && source.volume > 0.001f;
                float step = Time.deltaTime * 0.1f;

                manager.subtleAmbienceSoundVolumeMultiplier = Mathf.Clamp01(
                    manager.subtleAmbienceSoundVolumeMultiplier + (audible ? -step : step));
            }, null);
        }

        // -------------------------------------------------------------------------------------------
        // Transport
        // -------------------------------------------------------------------------------------------

        internal static void TogglePause()
        {
            UIGuard.Try("Music.TogglePause", () =>
            {
                AudioSource source = Source();

                if (source == null)
                    return;

                paused = !paused;

                if (paused)
                {
                    pausedAt = Time.time;
                    source.Pause();

                    return;
                }

                source.UnPause();

                // Only the game's own mode has clocks to repair. Ours has none: a track ends when the source
                // says it has stopped, which a pause does not affect.
                if (!Intercepting)
                    ReleaseVanillaPause();
            }, "The pause button did nothing.");
        }

        /// <summary>
        /// Plays the next thing, whoever is choosing.
        ///
        /// In the game's own mode this is vanilla's <c>StartNewSong</c>, which picks by its own rules and starts
        /// at once -- the same thing the player would get by waiting out the gap.
        /// </summary>
        internal static void Next()
        {
            UIGuard.Try("Music.Next", () =>
            {
                if (!Intercepting)
                {
                    if (Current.ProgramState == ProgramState.Playing && Find.MusicManagerPlay != null)
                        Find.MusicManagerPlay.StartNewSong();

                    return;
                }

                paused = false;
                Step(1, Source());
            }, "The skip button did nothing.");
        }

        internal static void Previous()
        {
            UIGuard.Try("Music.Previous", () =>
            {
                if (!Intercepting)
                {
                    PreviousInVanilla();

                    return;
                }

                paused = false;

                // Restart rather than step back when the track has barely begun -- the same behaviour every
                // music player has, and the reason is that Previous three seconds in means "I missed the start".
                if (Position > 3f)
                {
                    Step(0, Source());

                    return;
                }

                Step(-1, Source());
            }, "The back button did nothing.");
        }

        /// <summary>
        /// Steps back through what vanilla has played, using our own history.
        ///
        /// Vanilla keeps a queue of recent songs to avoid repeats and does not expose it, so the history here is
        /// built by watching what its manager reports as current. Without it there would be nothing to go back
        /// to and the button would have to be dead.
        /// </summary>
        private static void PreviousInVanilla()
        {
            if (Current.ProgramState != ProgramState.Playing)
                return;

            MusicManagerPlay manager = Find.MusicManagerPlay;

            if (manager == null || history.Count < 2)
                return;

            string previous = history[history.Count - 2];
            MusicTrack track = MusicLibrary.Track(previous);

            if (track == null || track.Kind != MusicTrackKind.Song || track.Song == null)
                return;

            history.RemoveAt(history.Count - 1);
            manager.ForcePlaySong(track.Song, false);
        }

        /// <summary>
        /// Jumps within the current track.
        ///
        /// <b>Only while we are choosing.</b> In the game's own mode the manager holds a <c>Time.time</c> stamp
        /// for when the song ends, so moving the playhead would either cut the song off early or leave it running
        /// past the end into silence. The bar is drawn without a handle there rather than being drawn and then
        /// ignoring the drag.
        /// </summary>
        internal static void Seek(float seconds)
        {
            if (!CanSeek)
                return;

            UIGuard.Try("Music.Seek", () =>
            {
                AudioSource source = Source();

                if (source == null || source.clip == null)
                    return;

                source.time = Mathf.Clamp(seconds, 0f, Mathf.Max(0f, source.clip.length - 0.05f));
            }, "The track could not be moved to that point.");
        }

        /// <summary>Selects a source and starts playing from the top of it.</summary>
        internal static void PlaySource(string sourceId)
        {
            UIGuard.Try("Music.PlaySource", () =>
            {
                MusicStore.Source = sourceId;

                queueSource = null;
                index = -1;
                paused = false;
                stopped = false;
                problem = null;

                if (sourceId == MusicStore.SourceGame)
                {
                    // Handing back rather than stopping. Vanilla's manager keeps running while we intercept, so
                    // its own clocks are stale; letting it pick immediately is the honest way back.
                    AudioSource handback = Source();

                    Silence(handback);

                    if (Current.ProgramState == ProgramState.Playing)
                    {
                        if (Find.MusicManagerPlay != null)
                            Find.MusicManagerPlay.StartNewSong();

                        return;
                    }

                    // At the menu the manager only restarts whatever clip the source is holding, so handing back
                    // means putting its own song back on the source and restoring the loop we cleared. Without
                    // this, saying "let the game choose" at the main menu would replay our last track once and
                    // then leave the menu silent.
                    RestoreEntrySong(handback);

                    return;
                }

                Step(1, Source());
            }, "The source could not be changed.");
        }

        /// <summary>Plays one track now, switching source to whatever list it was picked from.</summary>
        internal static void PlayTrack(string sourceId, MusicTrack track)
        {
            if (track == null)
                return;

            UIGuard.Try("Music.PlayTrack", () =>
            {
                if (MusicStore.Source != sourceId)
                {
                    MusicStore.Source = sourceId;
                    queueSource = null;
                }

                EnsureQueue();

                int found = queue.IndexOf(track.Id);

                if (found < 0)
                {
                    queue.Add(track.Id);
                    found = queue.Count - 1;
                }

                index = found;
                paused = false;
                stopped = false;
                problem = null;

                Load(track);
            }, "That track could not be started.");
        }

        /// <summary>
        /// Puts the main menu's own looping song back on the audio source.
        ///
        /// Guarded past the def lookup as well as the assignment: <c>SongDefOf.EntrySong</c> is resolved at
        /// startup and a mod could in principle leave it unresolved, and a null clip here would be silence with no
        /// explanation.
        /// </summary>
        private static void RestoreEntrySong(AudioSource source)
        {
            UIGuard.Try("Music.RestoreEntrySong", () =>
            {
                if (source == null || SongDefOf.EntrySong == null || SongDefOf.EntrySong.clip == null)
                    return;

                source.clip = SongDefOf.EntrySong.clip;
                source.loop = true;
                source.volume = Volume();
                source.Play();
            }, "The menu music did not restart. Leaving the menu and coming back fixes it.");
        }

        /// <summary>Called when the library or a playlist changes, so the queue is rebuilt without a restart.</summary>
        internal static void Invalidate()
        {
            queueSource = null;
        }

        // -------------------------------------------------------------------------------------------
        // Queue
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds the play order if the source or the shuffle setting has changed since it was last built.
        ///
        /// The current track keeps its place across a rebuild where it can, so toggling shuffle mid-song does not
        /// restart what is playing.
        /// </summary>
        private static void EnsureQueue()
        {
            string source = MusicStore.Source;
            bool shuffle = MusicStore.Shuffle;

            if (queueSource == source && queueShuffled == shuffle && queue.Count > 0)
                return;

            queueSource = source;
            queueShuffled = shuffle;

            queue.Clear();

            List<MusicTrack> tracks = MusicLibrary.Tracks(source);

            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i] != null && !tracks[i].Missing)
                    queue.Add(tracks[i].Id);
            }

            // Missing files are left out of the queue but stay in the list the player sees. Skipping them at
            // playback would work too and would mean a gap of silence per missing file at the moment it came up.
            if (queue.Count == 0)
            {
                for (int i = 0; i < tracks.Count; i++)
                {
                    if (tracks[i] != null)
                        queue.Add(tracks[i].Id);
                }
            }

            if (shuffle)
                Shuffle();

            index = current != null ? queue.IndexOf(current.Id) : -1;
        }

        /// <summary>
        /// Fisher-Yates, seeded by RimWorld's own Rand.
        ///
        /// Through <c>Rand</c> rather than <c>System.Random</c> so it goes through the same generator as the rest
        /// of the game and cannot be accused of desyncing anything.
        /// </summary>
        private static void Shuffle()
        {
            UIGuard.Try("Music.Shuffle", () =>
            {
                for (int i = queue.Count - 1; i > 0; i--)
                {
                    int swap = Rand.RangeInclusive(0, i);
                    string held = queue[i];

                    queue[i] = queue[swap];
                    queue[swap] = held;
                }
            }, null);
        }

        // -------------------------------------------------------------------------------------------
        // Vanilla mode housekeeping
        // -------------------------------------------------------------------------------------------

        /// <summary>
        /// Keeps a pause honoured while the game is choosing the music.
        ///
        /// Returns true to skip vanilla's update, which is what stops it noticing the song has ended and
        /// starting another over the top of a pause. Its two clocks are pushed forward on the way out so the
        /// paused song resumes with its remaining time intact.
        /// </summary>
        private static bool HoldVanillaPause()
        {
            if (!paused)
            {
                Watch();

                return false;
            }

            AudioSource source = Source();

            if (source == null)
                return false;

            if (source.isPlaying)
                source.Pause();

            return true;
        }

        /// <summary>
        /// Notes what vanilla is playing, so Previous has somewhere to go back to.
        ///
        /// Cheap: a reference comparison against the last thing recorded, every frame.
        /// </summary>
        private static void Watch()
        {
            UIGuard.Try("Music.Watch", () =>
            {
                if (Current.ProgramState != ProgramState.Playing)
                    return;

                MusicManagerPlay manager = Find.MusicManagerPlay;
                SongDef song = manager != null ? manager.CurrentSong : null;

                if (song == null)
                    return;

                string id = "def:" + song.defName;

                if (history.Count > 0 && history[history.Count - 1] == id)
                    return;

                history.Add(id);

                while (history.Count > 20)
                    history.RemoveAt(0);
            }, null);
        }

        /// <summary>
        /// Called when the pause is released in the game's own mode: gives vanilla back the time it lost.
        /// </summary>
        internal static void ReleaseVanillaPause()
        {
            UIGuard.Try("Music.ReleaseVanillaPause", () =>
            {
                if (Current.ProgramState != ProgramState.Playing)
                    return;

                MusicManagerPlay manager = Find.MusicManagerPlay;

                if (manager == null || SongEndTime == null || NextSongStartTime == null)
                    return;

                float held = Time.time - pausedAt;

                if (held <= 0f)
                    return;

                SongEndTime(manager) += held;
                NextSongStartTime(manager) += held;
            }, null);
        }

        // -------------------------------------------------------------------------------------------
        // Plumbing
        // -------------------------------------------------------------------------------------------

        /// <summary>The audio source for whichever manager is running, or null before one exists.</summary>
        private static AudioSource Source()
        {
            return UIGuard.Try("Music.Source", () =>
            {
                if (Current.ProgramState == ProgramState.Playing)
                {
                    if (PlayManagerSource == null || Find.MusicManagerPlay == null)
                        return null;

                    return PlayManagerSource(Find.MusicManagerPlay);
                }

                if (EntrySource == null || !(Current.Root is Root_Entry))
                    return null;

                return EntrySource(Find.MusicManagerEntry);
            }, null, null);
        }

        /// <summary>
        /// Builds a field accessor, or null if the field has moved.
        ///
        /// Null rather than a throw: a private field renamed by a RimWorld update should cost this feature and
        /// nothing else, and every caller already checks.
        /// </summary>
        private static AccessTools.FieldRef<T, AudioSource> ResolveFieldRef<T>(string field)
        {
            return UIGuard.Try("Music.ResolveSourceField",
                () => AccessTools.FieldRefAccess<T, AudioSource>(field), null,
                "The music player cannot reach the game's audio source, so it will leave the music alone.");
        }

        private static AccessTools.FieldRef<MusicManagerPlay, float> ResolveFloatRef(string field)
        {
            return UIGuard.Try("Music.ResolveClockField",
                () => AccessTools.FieldRefAccess<MusicManagerPlay, float>(field), null,
                "Pausing the game's own music will cut the song short when it resumes.");
        }
    }
}
