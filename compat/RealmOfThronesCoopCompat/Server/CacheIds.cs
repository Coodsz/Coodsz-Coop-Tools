#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RealmOfThronesCoopCompat
{
    // The settlement ids a distance cache file names, read the way the game writes it
    // (NavigationCache<T>.Serialize in 1.4.8): uint32, uint32, int32 count, then per entry a
    // string id, a bool, an int32 n and n times (string id, bool, float, plus a second float for
    // the All navigation type); then an int32 neighbour count with pairs of string ids; then an
    // int32 face count with (int32, string id, bool). Strings are BinaryReader strings.
    // Only for the log line that says a world lacks the cache's settlements, so it never throws:
    // a short or odd file gives null and the reason. No game types, so the launcher tests can run it.
    internal static class CacheIds
    {
        // The All cache stores a land ratio after each distance; the file name says which type it is.
        public static bool HasLandRatio(string path) =>
            Path.GetFileNameWithoutExtension(path ?? "").EndsWith("_All", StringComparison.OrdinalIgnoreCase);

        public static HashSet<string> Read(Stream stream, bool hasLandRatio, out string error)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
                {
                    var length = stream.Length;
                    reader.ReadUInt32();
                    reader.ReadUInt32();
                    var entries = Count(reader, length);
                    for (var i = 0; i < entries; i++)
                    {
                        ids.Add(reader.ReadString());
                        reader.ReadBoolean();
                        var pairs = Count(reader, length);
                        for (var j = 0; j < pairs; j++)
                        {
                            ids.Add(reader.ReadString());
                            reader.ReadBoolean();
                            reader.ReadSingle();
                            if (hasLandRatio)
                                reader.ReadSingle();
                        }
                    }
                    var neighbours = Count(reader, length);
                    for (var i = 0; i < neighbours; i++)
                    {
                        ids.Add(reader.ReadString());
                        ids.Add(reader.ReadString());
                    }
                    var faces = Count(reader, length);
                    for (var i = 0; i < faces; i++)
                    {
                        reader.ReadInt32();
                        ids.Add(reader.ReadString());
                        reader.ReadBoolean();
                    }
                }
                error = null;
                return ids;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + " at byte " + SafePosition(stream) + " (" + ex.Message + ")";
                return null;
            }
        }

        // A count can never be negative or larger than the bytes in the file; a wrong guess at the
        // layout would otherwise loop for a very long time.
        private static int Count(BinaryReader reader, long length)
        {
            var count = reader.ReadInt32();
            if (count < 0 || count > length)
                throw new InvalidDataException("count " + count + " does not fit a " + length + " byte file");
            return count;
        }

        private static long SafePosition(Stream stream)
        {
            try { return stream.Position; }
            catch { return -1; }
        }

        // The one line for a cache whose settlements this world lacks, or null when none are missing.
        public static string MissingLine(string path, ICollection<string> ids, Func<string, bool> settlementExists)
        {
            var missing = ids.Where(id => !settlementExists(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
            if (missing.Count == 0)
                return null;
            return "Map redirect: " + Path.GetFileName(path) + " names " + ids.Count + " settlements and this world lacks "
                + missing.Count + " of them (first " + string.Join(", ", missing.Take(5)) + "). The save may not be a"
                + " Realm of Thrones world, and the game's cache reader is about to fail on the missing ones.";
        }
    }
}
