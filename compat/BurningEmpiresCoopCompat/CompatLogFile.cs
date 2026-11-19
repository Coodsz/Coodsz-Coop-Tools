#nullable disable
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace BurningEmpiresCoopCompat
{
    // Burning Empires Coop Compat's own log file. The game's rgl log is not enough: the engine
    // writes the full log through a buffer that a killed or stopped process never flushes (the
    // live run 3 server log stopped at "Loading Save Game" while its errors log ran on for 15
    // minutes), and no client rgl log of that run was left on the PC at all. So every line also
    // goes here, written straight to disk.
    //   Client: Documents\Mount and Blade II Bannerlord\Logs\BurningEmpires.CoopCompat\coopcompat.log
    //   Server: <BANNERLORD_USER_DIR>\logs\BurningEmpires.CoopCompat\coopcompat.log
    // Past MaxBytes the file becomes coopcompat.old.log (replacing the last one), so the two stay
    // under 2 MB together. No game types, so the launcher tests can run it.
    internal sealed class CompatLogFile : IDisposable
    {
        public const string FolderName = "BurningEmpires.CoopCompat";
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

        // The dedicated server start script sets BANNERLORD_USER_DIR to the server's data folder,
        // whose logs folder the server tool's log pack reads. The game client has no such folder.
        // No Documents folder gives null: a relative path would land wherever the game runs from.
        public static string Folder(string userDir, string documents)
        {
            if (!string.IsNullOrWhiteSpace(userDir))
                return Path.Combine(userDir, "logs", FolderName);
            if (string.IsNullOrWhiteSpace(documents) || !Path.IsPathRooted(documents))
                return null;
            return Path.Combine(documents, "Mount and Blade II Bannerlord", "Logs", FolderName);
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
                        var folder = Folder(Environment.GetEnvironmentVariable("BANNERLORD_USER_DIR"),
                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
                        if (folder == null)
                        {
                            _broken = true;
                            return;
                        }
                        Directory.CreateDirectory(folder);
                        var log = new CompatLogFile(Path.Combine(folder, FileName), MaxBytes);
                        log.Append("---- Burning Empires Coop Compat started, process " + ProcessId());
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

        // One stream stays open, shared so the log packs can read it while the game runs. Each
        // line goes to the end of the file, so a second game on this PC writing the same file
        // does not write over it.
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

        // A full file becomes coopcompat.old.log. When the old file or this one is held open
        // without delete sharing (a zip tool, an editor, a virus scanner), the file starts over
        // instead, so the lines keep coming and the file stays small. Only when even that fails
        // does it keep growing, trying again one MaxBytes later rather than on every line.
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
