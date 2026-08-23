using System;
using System.IO;
using Gideon.UIFramework.Helpers;
using NAudio.Wave;
using UnityEngine;
using UnityEngine.Networking;
using Verse;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>
    /// One clip being loaded. Poll it each frame until <see cref="Done"/>.
    ///
    /// A class rather than a coroutine because this mod has no MonoBehaviour of its own and does not want one:
    /// the engine already gets a call every frame from the music manager it has patched, so polling costs nothing
    /// and a GameObject that has to survive scene changes is one more thing to shut down correctly.
    /// </summary>
    internal sealed class MusicClipRequest
    {
        internal MusicTrack Track;

        internal AudioClip Clip;

        internal bool Done;

        /// <summary>Set with <see cref="Problem"/> when the track cannot be played at all.</summary>
        internal bool Failed;

        /// <summary>Short, player-facing, and shown on the track's row. Null while things are going well.</summary>
        internal string Problem;

        /// <summary>Whether the clip is ours to destroy when we are finished with it.</summary>
        internal bool Owned;

        /// <summary>
        /// Set once the engine has handed this clip to the audio source.
        ///
        /// The request outlives that moment on purpose, because it owns the clip that is currently playing and
        /// releasing it would destroy the audio mid-track. So there has to be something separating "ready" from
        /// "already started", and without it the engine restarts the track on every frame: the position never
        /// leaves zero, and the elapsed readout flickers as it is reset and advanced sixty times a second.
        /// </summary>
        internal bool Started;

        // ---- Unity route ----
        internal UnityWebRequest Web;

        // ---- Media Foundation route ----
        internal MediaFoundationReader Reader;

        internal byte[] ReadBuffer;

        internal float[] SampleBuffer;

        internal int Channels;

        internal int WrittenSamples;

        internal int TotalSamples;
    }

    /// <summary>
    /// Turning a track into something an <c>AudioSource</c> can play.
    ///
    /// <b>Three routes, and the reason there is more than one.</b> A mod's song is already a loaded clip and
    /// needs nothing. ogg, wav and mp3 go to Unity's own streaming loader, the same call RimWorld uses for mod
    /// audio, which decodes as it plays and so costs almost no memory. mp4 and m4a cannot go that way at all:
    /// their audio is AAC and Unity has no AAC decoder on the desktop, which is why other music mods for this
    /// game cannot open an m4a.
    ///
    /// <b>What makes the fourth and fifth formats possible is already in the game folder.</b> RimWorld ships
    /// <c>NAudio.dll</c> in its own managed directory, and NAudio's Media Foundation reader hands AAC to
    /// Windows' own decoder. So there is no new dependency, nothing extra in the download, and no second copy of
    /// an audio library to collide with another mod's. It is Windows-only, which is a limit on those two formats
    /// rather than on the feature.
    ///
    /// <b>Decoded a piece at a time on the main thread, not on a worker.</b> A four minute stereo track is about
    /// eighty five megabytes of samples, so decoding it whole into an array and then handing that to Unity would
    /// briefly hold twice that. Instead the clip is allocated up front and filled in with
    /// <c>SetData</c> a chunk per frame, which holds one chunk at a time. Doing it on the main thread also keeps
    /// every Media Foundation call on one thread, which is what its COM objects want, and keeps
    /// <c>AudioClip</c> use where Unity requires it.
    /// </summary>
    internal static class MusicClips
    {
        /// <summary>
        /// Samples decoded per frame, per channel.
        ///
        /// About five seconds of stereo audio at 44.1kHz, which is roughly fifteen milliseconds of work: enough
        /// that a four minute track is ready in under a second, small enough not to drop a frame doing it. The
        /// cost is paid at a track change, where a moment of the previous song still playing hides it.
        /// </summary>
        private const int ChunkSamples = 1 << 18;

        /// <summary>
        /// The longest file we will decode, in seconds.
        ///
        /// Twenty minutes of stereo is about two hundred megabytes once decoded, and that is memory held for as
        /// long as the track is loaded. Past this it is refused with a message rather than quietly making the
        /// game unstable -- somebody who has pointed us at a two hour DJ set should be told, not obeyed.
        /// </summary>
        private const double LengthCeilingSeconds = 20 * 60;

        /// <summary>
        /// Starts loading a track. Never returns null.
        ///
        /// A song is finished on the spot, since its clip is already in memory; the other two routes come back
        /// with <see cref="MusicClipRequest.Done"/> false and want polling.
        /// </summary>
        internal static MusicClipRequest Begin(MusicTrack track)
        {
            MusicClipRequest request = new MusicClipRequest { Track = track };

            if (track == null)
            {
                Fail(request, "There is nothing to play.");

                return request;
            }

            if (track.Kind == MusicTrackKind.Song)
            {
                AudioClip clip = UIGuard.Try("Music.SongClip", () => track.Song != null ? track.Song.clip : null,
                    null, null);

                if (clip == null)
                {
                    Fail(request, "This mod's song did not load.");

                    return request;
                }

                request.Clip = clip;
                request.Owned = false;
                request.Done = true;

                return request;
            }

            bool exists = UIGuard.Try("Music.FileExists", () => File.Exists(track.FilePath), false, null);

            if (!exists)
            {
                Fail(request, "File moved or deleted.");

                return request;
            }

            if (track.NeedsMediaFoundation)
                BeginMediaFoundation(request);
            else
                BeginUnity(request);

            return request;
        }

        /// <summary>Moves a request forward. Safe to call on one that is already done.</summary>
        internal static void Poll(MusicClipRequest request)
        {
            if (request == null || request.Done)
                return;

            if (request.Web != null)
            {
                PollUnity(request);

                return;
            }

            if (request.Reader != null)
                PollMediaFoundation(request);
        }

        /// <summary>
        /// Lets go of everything a request holds, destroying the clip if we made it.
        ///
        /// A mod's clip is never destroyed: it belongs to the def database, and destroying it would take that
        /// song away from vanilla's own player for the rest of the session.
        /// </summary>
        internal static void Release(MusicClipRequest request)
        {
            if (request == null)
                return;

            UIGuard.Try("Music.ReleaseClip", () =>
            {
                if (request.Web != null)
                {
                    request.Web.Dispose();
                    request.Web = null;
                }

                if (request.Reader != null)
                {
                    request.Reader.Dispose();
                    request.Reader = null;
                }

                if (request.Owned && request.Clip != null)
                    UnityEngine.Object.Destroy(request.Clip);

                request.Clip = null;
                request.ReadBuffer = null;
                request.SampleBuffer = null;
            }, null);
        }

        // -------------------------------------------------------------------------------------------
        // ogg, wav, mp3: Unity's own streaming loader
        // -------------------------------------------------------------------------------------------

        private static void BeginUnity(MusicClipRequest request)
        {
            bool started = UIGuard.Try("Music.BeginUnityLoad", () =>
            {
                AudioType type = TypeOf(request.Track.Extension);
                UnityWebRequest web = UnityWebRequestMultimedia.GetAudioClip(FileUri(request.Track.FilePath), type);

                DownloadHandlerAudioClip handler = web.downloadHandler as DownloadHandlerAudioClip;

                // Streamed rather than decoded whole. A five minute mp3 is a hundred megabytes of samples
                // decoded and about eight on disk, and the whole point of handing this to Unity is that it can
                // read it as it plays.
                if (handler != null)
                    handler.streamAudio = true;

                web.SendWebRequest();
                request.Web = web;
            }, "This track could not be opened.");

            if (!started)
                Fail(request, "This track could not be opened.");
        }

        private static void PollUnity(MusicClipRequest request)
        {
            UIGuard.Try("Music.PollUnityLoad", () =>
            {
                if (!request.Web.isDone)
                    return;

                if (request.Web.result != UnityWebRequest.Result.Success)
                {
                    Fail(request, "This file could not be read.");

                    return;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(request.Web);

                if (clip == null)
                {
                    Fail(request, "This file is not audio we can play.");

                    return;
                }

                request.Clip = clip;
                request.Owned = true;
                request.Done = true;
            }, "This track could not be loaded.");
        }

        /// <summary>
        /// A file path as a URI Unity will accept.
        ///
        /// Through <c>Uri</c> rather than by pasting "file://" onto the front, because a path with a space or a
        /// hash in it -- <c>D:\My Music\Track #3.mp3</c> -- has to be escaped and hand-built URIs get that wrong.
        /// The naive form is kept as a fallback rather than failing the load outright.
        /// </summary>
        private static string FileUri(string path)
        {
            return UIGuard.Try("Music.FileUri", () => new Uri(path).AbsoluteUri,
                "file://" + path.Replace("\\", "/"), null);
        }

        private static AudioType TypeOf(string extension)
        {
            if (extension == ".ogg")
                return AudioType.OGGVORBIS;

            if (extension == ".wav")
                return AudioType.WAV;

            return AudioType.MPEG;
        }

        // -------------------------------------------------------------------------------------------
        // mp4, m4a: Media Foundation through NAudio
        // -------------------------------------------------------------------------------------------

        private static void BeginMediaFoundation(MusicClipRequest request)
        {
            bool started = UIGuard.Try("Music.BeginMediaFoundation", () =>
            {
                MediaFoundationReader reader = new MediaFoundationReader(request.Track.FilePath);
                WaveFormat format = reader.WaveFormat;

                if (format == null || format.Channels < 1 || format.SampleRate < 1)
                {
                    reader.Dispose();
                    Fail(request, "This file has no audio track.");

                    return;
                }

                double seconds = reader.TotalTime.TotalSeconds;

                if (seconds <= 0d)
                {
                    reader.Dispose();
                    Fail(request, "This file reports no length.");

                    return;
                }

                if (seconds > LengthCeilingSeconds)
                {
                    reader.Dispose();
                    Fail(request, "Longer than twenty minutes, so it is not loaded.");

                    return;
                }

                int channels = format.Channels;
                int total = (int) (seconds * format.SampleRate);

                if (total < 1)
                {
                    reader.Dispose();
                    Fail(request, "This file is too short to play.");

                    return;
                }

                AudioClip clip = AudioClip.Create(request.Track.Label, total, channels, format.SampleRate, false);

                if (clip == null)
                {
                    reader.Dispose();
                    Fail(request, "A clip for this track could not be made.");

                    return;
                }

                request.Reader = reader;
                request.Clip = clip;
                request.Owned = true;
                request.Channels = channels;
                request.TotalSamples = total;
                request.ReadBuffer = new byte[ChunkSamples * channels * format.BitsPerSample / 8];
                request.SampleBuffer = new float[ChunkSamples * channels];
            }, "This track could not be decoded.");

            if (!started && !request.Failed)
                Fail(request, "This track could not be decoded. Windows may be missing its codec.");
        }

        private static void PollMediaFoundation(MusicClipRequest request)
        {
            bool ok = UIGuard.Try("Music.PollMediaFoundation", () =>
            {
                int read = request.Reader.Read(request.ReadBuffer, 0, request.ReadBuffer.Length);

                if (read <= 0)
                {
                    // The reader ran out before the length it advertised. Whatever was written plays, and the
                    // remainder of the clip is the silence it was allocated with -- which is a better outcome
                    // than refusing a file that is very slightly shorter than its own header claims.
                    Finish(request);

                    return;
                }

                int samples = Convert(request, read);

                if (samples <= 0)
                {
                    Finish(request);

                    return;
                }

                int frames = samples / request.Channels;
                int room = request.TotalSamples - request.WrittenSamples;

                if (frames > room)
                    frames = room;

                if (frames <= 0)
                {
                    Finish(request);

                    return;
                }

                // SetData wants an array whose length is exactly what it is going to write, so a partial final
                // chunk is copied into a right-sized one rather than written from the middle of the big buffer.
                int wanted = frames * request.Channels;

                if (wanted == request.SampleBuffer.Length)
                {
                    request.Clip.SetData(request.SampleBuffer, request.WrittenSamples);
                }
                else
                {
                    float[] tail = new float[wanted];
                    Array.Copy(request.SampleBuffer, tail, wanted);
                    request.Clip.SetData(tail, request.WrittenSamples);
                }

                request.WrittenSamples += frames;

                if (request.WrittenSamples >= request.TotalSamples)
                    Finish(request);
            }, "This track could not be decoded past the point it reached.");

            if (!ok)
                Fail(request, "This track stopped decoding partway through.");
        }

        /// <summary>
        /// Turns the reader's bytes into floats, whatever it handed us.
        ///
        /// Media Foundation gives 16 bit PCM for most files and 32 bit float for some, and NAudio reports which
        /// through the wave format rather than converting. Both are handled, plus the two less common PCM widths,
        /// because a format we cannot convert would otherwise play as noise -- which is a worse failure than
        /// refusing the file.
        /// </summary>
        private static int Convert(MusicClipRequest request, int bytes)
        {
            WaveFormat format = request.Reader.WaveFormat;
            byte[] source = request.ReadBuffer;
            float[] target = request.SampleBuffer;

            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                int count = bytes / 4;

                if (count > target.Length)
                    count = target.Length;

                Buffer.BlockCopy(source, 0, target, 0, count * 4);

                return count;
            }

            if (format.BitsPerSample == 16)
            {
                int count = bytes / 2;

                if (count > target.Length)
                    count = target.Length;

                for (int i = 0; i < count; i++)
                    target[i] = (short) (source[i * 2] | (source[i * 2 + 1] << 8)) / 32768f;

                return count;
            }

            if (format.BitsPerSample == 24)
            {
                int count = bytes / 3;

                if (count > target.Length)
                    count = target.Length;

                for (int i = 0; i < count; i++)
                {
                    int sample = (source[i * 3] << 8) | (source[i * 3 + 1] << 16) | (source[i * 3 + 2] << 24);
                    target[i] = sample / 2147483648f;
                }

                return count;
            }

            if (format.BitsPerSample == 32)
            {
                int count = bytes / 4;

                if (count > target.Length)
                    count = target.Length;

                for (int i = 0; i < count; i++)
                {
                    int sample = source[i * 4] | (source[i * 4 + 1] << 8) | (source[i * 4 + 2] << 16)
                                 | (source[i * 4 + 3] << 24);
                    target[i] = sample / 2147483648f;
                }

                return count;
            }

            Fail(request, "This file's audio format is one we cannot convert.");

            return 0;
        }

        private static void Finish(MusicClipRequest request)
        {
            if (request.Reader != null)
            {
                request.Reader.Dispose();
                request.Reader = null;
            }

            request.ReadBuffer = null;
            request.SampleBuffer = null;
            request.Done = true;
        }

        private static void Fail(MusicClipRequest request, string problem)
        {
            if (request.Reader != null)
            {
                UIGuard.Try("Music.DisposeReader", () => request.Reader.Dispose(), null);
                request.Reader = null;
            }

            if (request.Web != null)
            {
                UIGuard.Try("Music.DisposeWeb", () => request.Web.Dispose(), null);
                request.Web = null;
            }

            if (request.Owned && request.Clip != null)
                UIGuard.Try("Music.DestroyFailedClip", () => UnityEngine.Object.Destroy(request.Clip), null);

            request.Clip = null;
            request.ReadBuffer = null;
            request.SampleBuffer = null;
            request.Failed = true;
            request.Done = true;
            request.Problem = problem;
        }
    }
}
