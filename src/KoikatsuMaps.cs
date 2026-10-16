using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SizeboxTweaks
{
    // Lists Koikatsu / Koikatsu Sunshine maps in Sizebox's stage select and loads them straight
    // from the game's own abdata folder (nothing is copied or converted).
    //
    // Sizebox already loads custom stages as scene AssetBundles and, after loading, swaps every
    // material's shader for one of its own by name (the bundle's compiled shaders are useless in
    // another Unity version anyway). Koikatsu map bundles are scene bundles with type trees and
    // their materials are almost all Unity's Standard shader, so they slot into the same path.
    // What we add: the stage entries, the shader table, layer fixes so the ground is walkable,
    // a spawn point, and a thumbnail captured the first time a map is visited.
    internal static class KoikatsuMaps
    {
        internal class MapEntry
        {
            public string Game;     // "KK" or "KKS"
            public string Name;     // shown in stage select
            public string Bundle;   // absolute path to the scene bundle
            public string AbData;   // the game's abdata folder (for dependencies)
            public string Thumb;    // png path in our cache
            public string ListThumb; // what the stage list shows until Thumb exists (mod maps share a placeholder)
            // Sideloader zipmod maps only
            public string Zip;       // the .zipmod file
            public string ZipEntry;  // abdata/map/scene/x.unity3d inside it
            public string SceneName; // scene to open when the bundle holds several
        }

        private static readonly Dictionary<string, MapEntry> ByBundle = new Dictionary<string, MapEntry>(StringComparer.OrdinalIgnoreCase);
        internal static MapEntry Loading;
        internal static Vector3? LastSpawn;

        private static string CacheDir => Path.Combine(Paths.BepInExRootPath, "cache", "SizeboxTweaks", "kkmaps");
        private static string EmptyDataJson => Path.Combine(CacheDir, "scene_data_empty.json");

        internal static IEnumerable<MapEntry> Discover()
        {
            ByBundle.Clear();
            Directory.CreateDirectory(CacheDir);
            if (!File.Exists(EmptyDataJson))
                File.WriteAllText(EmptyDataJson, "{\"materials\":[]}");

            foreach (var (game, dir) in new[] { ("KK", Plugin.KoikatsuDir.Value), ("KKS", Plugin.KoikatsuSunshineDir.Value) })
            {
                if (string.IsNullOrWhiteSpace(dir))
                    continue;
                string abdata = Path.Combine(dir.Trim(), "abdata");
                string sceneDir = Path.Combine(abdata, "map", "scene");
                if (!Directory.Exists(sceneDir))
                {
                    Plugin.Log.LogWarning($"Koikatsu maps: {sceneDir} not found");
                    continue;
                }
                foreach (string bundle in Directory.GetFiles(sceneDir, "*.unity3d").OrderBy(p => p))
                {
                    string id = Path.GetFileNameWithoutExtension(bundle);
                    if (Skipped(id))
                        continue;
                    var e = new MapEntry
                    {
                        Game = game,
                        Name = $"{game} - {Pretty(id)}",
                        Bundle = bundle,
                        AbData = abdata,
                        Thumb = Path.Combine(CacheDir, $"{game}_{id}.png")
                    };
                    MakeSquareThumb(e.Thumb, game == "KK" ? new Color(0.85f, 0.45f, 0.55f) : new Color(0.35f, 0.6f, 0.85f));
                    ByBundle[bundle] = e;
                    yield return e;
                }

                if (!Plugin.KoikatsuModMaps.Value)
                    continue;
                // Sideloader zipmod maps; they share one placeholder thumbnail until first visited.
                string placeholder = Path.Combine(CacheDir, $"{game}mod_placeholder.png");
                if (PngSize(placeholder).x != PngSize(placeholder).y || !File.Exists(placeholder))
                    WritePlaceholderThumb(placeholder, game == "KK" ? new Color(0.75f, 0.55f, 0.35f) : new Color(0.4f, 0.7f, 0.5f));
                IEnumerable<MapEntry> mods;
                try
                {
                    mods = ZipModMaps.Discover(game, dir.Trim(), placeholder).ToList();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Koikatsu maps: scanning {game} mods failed: {ex}");
                    continue;
                }
                foreach (MapEntry e in mods)
                {
                    ByBundle[e.Bundle] = e;
                    yield return e;
                }
            }
        }

        internal static bool Skipped(string id) =>
            Plugin.KoikatsuSkip.Value.Split(',').Any(s => s.Trim().Length > 0 && id.Equals(s.Trim(), StringComparison.OrdinalIgnoreCase));

        private static readonly Dictionary<string, AssetBundleManifest> Manifests = new Dictionary<string, AssetBundleManifest>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AssetBundle> Dependencies = new Dictionary<string, AssetBundle>(StringComparer.OrdinalIgnoreCase);

        // Some maps (all of KKS) keep shared meshes/textures in other bundles. The game's
        // abdata/abdata manifest lists them; they have to be loaded before the scene.
        internal static void LoadDependencies(MapEntry e)
        {
            if (e.Zip != null)
                return; // zipmods carry their own manifest, handled by ZipModMaps.LoadScene
            AssetBundleManifest manifest = GetManifest(e.AbData);
            if (manifest == null)
                return;
            string rel = e.Bundle.Substring(e.AbData.Length).TrimStart('\\', '/').Replace('\\', '/').ToLowerInvariant();
            foreach (string dep in manifest.GetAllDependencies(rel))
            {
                string path = Path.Combine(e.AbData, dep);
                if (Dependencies.ContainsKey(path))
                    continue;
                if (!File.Exists(path))
                {
                    Plugin.Log.LogWarning($"{e.Name}: dependency {dep} not found");
                    continue;
                }
                AssetBundle b = AssetBundle.LoadFromFile(path);
                if (b != null)
                {
                    Dependencies[path] = b;
                    Plugin.Log.LogInfo($"{e.Name}: loaded dependency {dep}");
                }
                else
                    Plugin.Log.LogWarning($"{e.Name}: dependency {dep} failed to load");
            }
        }

        private static AssetBundleManifest GetManifest(string abdata)
        {
            if (Manifests.TryGetValue(abdata, out AssetBundleManifest m))
                return m;
            string path = Path.Combine(abdata, "abdata");
            try
            {
                AssetBundle b = File.Exists(path) ? AssetBundle.LoadFromFile(path) : null;
                m = b != null ? b.LoadAsset<AssetBundleManifest>("AssetBundleManifest") : null;
                // Every manifest bundle is called "abdata" (KK, KKS, zipmods); Unity only allows one
                // open at a time, and the manifest object outlives the bundle.
                if (b != null)
                    b.Unload(false);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Could not read {path}: {ex.Message}");
            }
            if (m == null)
                Plugin.Log.LogWarning($"No bundle manifest in {abdata}; maps with shared bundles may render white");
            Manifests[abdata] = m;
            return m;
        }

        internal static MapEntry Find(string bundlePath) =>
            bundlePath != null && ByBundle.TryGetValue(bundlePath, out MapEntry e) ? e : null;

        private static string Pretty(string id)
        {
            if (id.Length == 0)
                return id;
            return char.ToUpperInvariant(id[0]) + id.Substring(1);
        }

        // The stage select shows a square picture. Older thumbnails were 16:9: crop those to their
        // middle square (they get re-shot at full size on the next visit), and write a square
        // placeholder for maps never visited.
        private static void MakeSquareThumb(string path, Color placeholder)
        {
            Vector2Int size = PngSize(path);
            if (size.x > 0 && size.x == size.y)
                return;
            if (size.x > 0 && new FileInfo(path).Length > 20000)
            {
                var tex = new Texture2D(2, 2);
                if (tex.LoadImage(File.ReadAllBytes(path)))
                {
                    int side = Mathf.Min(tex.width, tex.height);
                    var sq = new Texture2D(side, side, TextureFormat.RGB24, false);
                    sq.SetPixels(tex.GetPixels((tex.width - side) / 2, (tex.height - side) / 2, side, side));
                    sq.Apply();
                    File.WriteAllBytes(path, sq.EncodeToPNG());
                    UnityEngine.Object.Destroy(sq);
                    UnityEngine.Object.Destroy(tex);
                    return;
                }
                UnityEngine.Object.Destroy(tex);
            }
            WritePlaceholderThumb(path, placeholder);
        }

        // Width and height from a PNG header, (0, 0) if the file is missing or not a PNG
        internal static Vector2Int PngSize(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return Vector2Int.zero;
                var b = new byte[24];
                using (var f = File.OpenRead(path))
                    if (f.Read(b, 0, 24) < 24 || b[1] != (byte)'P' || b[2] != (byte)'N' || b[3] != (byte)'G')
                        return Vector2Int.zero;
                int Be(int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
                return new Vector2Int(Be(16), Be(20));
            }
            catch (IOException)
            {
                return Vector2Int.zero;
            }
        }

        private static void WritePlaceholderThumb(string path, Color c)
        {
            var tex = new Texture2D(256, 256, TextureFormat.RGB24, false);
            var px = new Color[256 * 256];
            for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                    px[y * 256 + x] = Color.Lerp(c * 0.55f, c, y / 255f);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.Destroy(tex);
        }

        private enum Plan { Keep, Cutout, Fade, Hide }

        private const int KkMapLayer = 11;
        // Sky boxes and sea planes around KK maps are kilometres wide; ignore them for spawning.
        private const float HugeCollider = 600f;

        // Decided per renderer/material slot before Sizebox swaps the shaders (the original
        // KK shader name is only readable until then), applied after the swap.
        private static readonly Dictionary<Renderer, Plan[]> Plans = new Dictionary<Renderer, Plan[]>();

        // Called from the OnSceneLoaded prefix, before Sizebox wires up the stage.
        internal static void PrepareScene(Scene scene, Dictionary<string, string> shaderTable)
        {
            MapEntry e = Loading;
            Plans.Clear();
            // Sizebox fills Layers.* in GameController.Awake, which runs after this hook.
            int mapLayer = LayerMask.NameToLayer("Map");
            var roots = scene.GetRootGameObjects();
            int renderers = 0, colliders = 0, triggers = 0;

            foreach (GameObject root in roots)
            {
                // KK keeps its own listeners and ambience in the map; Sizebox brings its own.
                foreach (var l in root.GetComponentsInChildren<AudioListener>(true))
                    UnityEngine.Object.Destroy(l);
                foreach (var a in root.GetComponentsInChildren<AudioSource>(true))
                    a.enabled = false;

                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    t.gameObject.layer = mapLayer;

                // KK lights are masked to KK's own layer numbers (11 = its "Map"); after the layer
                // move they would light nothing (black roads). Map lights now light everything;
                // KK's character-only rim/fill lights would blow out the whole map, so they go.
                foreach (Light l in root.GetComponentsInChildren<Light>(true))
                {
                    if (l.cullingMask == ~0 || (l.cullingMask & (1 << KkMapLayer)) != 0)
                        l.cullingMask = ~0;
                    else
                        l.enabled = false;
                }

                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger)
                    {
                        // KK gameplay trigger volumes (doors, H points, events)
                        c.enabled = false;
                        triggers++;
                    }
                    else if (IsHuge(c) && (c is SphereCollider || c.bounds.size.y > 100f))
                    {
                        // Solid sky domes around the whole map (KK's 1.2 km "sky" sphere). Every
                        // character spawns inside it and physics keeps shoving them toward its
                        // edge: the "pulled to one side and can't move" bug. Thin sea/ground
                        // planes are kept.
                        c.enabled = false;
                        triggers++;
                        Plugin.Log.LogInfo($"Koikatsu map: disabled enclosing collider '{c.name}' ({c.bounds.size})");
                    }
                    else
                        colliders++;
                }
            }

            Bounds world = ColliderBounds(roots, out bool hasWorld);

            foreach (GameObject root in roots)
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    renderers++;
                    Material[] mats = r.sharedMaterials;
                    var plan = new Plan[mats.Length];
                    for (int i = 0; i < mats.Length; i++)
                    {
                        Material m = mats[i];
                        if (m == null)
                            continue;
                        string sn = m.shader != null ? m.shader.name : "";
                        plan[i] = Decide(r, m, sn, world, hasWorld);
                        shaderTable[r.name + m.name] = PickShader(sn);
                        if (Plugin.KoikatsuDebug.Value)
                            Plugin.Log.LogInfo($"  KK mat {r.name} / {m.name}: shader '{sn}' q{m.renderQueue} -> {shaderTable[r.name + m.name]} {plan[i]} bounds {r.bounds.size}");
                    }
                    Plans[r] = plan;
                }
            }

            // Sizebox resolves the skybox through the same table; leaving it out keeps Sizebox's sky.
            if (RenderSettings.skybox != null)
                shaderTable.Remove("Scene-Skybox" + RenderSettings.skybox.name);

            int added = Plugin.KoikatsuAddColliders.Value ? AddMissingColliders() : 0;
            if (added > 0)
                world = ColliderBounds(roots, out hasWorld);

            AddSpawnPoint(scene, world, hasWorld);
            Plugin.Log.LogInfo($"Koikatsu map '{e?.Name}': {renderers} renderers, {colliders} colliders, {triggers} triggers disabled, {added} colliders added, layer -> Map ({mapLayer})");
        }

        private static Plan Decide(Renderer r, Material m, string sn, Bounds world, bool hasWorld)
        {
            if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer)
                return Plan.Hide;
            // Screen effects and the minimap overlay have no Sizebox equivalent.
            if (Has(sn, "heat_distortion") || Has(sn, "Mirror") || Has(sn, "Xffect") || Has(sn, "Silhouette") || Has(sn, "MiniMap") || sn.StartsWith("FX/"))
                return Plan.Hide;
            // Painted fake shadows only make sense with KK's baked lighting.
            if (!Plugin.KoikatsuBakedLighting.Value && sn.StartsWith("Unlit") && (Has(r.name, "kage") || Has(r.name, "shadow")))
                return Plan.Hide;
            // Painted sky domes and star boxes around the whole map: Sizebox draws its own sky.
            if (sn.StartsWith("Skybox") || Has(r.name, "sky") || (sn.StartsWith("Unlit") && hasWorld && r.bounds.Contains(world.center) && r.bounds.size.y > 20f))
                return Plan.Hide;
            // Leaf billboards drawn with particle shaders, and SpeedTree leaves/fronds
            if (Has(sn, "Particles"))
                return Plan.Fade;
            if (Has(sn, "SpeedTree") && (Has(m.name, "Lea") || Has(m.name, "Frond") || Has(m.name, "Billboard")))
                return Plan.Cutout;
            if (sn == "Standard" || sn.StartsWith("Standard (") || sn == "Custom/StandardDouble")
                return Plan.Keep; // has its own _Mode, which Sizebox applies
            int q = m.renderQueue;
            if (Has(sn, "Transparent") || Has(sn, "Fade") || Has(sn, "Alpha") || q >= 2900)
                return Plan.Fade;
            if (Has(sn, "Cutout") || q >= 2450)
                return Plan.Cutout;
            return Plan.Keep;
        }

        private static bool Has(string s, string part) => s != null && s.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;

        // Sizebox only has its own shaders; pick the closest. ShaderLoader then reads _Mode.
        private static string PickShader(string orig)
        {
            if (Has(orig, "Double") || Has(orig, "Cull Off"))
                return "Standard2Sided";
            if (Has(orig, "Specular"))
                return "Standard (Specular setup)";
            return "Standard";
        }

        // Runs after Sizebox has swapped the shaders.
        internal static void FixMaterials(Scene scene)
        {
            int cutout = 0, fade = 0, hidden = 0, metal = 0;
            // Reflections and ambient from Sizebox's sky instead of KK's baked cubemap.
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            DynamicGI.UpdateEnvironment();

            // KK swaps day/evening/night lightmaps with its own scripts, which are not here, so the
            // baked set that loads is often black or the wrong time of day, and its light probes
            // make characters dark. Default: light the map in real time from Sizebox's sun and sky.
            int unbaked = 0;
            if (!Plugin.KoikatsuBakedLighting.Value)
            {
                foreach (Renderer r in Plans.Keys)
                {
                    if (r != null && r.lightmapIndex >= 0 && r.lightmapIndex < 0xFFFE)
                    {
                        r.lightmapIndex = -1;
                        unbaked++;
                    }
                }
                LightmapSettings.lightmaps = new LightmapData[0];
                LightmapSettings.lightProbes = null;
                foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>())
                    if (l.type == LightType.Directional && l.gameObject.scene == scene)
                        l.bakingOutput = new LightBakingOutput { isBaked = false, lightmapBakeType = LightmapBakeType.Realtime, mixedLightingMode = MixedLightingMode.IndirectOnly };
            }

            foreach (var kv in Plans)
            {
                Renderer r = kv.Key;
                if (r == null)
                    continue;
                if (kv.Value.Any(p => p == Plan.Hide))
                {
                    r.enabled = false;
                    hidden++;
                    continue;
                }
                // Sizebox already replaced these with per-renderer instances.
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length && i < kv.Value.Length; i++)
                {
                    Material m = mats[i];
                    if (m == null || !m.HasProperty("_Mode"))
                        continue;
                    if (kv.Value[i] == Plan.Fade)
                    {
                        ShaderLoader.SetupMaterialWithBlendMode(m, ShaderLoader.BlendMode.Fade);
                        fade++;
                    }
                    else if (kv.Value[i] == Plan.Cutout)
                    {
                        ShaderLoader.SetupMaterialWithBlendMode(m, ShaderLoader.BlendMode.Cutout);
                        cutout++;
                    }
                    // KK paints smoothness into metallic maps and relies on its own reflection
                    // cubemap, which does not survive the Unity version jump. Metal with nothing
                    // to reflect renders black, so make everything dielectric.
                    if (m.IsKeywordEnabled("_METALLICGLOSSMAP") || (m.HasProperty("_Metallic") && m.GetFloat("_Metallic") > 0.05f))
                    {
                        m.DisableKeyword("_METALLICGLOSSMAP");
                        m.SetFloat("_Metallic", 0f);
                        if (m.HasProperty("_Glossiness"))
                            m.SetFloat("_Glossiness", Mathf.Min(m.GetFloat("_Glossiness"), 0.35f));
                        metal++;
                    }
                }
            }
            if (Plugin.KoikatsuDebug.Value)
            {
                foreach (var kv in Plans)
                {
                    Renderer r = kv.Key;
                    if (r == null || !(r.name.Contains("road") || r.name.Contains("sibahu") || r.name.Contains("kage")))
                        continue;
                    foreach (Material m in r.sharedMaterials)
                        Plugin.Log.LogInfo($"  KK runtime {r.name}: enabled {r.enabled} shader {m.shader.name} supported {m.shader.isSupported} kw [{string.Join(",", m.shaderKeywords)}] mode {(m.HasProperty("_Mode") ? m.GetFloat("_Mode") : -1)} q{m.renderQueue} color {m.color} mainTex {(m.mainTexture != null ? m.mainTexture.name + " " + m.mainTexture.width + "x" + m.mainTexture.height : "null")} lightmap {r.lightmapIndex} {r.lightmapScaleOffset}");
                }
                Plugin.Log.LogInfo($"  KK lightmaps: {LightmapSettings.lightmaps.Length}, mode {LightmapSettings.lightmapsMode}, ambient {RenderSettings.ambientMode} {RenderSettings.ambientLight}, fog {RenderSettings.fog}");
            }
            Plans.Clear();
            Plugin.Log.LogInfo($"Koikatsu map: {cutout} cutout, {fade} fade, {metal} made non-metallic, {hidden} renderers hidden (sky domes / effects), {unbaked} switched from baked to realtime light");
        }

        // KK walks characters on navmeshes and only gives a few things colliders; Sizebox needs
        // physical floors (KKS sand has none, so everyone falls through) and it is nice to be able
        // to stand on desks and benches. Solid, visible meshes without a collider get one.
        private static int AddMissingColliders()
        {
            // KK pairs a visible mesh "X" with an invisible collision stand-in "X_d" (stairs get
            // a smooth tilted ramp box). Giving the visible stairs their own stepped collider on
            // top of that traps characters on the steps, so anything with a stand-in is skipped.
            var standIns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Collider c in UnityEngine.Object.FindObjectsOfType<Collider>())
            {
                string n = c.gameObject.name;
                int i = n.LastIndexOf("_d", StringComparison.OrdinalIgnoreCase);
                if (i > 0)
                    standIns.Add(n.Substring(0, i));
            }

            int added = 0, skipped = 0;
            foreach (var kv in Plans)
            {
                Renderer r = kv.Key;
                if (!(r is MeshRenderer) || !r.enabled || kv.Value.Any(p => p != Plan.Keep))
                    continue;
                if (standIns.Contains(r.name))
                {
                    skipped++;
                    continue;
                }
                if (r.bounds.size.magnitude < 0.3f)
                    continue;
                if (r.GetComponent<Collider>() != null || r.GetComponentInParent<Collider>() != null || r.GetComponentInChildren<Collider>() != null)
                    continue;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                    continue;
                try
                {
                    var mc = r.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    added++;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"No collider for {r.name}: {e.Message}");
                }
            }
            if (skipped > 0)
                Plugin.Log.LogInfo($"Koikatsu map: {skipped} meshes already have a KK collision stand-in (X_d), no collider added");
            return added;
        }

        // A floor point where a person-sized capsule doesn't overlap anything (spawning inside a
        // wall or a desk traps the character). Searches outward in rings up to 8 m, staying on
        // the same floor level.
        private static bool FindFreeSpot(Vector3 floor, int mask, out Vector3 spot)
        {
            spot = floor;
            if (IsFree(floor, mask))
                return true;
            for (float r = 0.5f; r <= 8f; r += 0.5f)
            {
                int steps = Mathf.Max(8, Mathf.RoundToInt(r * 8f));
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps;
                    Vector3 probe = floor + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    if (!Physics.Raycast(probe + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 3f, mask) || hit.normal.y < 0.7f)
                        continue;
                    Vector3 p = hit.point + Vector3.up * 0.05f;
                    if (Mathf.Abs(p.y - floor.y) < 0.3f && IsFree(p, mask))
                    {
                        spot = p;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsFree(Vector3 feet, int mask)
        {
            return !Physics.CheckCapsule(feet + Vector3.up * 0.35f, feet + Vector3.up * 1.6f, 0.3f, mask, QueryTriggerInteraction.Ignore);
        }

        private static bool IsHuge(Collider c)
        {
            Vector3 size = c.bounds.size;
            return size.x > HugeCollider || size.z > HugeCollider;
        }

        private static Bounds ColliderBounds(GameObject[] roots, out bool any)
        {
            Bounds b = default;
            any = false;
            foreach (GameObject root in roots)
                foreach (Collider c in root.GetComponentsInChildren<Collider>())
                {
                    if (!c.enabled || c.isTrigger || IsHuge(c))
                        continue;
                    if (!any) { b = c.bounds; any = true; }
                    else b.Encapsulate(c.bounds);
                }
            return b;
        }

        private static void AddSpawnPoint(Scene scene, Bounds b, bool any)
        {
            if (scene.GetRootGameObjects().Any(r => r.GetComponentInChildren<PlayerSpawnPoint>(true) != null))
                return;

            Vector3 spawn = Vector3.zero;
            if (any)
            {
                Physics.SyncTransforms();
                // KK maps are built around the origin with the main floor near y = 0, so take the
                // upward-facing surface closest to y = 0, looking down the origin column first and
                // the middle of the map second. Giant sky/sea colliders are skipped.
                int mask = LayerMask.GetMask("Map");
                float top = Mathf.Min(b.max.y, 400f) + 10f;
                bool found = false;
                foreach (Vector3 col in new[] { Vector3.zero, new Vector3(b.center.x, 0f, b.center.z) })
                {
                    RaycastHit[] hits = Physics.RaycastAll(new Vector3(col.x, top, col.z), Vector3.down, top + 420f, mask);
                    var floors = hits.Where(h => h.normal.y > 0.7f && !IsHuge(h.collider)).OrderBy(h => Mathf.Abs(h.point.y)).ToArray();
                    // The surface closest to y = 0 is the main floor; if furniture stands on that
                    // spot (a lectern, a bed), walk out to free floor at the same level rather than
                    // standing on top of it.
                    foreach (RaycastHit f in floors)
                    {
                        if (FindFreeSpot(f.point + Vector3.up * 0.05f, mask, out spawn))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found)
                        break;
                }
                Plugin.Log.LogInfo($"Koikatsu map bounds {b.size}, spawn at {spawn}{(found ? "" : " (no floor found, using origin)")}");
            }

            // The spawn point is also where Sizebox's floating camera starts, and that camera
            // sets its own speed and size from its height above the ground: on the floor it is
            // tiny and crawls. Start it at eye height (lower under a low ceiling), 3 m outdoors.
            float camHeight = 3f;
            if (Physics.Raycast(spawn + Vector3.up * 0.1f, Vector3.up, out RaycastHit ceiling, 6f, LayerMask.GetMask("Map")))
                camHeight = Mathf.Clamp(ceiling.distance * 0.6f, 0.5f, 1.7f);

            var go = new GameObject("KK Spawn");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = spawn + Vector3.up * camHeight;
            LastSpawn = spawn;
            if (!scene.GetRootGameObjects().Any(r => r.GetComponentInChildren<MapSettings>(true) != null))
            {
                // Without map settings the game allows micros down to 0.001 (a millimetre)
                var settings = new GameObject("KK Map Settings");
                SceneManager.MoveGameObjectToScene(settings, scene);
                settings.AddComponent<MapSettings>().minPlayerSize = 0.1f;
            }
            go.AddComponent<PlayerSpawnPoint>();
        }

        private const int ThumbSize = 512;

        // First visit: replace the placeholder thumbnail with a square picture of the map, taken
        // from the game cameras (far camera with the sky first, then the near one) without the
        // menus or the player in it. Shown in the stage select from then on.
        internal static IEnumerator CaptureThumb(MapEntry e, SceneLoader loader)
        {
            if (e == null)
                yield break;
            Vector2Int have = PngSize(e.Thumb);
            if (have.x >= ThumbSize && have.x == have.y)
                yield break;
            yield return new WaitForSeconds(4f);
            yield return new WaitForEndOfFrame();

            int ui = LayerMask.NameToLayer("UI");
            int player = LayerMask.NameToLayer("Player");
            Camera[] cams = Camera.allCameras
                .Where(c => c.enabled && c.targetTexture == null && c.cullingMask != 0 && (ui < 0 || c.cullingMask != 1 << ui))
                .OrderBy(c => c.depth).ToArray();
            if (cams.Length == 0)
                yield break;

            var rt = RenderTexture.GetTemporary(ThumbSize, ThumbSize, 24);
            foreach (Camera cam in cams)
            {
                RenderTexture oldTarget = cam.targetTexture;
                int oldMask = cam.cullingMask;
                float oldFov = cam.fieldOfView;
                cam.targetTexture = rt;
                cam.aspect = 1f;
                if (player >= 0)
                    cam.cullingMask &= ~(1 << player);
                if (!cam.orthographic)
                    cam.fieldOfView = Mathf.Max(oldFov, 70f);
                try
                {
                    cam.Render();
                }
                finally
                {
                    cam.targetTexture = oldTarget;
                    cam.cullingMask = oldMask;
                    cam.fieldOfView = oldFov;
                    cam.ResetAspect();
                }
            }
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(ThumbSize, ThumbSize, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, ThumbSize, ThumbSize), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(e.Thumb, tex.EncodeToPNG());
            e.ListThumb = null;
            Plugin.Log.LogInfo($"Saved thumbnail for {e.Name}");

            // The stage list loaded its pictures once; swap this one in so it shows without a restart
            var scenes = Traverse.Create(loader).Field("_allScenes").GetValue<List<SceneLoader.SceneInfo>>();
            SceneLoader.SceneInfo info = scenes?.FirstOrDefault(s => s.AssetData != null && string.Equals(s.AssetData.Path, e.Bundle, StringComparison.OrdinalIgnoreCase));
            if (info != null)
            {
                info.AssetData.Thumbnail = e.Thumb;
                info.Thumbnail = Sprite.Create(tex, new Rect(0, 0, ThumbSize, ThumbSize), new Vector2(0.5f, 0.5f));
            }
            else
                UnityEngine.Object.Destroy(tex);
        }
    }

    [HarmonyPatch(typeof(SceneLoader), "FindScenes")]
    internal static class SceneListPatch
    {
        private static void Postfix(List<AssetData> ____assetDataList, IEnumerable<AssetData> __result)
        {
            if (!Plugin.KoikatsuEnabled.Value || ____assetDataList == null)
                return;
            if (____assetDataList.Any(a => KoikatsuMaps.Find(a.Path) != null))
                return; // already added (FindScenes caches its list)
            int n = 0;
            foreach (KoikatsuMaps.MapEntry e in KoikatsuMaps.Discover())
            {
                ____assetDataList.Add(new AssetData
                {
                    Name = e.Name,
                    Dir = Path.GetDirectoryName(e.Zip ?? e.Bundle) + "/",
                    Path = e.Bundle,
                    Data = Path.Combine(Paths.BepInExRootPath, "cache", "SizeboxTweaks", "kkmaps", "scene_data_empty.json"),
                    Thumbnail = e.ListThumb ?? e.Thumb,
                    Modified = 0
                });
                n++;
            }
            Plugin.Log.LogInfo($"Koikatsu maps: added {n} stages");
        }
    }

    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadScene), typeof(SceneLoader.SceneInfo))]
    internal static class SceneLoadStartPatch
    {
        private static void Prefix(SceneLoader.SceneInfo scene)
        {
            KoikatsuMaps.Loading = KoikatsuMaps.Find(scene?.AssetData?.Path);
            if (KoikatsuMaps.Loading == null)
                return;
            Plugin.Log.LogInfo($"Loading Koikatsu map {KoikatsuMaps.Loading.Name} from {KoikatsuMaps.Loading.Bundle}");
            try
            {
                KoikatsuMaps.LoadDependencies(KoikatsuMaps.Loading);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Koikatsu map dependencies failed: " + e);
            }
        }
    }

    // Sizebox opens custom stage bundles with AssetBundle.LoadFromFile(path); zipmod maps live
    // inside a zip, so they are opened at their offset instead.
    [HarmonyPatch(typeof(SceneLoader), "LoadAssetBundleScene")]
    internal static class ZipSceneLoadPatch
    {
        private static readonly System.Reflection.MethodInfo LoadByPath =
            AccessTools.Method(typeof(SceneLoader), "LoadScene", new[] { typeof(string) });

        private static bool Prefix(SceneLoader __instance, string assetPath, ref AsyncOperation __result)
        {
            KoikatsuMaps.MapEntry e = KoikatsuMaps.Find(assetPath);
            if (e?.Zip == null)
                return true;
            __result = null;
            try
            {
                string scene = ZipModMaps.LoadScene(e);
                if (scene != null)
                    __result = (AsyncOperation)LoadByPath.Invoke(__instance, new object[] { scene });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Koikatsu mod map {e.Name} failed to load: {ex}");
            }
            if (__result == null)
                KoikatsuMaps.Loading = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(SceneLoader), "OnSceneLoaded")]
    internal static class SceneLoadedPatch
    {
        private static void Prefix(Scene scene, Dictionary<string, string> ____shaderData)
        {
            if (KoikatsuMaps.Loading == null)
                return;
            try
            {
                KoikatsuMaps.PrepareScene(scene, ____shaderData);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Koikatsu map prepare failed: " + e);
            }
        }

        private static void Postfix(SceneLoader __instance, Scene scene)
        {
            KoikatsuMaps.MapEntry e = KoikatsuMaps.Loading;
            if (e == null)
                return;
            KoikatsuMaps.Loading = null;
            try
            {
                KoikatsuMaps.FixMaterials(scene);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Koikatsu map material fix failed: " + ex);
            }
            __instance.StartCoroutine(KoikatsuMaps.CaptureThumb(e, __instance));
        }
    }
}
