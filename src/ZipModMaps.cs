using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;

namespace SizeboxTweaks
{
    // Koikatsu maps that come from sideloader zipmods (the game's mods folder).
    //
    // A zipmod is a plain zip whose abdata/ folder overlays the game's. Map mods put their scene
    // bundle in abdata/map/scene/ and usually list it in a Studio map list
    // (abdata/studio/info/**/Map_*.csv) with a readable name. Zipmod entries are stored, not
    // deflated, so Unity can load the bundle straight out of the zip with a file offset.
    internal static class ZipModMaps
    {
        internal class Entry
        {
            public string Name;
            public int Method;
            public long CompSize;
            public long Size;
            public long LocalOffset;
        }

        private class Found
        {
            public string Zip;
            public string Entry;     // abdata/map/scene/x.unity3d, as spelled in the zip
            public string Scene;     // scene name from the map list, may be empty
            public string Label;     // name from the map list or manifest, may be empty
            public string Guid;
            public string Version;
            public int MapsInZip;
            public bool Unsupported; // scene bundle Unity 2022 cannot open (see SceneBundleLooksStandard)
        }

        private const string IndexVersion = "2";

        private static string CacheDir => Path.Combine(Paths.BepInExRootPath, "cache", "SizeboxTweaks", "kkmaps");
        private static string ExtractDir => Path.Combine(CacheDir, "extracted");

        // ---------- discovery ----------

        internal static IEnumerable<KoikatsuMaps.MapEntry> Discover(string game, string installDir, string placeholderThumb)
        {
            string mods = Path.Combine(installDir, "mods");
            if (!Directory.Exists(mods))
                return Enumerable.Empty<KoikatsuMaps.MapEntry>();

            var watch = System.Diagnostics.Stopwatch.StartNew();
            List<Found> found = Scan(game, mods, out int zips, out int rescanned);

            // Several versions of one mod are often installed side by side; the sideloader only
            // uses the newest, and loading two of them would collide inside Unity anyway.
            var newest = new HashSet<string>(
                found.GroupBy(f => string.IsNullOrEmpty(f.Guid) ? f.Zip : f.Guid, StringComparer.OrdinalIgnoreCase)
                     .Select(g => g.OrderByDescending(f => f.Version, VersionComparer.Instance).First().Zip),
                StringComparer.OrdinalIgnoreCase);
            var byBundle = new Dictionary<string, Found>(StringComparer.OrdinalIgnoreCase);
            foreach (Found f in found.Where(f => newest.Contains(f.Zip)))
            {
                if (!byBundle.TryGetValue(f.Entry, out Found had) || VersionComparer.Instance.Compare(f.Version, had.Version) > 0)
                    byBundle[f.Entry] = f;
            }

            var result = new List<KoikatsuMaps.MapEntry>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unsupported = new List<string>();
            foreach (Found f in byBundle.Values)
            {
                string file = Path.GetFileNameWithoutExtension(f.Entry);
                if (KoikatsuMaps.Skipped(file))
                    continue;
                if (f.Unsupported)
                {
                    unsupported.Add($"{f.Label} ({Path.GetFileName(f.Zip)})");
                    continue;
                }
                string label = !string.IsNullOrWhiteSpace(f.Label) ? f.Label.Trim() : file;
                string name = $"{game} mod - {label}";
                if (!names.Add(name))
                {
                    name = $"{game} mod - {label} ({file})";
                    names.Add(name);
                }
                string thumb = Path.Combine(CacheDir, $"{game}mod_{Sanitize(file)}.png");
                result.Add(new KoikatsuMaps.MapEntry
                {
                    Game = game,
                    Name = name,
                    Bundle = f.Zip + "|" + f.Entry,
                    AbData = Path.Combine(installDir, "abdata"),
                    Thumb = thumb,
                    ListThumb = File.Exists(thumb) ? thumb : placeholderThumb,
                    Zip = f.Zip,
                    ZipEntry = f.Entry,
                    SceneName = f.Scene
                });
            }
            Plugin.Log.LogInfo($"Koikatsu maps: {game} mods: {zips} zipmods ({rescanned} read, rest from cache), {found.Count} map bundles, {result.Count} listed after removing old versions, {watch.ElapsedMilliseconds} ms");
            if (unsupported.Count > 0)
                Plugin.Log.LogInfo($"Koikatsu maps: {game} mods left out (repacked scene bundle that Unity 2022 cannot open): {string.Join(", ", unsupported)}");
            return result.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
        }

        // Reading every zip's directory on each start is slow on a mods folder with thousands of
        // zipmods, so the result is cached per file (size + write time).
        private static List<Found> Scan(string game, string mods, out int zips, out int rescanned)
        {
            string indexPath = Path.Combine(CacheDir, $"zipmod_index_{game}.txt");
            var cache = ReadIndex(indexPath);
            var fresh = new Dictionary<string, (string stamp, List<Found> maps)>(StringComparer.OrdinalIgnoreCase);
            zips = 0;
            rescanned = 0;
            foreach (string zip in Directory.EnumerateFiles(mods, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(zip);
                if (!ext.Equals(".zipmod", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                zips++;
                string stamp;
                try
                {
                    var fi = new FileInfo(zip);
                    stamp = fi.Length + ":" + fi.LastWriteTimeUtc.Ticks;
                }
                catch
                {
                    continue;
                }
                if (cache.TryGetValue(zip, out var c) && c.stamp == stamp)
                {
                    fresh[zip] = c;
                    continue;
                }
                rescanned++;
                List<Found> maps;
                try
                {
                    maps = ReadMaps(zip);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Koikatsu maps: could not read {zip}: {e.Message}");
                    maps = new List<Found>();
                }
                fresh[zip] = (stamp, maps);
            }
            if (rescanned > 0 || fresh.Count != cache.Count)
                WriteIndex(indexPath, fresh);
            return fresh.Values.SelectMany(v => v.maps).ToList();
        }

        private static List<Found> ReadMaps(string zip)
        {
            var maps = new List<Found>();
            using (var fs = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
            {
                List<Entry> entries = ReadDirectory(fs);
                List<Entry> scenes = entries.Where(e => IsSceneBundle(e.Name)).ToList();
                if (scenes.Count == 0)
                    return maps;

                string guid = "", version = "", modName = "";
                Entry manifest = entries.FirstOrDefault(e => e.Name.Equals("manifest.xml", StringComparison.OrdinalIgnoreCase));
                if (manifest != null)
                {
                    string xml = Text(ReadEntry(fs, manifest));
                    guid = Tag(xml, "guid");
                    version = Tag(xml, "version");
                    modName = Tag(xml, "name");
                }

                // Studio map lists: ID,[group,]Name,Bundle Path,File Path,...
                var rows = new Dictionary<string, (string name, string scene)>(StringComparer.OrdinalIgnoreCase);
                foreach (Entry csv in entries.Where(e => Regex.IsMatch(e.Name, @"(^|/)studio/info/.*map_[^/]*\.csv$", RegexOptions.IgnoreCase)))
                {
                    foreach (string line in Text(ReadEntry(fs, csv)).Split('\n'))
                    {
                        string[] c = line.Trim().Split(',');
                        int b = Array.FindIndex(c, s => s.Trim().EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase));
                        if (b < 2 || !c[0].Trim().All(char.IsDigit) || c[0].Trim().Length == 0)
                            continue;
                        string bundle = "abdata/" + c[b].Trim().TrimStart('/');
                        if (!rows.ContainsKey(bundle))
                            rows[bundle] = (c[b - 1].Trim(), b + 1 < c.Length ? c[b + 1].Trim() : "");
                    }
                }

                foreach (Entry s in scenes)
                {
                    var f = new Found { Zip = zip, Entry = s.Name, Guid = guid, Version = version, MapsInZip = scenes.Count };
                    try
                    {
                        f.Unsupported = s.Method == 0 && !SceneBundleLooksStandard(fs, DataOffset(fs, s));
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"Koikatsu maps: could not read the bundle header of {s.Name} in {zip}: {ex.Message}");
                    }
                    if (rows.TryGetValue(s.Name, out var row))
                    {
                        f.Label = row.name;
                        f.Scene = row.scene;
                    }
                    else if (scenes.Count == 1)
                        f.Label = modName;
                    else if (modName.Length > 0)
                        f.Label = modName + " - " + Path.GetFileNameWithoutExtension(s.Name);
                    maps.Add(f);
                }
            }
            return maps;
        }

        private static bool IsSceneBundle(string name) =>
            name.StartsWith("abdata/map/scene/", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".unity3d", StringComparison.OrdinalIgnoreCase);

        private static Dictionary<string, (string stamp, List<Found> maps)> ReadIndex(string path)
        {
            var d = new Dictionary<string, (string, List<Found>)>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
                return d;
            try
            {
                string zip = null;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                if (lines.Length == 0 || lines[0] != "V\t" + IndexVersion)
                    return d; // older format: read every zipmod again once
                foreach (string line in lines)
                {
                    string[] p = line.Split('\t');
                    if (p[0] == "Z" && p.Length >= 3)
                    {
                        zip = p[1];
                        d[zip] = (p[2], new List<Found>());
                    }
                    else if (p[0] == "M" && p.Length >= 7 && zip != null)
                        d[zip].Item2.Add(new Found { Zip = zip, Entry = p[1], Scene = p[2], Label = p[3], Guid = p[4], Version = p[5], MapsInZip = int.TryParse(p[6], out int n) ? n : 1, Unsupported = p.Length > 7 && p[7] == "1" });
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Koikatsu maps: zipmod index unreadable, rescanning: {e.Message}");
                d.Clear();
            }
            return d;
        }

        private static void WriteIndex(string path, Dictionary<string, (string stamp, List<Found> maps)> d)
        {
            var sb = new StringBuilder();
            sb.Append("V\t").Append(IndexVersion).Append('\n');
            foreach (var kv in d)
            {
                sb.Append("Z\t").Append(kv.Key).Append('\t').Append(kv.Value.stamp).Append('\n');
                foreach (Found f in kv.Value.maps)
                    sb.Append("M\t").Append(f.Entry).Append('\t').Append(Clean(f.Scene)).Append('\t').Append(Clean(f.Label)).Append('\t')
                      .Append(Clean(f.Guid)).Append('\t').Append(Clean(f.Version)).Append('\t').Append(f.MapsInZip).Append('\t').Append(f.Unsupported ? "1" : "0").Append('\n');
            }
            try
            {
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Koikatsu maps: could not write {path}: {e.Message}");
            }
        }

        private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

        // ---------- loading ----------

        private static readonly Dictionary<string, AssetBundle> Scenes = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AssetBundle> Dependencies = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        // Returns the scene path to hand to SceneManager, or null.
        internal static string LoadScene(KoikatsuMaps.MapEntry e)
        {
            // One mod map bundle at a time: the KK and KKS builds of the same map share internal
            // file names and Unity refuses to load the second while the first is open.
            foreach (var kv in Scenes.Where(kv => !kv.Key.Equals(e.Bundle, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                if (kv.Value != null)
                    kv.Value.Unload(false);
                Scenes.Remove(kv.Key);
            }

            if (!Scenes.TryGetValue(e.Bundle, out AssetBundle bundle) || bundle == null)
            {
                using (var fs = new FileStream(e.Zip, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
                {
                    List<Entry> entries = ReadDirectory(fs);
                    Entry entry = entries.FirstOrDefault(x => x.Name.Equals(e.ZipEntry, StringComparison.OrdinalIgnoreCase));
                    if (entry == null)
                    {
                        Plugin.Log.LogError($"{e.Name}: {e.ZipEntry} is no longer in {e.Zip}");
                        return null;
                    }
                    LoadDependencies(e, fs, entries);
                    bundle = OpenBundle(e.Zip, fs, entry);
                }
                if (bundle == null)
                {
                    Plugin.Log.LogError($"{e.Name}: Unity could not open {e.ZipEntry} from {e.Zip} (see the Unity log above)");
                    return null;
                }
                Scenes[e.Bundle] = bundle;
            }

            string[] paths = bundle.GetAllScenePaths();
            if (paths.Length == 0)
            {
                Plugin.Log.LogError($"{e.Name}: {e.ZipEntry} has no scene");
                return null;
            }
            string pick = paths.FirstOrDefault(p => !string.IsNullOrEmpty(e.SceneName) && Path.GetFileNameWithoutExtension(p).Equals(e.SceneName, StringComparison.OrdinalIgnoreCase)) ?? paths[0];
            Plugin.Log.LogInfo($"{e.Name}: scene {pick} from {Path.GetFileName(e.Zip)} ({paths.Length} in bundle, streamed scene bundle {bundle.isStreamedSceneAssetBundle})");
            return pick;
        }

        private static AssetBundle OpenBundle(string zip, FileStream fs, Entry entry)
        {
            if (entry.Method == 0)
                return AssetBundle.LoadFromFile(zip, 0, (ulong)DataOffset(fs, entry));
            // A deflated entry cannot be read in place; unpack it once into the cache.
            Directory.CreateDirectory(ExtractDir);
            string key = Path.GetFileNameWithoutExtension(zip) + "_" + entry.Name;
            string path = Path.Combine(ExtractDir, Sanitize(key));
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Size)
            {
                Plugin.Log.LogInfo($"Unpacking deflated {entry.Name} from {Path.GetFileName(zip)}");
                File.WriteAllBytes(path, ReadEntry(fs, entry));
            }
            return AssetBundle.LoadFromFile(path);
        }

        // Few mod maps share bundles, but the ones that do ship their own abdata/abdata manifest.
        // Dependencies are looked up in the same zip, then the game's abdata, then other zipmods.
        private static void LoadDependencies(KoikatsuMaps.MapEntry e, FileStream fs, List<Entry> entries)
        {
            Entry man = entries.FirstOrDefault(x => x.Name.Equals("abdata/abdata", StringComparison.OrdinalIgnoreCase));
            if (man == null)
                return;
            AssetBundleManifest manifest = null;
            AssetBundle mb = OpenBundle(e.Zip, fs, man);
            if (mb != null)
            {
                manifest = mb.LoadAsset<AssetBundleManifest>("AssetBundleManifest");
                mb.Unload(false); // keep the manifest object; every manifest bundle is called "abdata"
            }
            if (manifest == null)
            {
                Plugin.Log.LogWarning($"{e.Name}: could not read the zipmod's abdata manifest");
                return;
            }
            string rel = e.ZipEntry.Substring("abdata/".Length).ToLowerInvariant();
            foreach (string dep in manifest.GetAllDependencies(rel))
            {
                string want = "abdata/" + dep;
                if (Dependencies.ContainsKey(want))
                    continue;
                AssetBundle b = null;
                Entry local = entries.FirstOrDefault(x => x.Name.Equals(want, StringComparison.OrdinalIgnoreCase));
                string from;
                if (local != null)
                {
                    b = OpenBundle(e.Zip, fs, local);
                    from = "same zipmod";
                }
                else if (File.Exists(Path.Combine(e.AbData, dep)))
                {
                    b = AssetBundle.LoadFromFile(Path.Combine(e.AbData, dep));
                    from = "game abdata";
                }
                else
                {
                    b = FromOtherZip(Path.Combine(Path.GetDirectoryName(e.AbData), "mods"), want, out from);
                }
                if (b != null)
                {
                    Dependencies[want] = b;
                    Plugin.Log.LogInfo($"{e.Name}: loaded dependency {dep} ({from})");
                }
                else
                    Plugin.Log.LogWarning($"{e.Name}: dependency {dep} not found or failed to load");
            }
        }

        private static AssetBundle FromOtherZip(string mods, string want, out string from)
        {
            from = "not found";
            if (!Directory.Exists(mods))
                return null;
            foreach (string zip in Directory.EnumerateFiles(mods, "*.zip*", SearchOption.AllDirectories))
            {
                try
                {
                    using (var fs = new FileStream(zip, FileMode.Open, FileAccess.Read, FileShare.Read, 4096))
                    {
                        Entry hit = ReadDirectory(fs).FirstOrDefault(x => x.Name.Equals(want, StringComparison.OrdinalIgnoreCase));
                        if (hit == null)
                            continue;
                        from = Path.GetFileName(zip);
                        return OpenBundle(zip, fs, hit);
                    }
                }
                catch
                {
                }
            }
            return null;
        }

        // ---------- Unity bundle header ----------

        // A few map mods were repacked by hand: their scene sits next to a "CAB-..." file instead of
        // Unity's own "BuildPlayer-<scene>.sharedAssets". Koikatsu (Unity 5.6) opens those, but Unity
        // 2022 says the scene "has not been added to the build settings". Read the bundle's file list
        // (UnityFS header + LZ4 block info) to leave them out. Unknown layouts count as standard.
        private static bool SceneBundleLooksStandard(FileStream fs, long offset)
        {
            byte[] h = ReadAt(fs, offset, (int)Math.Min(512, fs.Length - offset));
            int p = 0;
            if (CString(h, ref p) != "UnityFS")
                return true;
            int format = (int)BE32(h, p); p += 4;
            CString(h, ref p);
            CString(h, ref p);
            long size = (long)(((ulong)BE32(h, p) << 32) | BE32(h, p + 4)); p += 8;
            int csz = (int)BE32(h, p), usz = (int)BE32(h, p + 4);
            uint flags = BE32(h, p + 8); p += 12;
            if (format >= 7)
                p = (p + 15) / 16 * 16;
            byte[] info = (flags & 0x80) != 0 ? ReadAt(fs, offset + size - csz, csz) : ReadAt(fs, offset + p, csz);
            uint comp = flags & 0x3f;
            if (comp == 2 || comp == 3)
                info = Lz4(info, usz);
            else if (comp != 0)
                return true; // LZMA block info: not worth a decoder for this check
            int q = 16;
            int blocks = (int)BE32(info, q); q += 4 + blocks * 10;
            int nodes = (int)BE32(info, q); q += 4;
            var names = new List<string>();
            for (int i = 0; i < nodes; i++)
            {
                q += 20;
                names.Add(CString(info, ref q));
            }
            return names.Any(n => n.StartsWith("BuildPlayer-") && n.EndsWith(".sharedAssets")) || !names.Any(n => n.StartsWith("BuildPlayer-"));
        }

        private static string CString(byte[] b, ref int p)
        {
            int end = Array.IndexOf(b, (byte)0, p);
            if (end < 0)
                throw new InvalidDataException("unterminated string");
            string s = Encoding.UTF8.GetString(b, p, end - p);
            p = end + 1;
            return s;
        }

        private static uint BE32(byte[] b, int i) => (uint)((b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]);

        private static byte[] Lz4(byte[] src, int outLen)
        {
            var dst = new byte[outLen];
            int s = 0, d = 0;
            while (s < src.Length)
            {
                int token = src[s++];
                int lit = token >> 4;
                if (lit == 15)
                {
                    int b;
                    do { b = src[s++]; lit += b; } while (b == 255);
                }
                Buffer.BlockCopy(src, s, dst, d, lit);
                s += lit;
                d += lit;
                if (s >= src.Length)
                    break;
                int off = src[s] | (src[s + 1] << 8);
                s += 2;
                int len = token & 15;
                if (len == 15)
                {
                    int b;
                    do { b = src[s++]; len += b; } while (b == 255);
                }
                len += 4;
                for (int i = 0; i < len; i++, d++)
                    dst[d] = dst[d - off];
            }
            return dst;
        }

        // ---------- zip reading (central directory only, no extraction library needed) ----------

        internal static List<Entry> ReadDirectory(FileStream fs)
        {
            long len = fs.Length;
            int tail = (int)Math.Min(len, 22 + 65535 + 20);
            byte[] buf = ReadAt(fs, len - tail, tail);
            int eocd = -1;
            for (int i = tail - 22; i >= 0; i--)
                if (U32(buf, i) == 0x06054b50) { eocd = i; break; }
            if (eocd < 0)
                throw new InvalidDataException("not a zip file");
            long count = U16(buf, eocd + 10);
            long cdSize = U32(buf, eocd + 12);
            long cdOff = U32(buf, eocd + 16);
            if ((count == 0xFFFF || cdOff == 0xFFFFFFFF) && eocd >= 20 && U32(buf, eocd - 20) == 0x07064b50)
            {
                byte[] z64 = ReadAt(fs, (long)U64(buf, eocd - 20 + 8), 56);
                if (U32(z64, 0) == 0x06064b50)
                {
                    count = (long)U64(z64, 32);
                    cdSize = (long)U64(z64, 40);
                    cdOff = (long)U64(z64, 48);
                }
            }

            byte[] cd = ReadAt(fs, cdOff, (int)cdSize);
            var list = new List<Entry>((int)Math.Min(count, 100000));
            int p = 0;
            while (p + 46 <= cd.Length && U32(cd, p) == 0x02014b50)
            {
                int nameLen = U16(cd, p + 28), extraLen = U16(cd, p + 30), commentLen = U16(cd, p + 32);
                var e = new Entry
                {
                    Method = U16(cd, p + 10),
                    CompSize = U32(cd, p + 20),
                    Size = U32(cd, p + 24),
                    LocalOffset = U32(cd, p + 42),
                    Name = Encoding.UTF8.GetString(cd, p + 46, nameLen).Replace('\\', '/')
                };
                // Zip64 sizes/offset live in extra field 0x0001, in this order, only when maxed out.
                int x = p + 46 + nameLen, xEnd = x + extraLen;
                while (x + 4 <= xEnd)
                {
                    int id = U16(cd, x), sz = U16(cd, x + 2);
                    if (id == 1)
                    {
                        int q = x + 4;
                        if (e.Size == 0xFFFFFFFF && q + 8 <= x + 4 + sz) { e.Size = (long)U64(cd, q); q += 8; }
                        if (e.CompSize == 0xFFFFFFFF && q + 8 <= x + 4 + sz) { e.CompSize = (long)U64(cd, q); q += 8; }
                        if (e.LocalOffset == 0xFFFFFFFF && q + 8 <= x + 4 + sz) { e.LocalOffset = (long)U64(cd, q); }
                    }
                    x += 4 + sz;
                }
                list.Add(e);
                p += 46 + nameLen + extraLen + commentLen;
            }
            return list;
        }

        private static long DataOffset(FileStream fs, Entry e)
        {
            byte[] h = ReadAt(fs, e.LocalOffset, 30);
            if (U32(h, 0) != 0x04034b50)
                throw new InvalidDataException("bad local header for " + e.Name);
            return e.LocalOffset + 30 + U16(h, 26) + U16(h, 28);
        }

        internal static byte[] ReadEntry(FileStream fs, Entry e)
        {
            byte[] raw = ReadAt(fs, DataOffset(fs, e), (int)e.CompSize);
            if (e.Method == 0)
                return raw;
            if (e.Method != 8)
                throw new NotSupportedException($"zip method {e.Method} for {e.Name}");
            using (var ds = new DeflateStream(new MemoryStream(raw), CompressionMode.Decompress))
            using (var ms = new MemoryStream((int)Math.Min(e.Size, int.MaxValue)))
            {
                ds.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static byte[] ReadAt(FileStream fs, long offset, int count)
        {
            var buf = new byte[count];
            fs.Seek(offset, SeekOrigin.Begin);
            int got = 0;
            while (got < count)
            {
                int n = fs.Read(buf, got, count - got);
                if (n <= 0)
                    throw new EndOfStreamException();
                got += n;
            }
            return buf;
        }

        private static int U16(byte[] b, int i) => b[i] | (b[i + 1] << 8);
        private static long U32(byte[] b, int i) => (uint)(b[i] | (b[i + 1] << 8) | (b[i + 2] << 16) | (b[i + 3] << 24));
        private static ulong U64(byte[] b, int i) => (ulong)U32(b, i) | ((ulong)U32(b, i + 4) << 32);

        private static string Text(byte[] b)
        {
            string s = Encoding.UTF8.GetString(b);
            return s.Length > 0 && s[0] == '﻿' ? s.Substring(1) : s;
        }

        private static string Tag(string xml, string tag)
        {
            Match m = Regex.Match(xml, $"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s;
        }

        // Sideloader-style version order: compare the numeric runs, then the text.
        private class VersionComparer : IComparer<string>
        {
            internal static readonly VersionComparer Instance = new VersionComparer();

            public int Compare(string a, string b)
            {
                string[] x = Regex.Split(a ?? "", @"[^0-9]+").Where(s => s.Length > 0).ToArray();
                string[] y = Regex.Split(b ?? "", @"[^0-9]+").Where(s => s.Length > 0).ToArray();
                for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
                {
                    long xi = i < x.Length && long.TryParse(x[i], out long xv) ? xv : 0;
                    long yi = i < y.Length && long.TryParse(y[i], out long yv) ? yv : 0;
                    if (xi != yi)
                        return xi.CompareTo(yi);
                }
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
