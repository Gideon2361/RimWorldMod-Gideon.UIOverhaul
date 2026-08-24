using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using Gideon.UIFramework.Helpers;
using Verse;

namespace Gideon.UIOverhaul.Features.Music
{
    /// <summary>
    /// Reading the player's filesystem, where being refused is normal.
    ///
    /// <b>Why this is not just <c>UIGuard.Try</c> around a directory listing.</b> A guard reports what it
    /// catches, and rightly: an exception out of our own code is a fault worth a log entry. But a browser
    /// pointed at the root of a Windows drive walks straight into <c>C:\PerfLogs</c>, <c>System Volume
    /// Information</c>, <c>$Recycle.Bin</c> and whatever else the account cannot open, and none of those are
    /// faults. They are the operating system answering the question correctly. Guarding them produced a stack
    /// trace in the player's log for the ordinary act of opening a drive.
    ///
    /// <b>So the expected refusals are caught by name and swallowed.</b> Access denied, a folder that has gone,
    /// a path past Windows' length limit, a drive that is not ready -- each one means "there is nothing here to
    /// list", the caller gets an empty result, and the browser carries on. Anything else still goes to
    /// <see cref="UIGuard"/>, because an exception nobody predicted is still a fault.
    ///
    /// <b>The path is recorded for the debug log, once each.</b> Asked for on 2026-08-23: the refusal itself is
    /// silent, but with debug logging switched on the path that was blocked is worth having, because "the folder
    /// I wanted is missing from the tree" is otherwise unanswerable. Once per path per session -- the tree
    /// relists an open folder every frame, so a warning per call would be a warning per frame.
    /// </summary>
    internal static class MusicFolders
    {
        private static readonly string[] Nothing = new string[0];

        /// <summary>Paths already mentioned in the debug log, so a refusal is reported once and not per frame.</summary>
        private static readonly HashSet<string> reported = new HashSet<string>();

        /// <summary>The files directly in a folder, or nothing if it cannot be read.</summary>
        internal static string[] Files(string path)
        {
            if (path.NullOrEmpty())
                return Nothing;

            return Read("Music.ReadFiles", path, () => Directory.GetFiles(path));
        }

        /// <summary>The subfolders of a folder, or nothing if it cannot be read.</summary>
        internal static string[] Directories(string path)
        {
            if (path.NullOrEmpty())
                return Nothing;

            return Read("Music.ReadDirectories", path, () => Directory.GetDirectories(path));
        }

        /// <summary>
        /// A path's attributes, or <c>Normal</c> if they cannot be read.
        ///
        /// Normal rather than Hidden on failure, so a folder whose attributes are unreadable is still offered.
        /// The listing inside it will fail harmlessly if it is genuinely unreachable, where hiding it would take
        /// away a folder that might have been fine.
        /// </summary>
        internal static FileAttributes Attributes(string path)
        {
            if (path.NullOrEmpty())
                return FileAttributes.Normal;

            try
            {
                return File.GetAttributes(path);
            }
            catch (UnauthorizedAccessException)
            {
                Note(path, "access is denied");
            }
            catch (SecurityException)
            {
                Note(path, "access is denied");
            }
            catch (DirectoryNotFoundException)
            {
                Note(path, "it is no longer there");
            }
            catch (FileNotFoundException)
            {
                Note(path, "it is no longer there");
            }
            catch (PathTooLongException)
            {
                Note(path, "its path is too long");
            }
            catch (IOException)
            {
                Note(path, "it could not be read");
            }
            catch (Exception ex)
            {
                UIGuard.Report("Music.ReadAttributes", ex, null);
            }

            return FileAttributes.Normal;
        }

        /// <summary>A file's size in bytes, or -1 when it cannot be read.</summary>
        internal static long Length(string path)
        {
            if (path.NullOrEmpty())
                return -1L;

            try
            {
                return new FileInfo(path).Length;
            }
            catch (UnauthorizedAccessException)
            {
                Note(path, "access is denied");
            }
            catch (SecurityException)
            {
                Note(path, "access is denied");
            }
            catch (FileNotFoundException)
            {
                Note(path, "it is no longer there");
            }
            catch (DirectoryNotFoundException)
            {
                Note(path, "it is no longer there");
            }
            catch (PathTooLongException)
            {
                Note(path, "its path is too long");
            }
            catch (IOException)
            {
                Note(path, "it could not be read");
            }
            catch (Exception ex)
            {
                UIGuard.Report("Music.ReadLength", ex, null);
            }

            return -1L;
        }

        /// <summary>Whether a folder exists and we are allowed to look, both answered without throwing.</summary>
        internal static bool Exists(string path)
        {
            if (path.NullOrEmpty())
                return false;

            try
            {
                return Directory.Exists(path);
            }
            catch (Exception ex)
            {
                // Directory.Exists is documented not to throw, and returns false for anything it cannot reach.
                // Caught anyway rather than trusted: this is called against drive letters, and a drive that is
                // half mounted is exactly the case documentation does not cover.
                UIGuard.Report("Music.FolderExists", ex, null);

                return false;
            }
        }

        /// <summary>
        /// Runs a listing, turning the refusals a filesystem browser must expect into an empty result.
        ///
        /// The derived exceptions are listed before <see cref="IOException"/> deliberately: both
        /// <c>DirectoryNotFoundException</c> and <c>PathTooLongException</c> inherit from it, and catching the
        /// base first would swallow them into the wrong message.
        /// </summary>
        private static string[] Read(string site, string path, Func<string[]> body)
        {
            try
            {
                return body();
            }
            catch (UnauthorizedAccessException)
            {
                Note(path, "access is denied");
            }
            catch (SecurityException)
            {
                Note(path, "access is denied");
            }
            catch (DirectoryNotFoundException)
            {
                Note(path, "it is no longer there");
            }
            catch (PathTooLongException)
            {
                Note(path, "its path is too long");
            }
            catch (IOException)
            {
                // A drive with no disc in it, a network path that has dropped, a device that is not ready.
                Note(path, "it could not be read");
            }
            catch (Exception ex)
            {
                UIGuard.Report(site, ex,
                    "That folder was skipped. The rest of the music browser still works.");
            }

            return Nothing;
        }

        /// <summary>
        /// Records a folder we were refused, for the debug log only.
        ///
        /// Guarded, because this runs on the failure path of something already going wrong and a throw from the
        /// note would turn a folder we could not read into an exception we did not expect.
        /// </summary>
        private static void Note(string path, string because)
        {
            UIGuard.Try("Music.NoteBlockedFolder", () =>
            {
                if (!reported.Add(path))
                    return;

                UIDebug.Warning("Skipped " + path + " while browsing for music: " + because + ".");
            }, null);
        }
    }
}
