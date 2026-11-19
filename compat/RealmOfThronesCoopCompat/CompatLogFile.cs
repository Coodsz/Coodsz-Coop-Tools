#nullable disable
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace RealmOfThronesCoopCompat
{
    // Realm of Thrones Coop Compat's own log file, written straight to disk, because the game's
    // rgl log goes through a buffer that a crashed or killed process never flushes. Same helper
    // as Burning Empires Coop Compat, with its own folder.
    //   Client: Documents\Mount and Blade II Bannerlord\Logs\RealmOfThrones.CoopCompat\coopcompat.log
    //   Server: <BANNERLORD_USER_DIR>\logs\RealmOfThrones.CoopCompat\coopcompat.log, or the
    //           client path when the server runs without that variable
    // Past MaxBytes the file becomes coopcompat.old.log (replacing the last one), so the two stay
    // under 2 MB together. No game types, so the launcher tests can run it.
    internal sealed class CompatLogFile : IDisposable
    {
        public const string FolderName = "RealmOfThrones.CoopCompat";
        public const string FileName = "coopcompat.log";
        public const string OldFileName = "coopcompat.old.log";
        public const long MaxBytes = 1_000_000;

        private static readonly object Sync = new object();
        private static CompatLogFile _log;
        private static bool _broken;

        private readonly string _path;
        private readonly long _maxBytes;
        private FileStream _stream;
        private long _size;
        private long _rotateAt;

        // No Documents folder gives null: a relative path would land wherever the game runs from.
        public static string Folder(string userDir, string documents)
        {
            if (!string.IsNullOrWhiteSpace(userDir))
                return Path.Combine(userDir, "logs", FolderName);
            if (string.IsNullOrWhiteSpace(documents) || !Path.IsPathRooted(documents))
                return null;
            return Path.Combine(documents, "Mount and Blade II Bannerlord", "Logs", FolderName);
        }

        // Where the file goes in this process, for the start line. Null when there is nowhere.
        public static string CurrentPath()
        {
            var folder = Folder(Environment.GetEnvironmentVariable("BANNERLORD_USER_DIR"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            return folder == null ? null : Path.Combine(folder, FileName);
        }

        public static void Write(string line)
        {
            if (_broken)
                return;
            lock (Sync)
            {
                if (_log == null)
                {
                    try
                    {
                        var path = CurrentPath();
                        if (path == null)
                        {
                            _broken = true;
                            return;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        var log = new CompatLogFile(path, MaxBytes);
                        log.Append("---- Realm of Thrones Coop Compat started, process " + ProcessId());
                        _log = log;
                    }
                    catch
                    {
                        // No folder or no first line (a read only folder) stops the file.
                        _broken = true;
                        return;
                    }
                }
                // One failed line (the disk full for a moment) does not stop the file.
                try { _log.Append(line); }
                catch { }
            }
        }

        // One stream stays open, shared so a log pack can read it while the game runs. Each line
        // goes to the end of the file, so a second game on this PC writing it does not write over it.
        public CompatLogFile(string path, long maxBytes)
        {
            _path = path;
            _maxBytes = maxBytes;
            _rotateAt = maxBytes;
            Open(FileMode.Append);
        }

        public void Append(string line)
        {
            if (_size >= _rotateAt)
                Rotate();
            var bytes = Encoding.UTF8.GetBytes(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                + "  " + line + Environment.NewLine);
            _stream.Seek(0, SeekOrigin.End);
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
            _size = _stream.Position;
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _stream = null;
        }

        // A full file becomes coopcompat.old.log. When either file is held open without delete
        // sharing, the file starts over instead, so it stays small. Only when even that fails does
        // it keep growing, trying again one MaxBytes later rather than on every line.
        private void Rotate()
        {
            Dispose();
            try
            {
                var old = Path.Combine(Path.GetDirectoryName(_path) ?? "", OldFileName);
                if (File.Exists(old))
                    File.Delete(old);
                File.Move(_path, old);
                Open(FileMode.Append);
                _rotateAt = _maxBytes;
                return;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
            }

            try
            {
                Open(FileMode.Create);
                _rotateAt = _maxBytes;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Open(FileMode.Append);
                _rotateAt = _size + _maxBytes;
            }
        }

        private void Open(FileMode mode)
        {
            _stream = new FileStream(_path, mode, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _size = _stream.Length;
        }

        private static int ProcessId()
        {
            try { return System.Diagnostics.Process.GetCurrentProcess().Id; }
            catch { return 0; }
        }
    }
}
