using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SizeboxTweaks
{
    // Developer-only automation (Dev.SelfTest in the config, Off by default).
    // Loads a stage by itself, spawns every installed character at the same typed-in height
    // side by side, logs how tall each one really renders and saves a screenshot, so the
    // height fix can be checked without anyone clicking through the menus.
    internal class SelfTest : MonoBehaviour
    {
        private string OutDir => Path.Combine(Paths.BepInExRootPath, "selftest");
        private string _stage = "";

        private IEnumerator Start()
        {
            Directory.CreateDirectory(OutDir);
            Plugin.Log.LogInfo($"[SelfTest] mode {Plugin.Test.Value}");

            SceneLoader loader = null;
            float waited = 0f;
            while ((loader = FindObjectOfType<SceneLoader>()) == null && waited < 60f)
            {
                yield return new WaitForSeconds(0.5f);
                waited += 0.5f;
            }
            if (loader == null)
            {
                Plugin.Log.LogError("[SelfTest] no SceneLoader after 60 s");
                yield break;
            }
            yield return new WaitForSeconds(3f);

            if (Plugin.Test.Value == DevTest.Menu)
            {
                yield return MenuShots();
                yield break;
            }

            if (Plugin.Test.Value == DevTest.UiTour)
            {
                yield return UiTour(loader);
                yield break;
            }

            List<SceneLoader.SceneInfo> scenes = loader.GetSceneList();
            Plugin.Log.LogInfo("[SelfTest] stages: " + string.Join(" | ", scenes.Select(s => s.Scene + (s.AssetData != null ? " (custom)" : ""))));
            string want = Plugin.TestScene.Value;
            SceneLoader.SceneInfo pick = string.IsNullOrEmpty(want) ? scenes.FirstOrDefault() : scenes.FirstOrDefault(s => s.Scene == want);
            if (pick == null)
            {
                Plugin.Log.LogError($"[SelfTest] stage '{want}' not found");
                yield break;
            }
            _stage = pick.Scene;
            Plugin.Log.LogInfo($"[SelfTest] loading stage '{pick.Scene}'");
            AsyncOperation op = loader.LoadScene(pick);
            if (op == null)
            {
                Plugin.Log.LogError("[SelfTest] LoadScene returned null (bundle failed to load?)");
                yield break;
            }
            while (!op.isDone)
                yield return null;

            waited = 0f;
            while ((GameController.Instance == null || GameController.LocalClient == null || ObjectManager.Instance == null) && waited < 60f)
            {
                yield return new WaitForSeconds(0.5f);
                waited += 0.5f;
            }
            yield return new WaitForSeconds(4f);
            Plugin.Log.LogInfo($"[SelfTest] stage ready, ReferenceScale {GameController.ReferenceScale}");

            if (Plugin.Test.Value == DevTest.Scene)
            {
                LogSceneStats();
                // Frame time over 6 s, looking around so the whole map gets drawn
                Transform camT = Camera.main != null ? Camera.main.transform : null;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                yield return new WaitForSeconds(1f);
                var times = new List<float>();
                float tt = 0f;
                while (tt < 6f)
                {
                    if (camT != null)
                        camT.rotation = Quaternion.Euler(10f, tt * 60f, 0f);
                    yield return null;
                    times.Add(Time.unscaledDeltaTime);
                    tt += Time.unscaledDeltaTime;
                }
                times.Sort();
                float avg = times.Average();
                Plugin.Log.LogInfo($"[SelfTest] {SystemInfo.graphicsDeviceType}: {1f / avg:0} fps average, worst 1% {1f / times[(int)(times.Count * 0.99f)]:0} fps, longest frame {times[times.Count - 1] * 1000f:0} ms ({times.Count} frames)");
                yield return Shot("scene_" + Sanitize(pick.Scene));
                yield break;
            }

            if (Plugin.Test.Value == DevTest.Stairs)
            {
                yield return StairsProbe();
                yield break;
            }

            if (Plugin.Test.Value == DevTest.Drift)
            {
                yield return DriftProbe();
                yield break;
            }

            if (Plugin.Test.Value == DevTest.Movement)
            {
                yield return MovementProbe();
                yield break;
            }

            if (Plugin.Test.Value == DevTest.Qol)
            {
                yield return QolProbe();
                Plugin.Log.LogInfo("[SelfTest] screenshot qol done");
                yield break;
            }

            if (Plugin.Test.Value == DevTest.Gameplay)
            {
                yield return GameplayProbe();
                Plugin.Log.LogInfo("[SelfTest] screenshot gameplay done");
                yield break;
            }

            if (Plugin.Test.Value == DevTest.SpawnSize)
            {
                yield return SpawnSizeProbe();
                Plugin.Log.LogInfo("[SelfTest] screenshot spawn done");
                yield break;
            }

            if (Plugin.Test.Value == DevTest.Push)
            {
                yield return PushProbe();
                Plugin.Log.LogInfo("[SelfTest] screenshot push done");
                yield break;
            }

            yield return HeightLineup();
        }

        // Possess a micro, feed it scripted input and log how quickly it starts, stops and turns,
        // once with the game's values and once with ours.
        private IEnumerator MovementProbe()
        {
            AssetManager am = AssetManager.Instance;
            string name = am.GetFemaleMicroAssetNames().Concat(am.GetMaleMicroAssetNames()).FirstOrDefault(n => n.IndexOf("unitychan", System.StringComparison.OrdinalIgnoreCase) >= 0)
                          ?? am.GetFemaleMicroAssetNames().First();
            Transform cam = Camera.main.transform;
            Vector3 pos = cam.position + cam.forward.Flatten().normalized * 4f;
            if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 500f, Layers.walkableMask))
                pos = hit.point;
            EntityBase micro = null;
            GameController.LocalClient.SpawnMicro(am.GetAssetDescriptionByName(name), pos, Quaternion.LookRotation(cam.forward.Flatten()), 1f, e => micro = e);
            while (micro == null)
                yield return null;
            yield return new WaitForSeconds(1f);
            if (micro is Humanoid hm && hm.ai != null)
                hm.ai.DisableAI();
            bool ok = GameController.LocalClient.Player.PlayAs(micro as IPlayable);
            Plugin.Log.LogInfo($"[SelfTest] playing as {name}: {ok}");
            yield return new WaitForSeconds(2f);

            var ctrl = FindObjectOfType<NewPlayerMicroController>();
            Rigidbody rb = micro.GetComponent<Rigidbody>();
            if (ctrl == null || rb == null)
            {
                Plugin.Log.LogError("[SelfTest] no micro controller / rigidbody");
                yield break;
            }

            foreach (bool tuned in new[] { false, true })
            {
                Plugin.MovementEnabled.Value = tuned;
                yield return Drive(ctrl, 0f, 0f, 1f);
                float top = 0f, t90 = -1f, t10 = -1f, maxSlip = 0f, slipSettle = -1f, drift = -1f;
                // run forward
                float t = 0f;
                while (t < 2f)
                {
                    SetInput(ctrl, 0f, 1f);
                    yield return new WaitForFixedUpdate();
                    t += Time.fixedDeltaTime;
                    top = Mathf.Max(top, rb.velocity.Flatten().magnitude);
                }
                t = 0f;
                yield return Drive(ctrl, 0f, 1f, 0f);
                // measure ramp-up from standstill again for a clean number
                yield return Drive(ctrl, 0f, 0f, 1.5f);
                t = 0f;
                while (t < 2f)
                {
                    SetInput(ctrl, 0f, 1f);
                    yield return new WaitForFixedUpdate();
                    t += Time.fixedDeltaTime;
                    if (t90 < 0f && rb.velocity.Flatten().magnitude >= top * 0.9f)
                        t90 = t;
                }
                // let go
                t = 0f;
                while (t < 2f)
                {
                    SetInput(ctrl, 0f, 0f);
                    yield return new WaitForFixedUpdate();
                    t += Time.fixedDeltaTime;
                    if (t10 < 0f && rb.velocity.Flatten().magnitude <= top * 0.1f)
                        t10 = t;
                }
                // run, then hard 90 degree turn: how far does the body lag the travel direction
                t = 0f;
                while (t < 1.5f)
                {
                    SetInput(ctrl, 0f, 1f);
                    yield return new WaitForFixedUpdate();
                    t += Time.fixedDeltaTime;
                }
                t = 0f;
                while (t < 2f)
                {
                    SetInput(ctrl, 1f, 0f);
                    yield return new WaitForFixedUpdate();
                    t += Time.fixedDeltaTime;
                    Vector3 v = rb.velocity.Flatten();
                    if (drift < 0f && Vector3.Angle(v, cam.right.Flatten()) < 10f)
                        drift = t;
                    if (v.magnitude > top * 0.2f)
                    {
                        float slip = Vector3.Angle(v, micro.transform.forward.Flatten());
                        maxSlip = Mathf.Max(maxSlip, slip);
                        if (slipSettle < 0f && t > 0.05f && slip < 10f)
                            slipSettle = t;
                    }
                }
                SetInput(ctrl, 0f, 0f);
                // Jump from standstill and record the peak
                yield return Drive(ctrl, 0f, 0f, 1.5f);
                float baseY = micro.transform.position.y, peak = 0f;
                Traverse.Create(ctrl).Field("_jump").SetValue(true);
                for (float jt = 0f; jt < 3f; jt += Time.fixedDeltaTime)
                {
                    yield return new WaitForFixedUpdate();
                    peak = Mathf.Max(peak, micro.transform.position.y - baseY);
                }
                Plugin.Log.LogInfo($"[SelfTest] jump {(tuned ? "TUNED " : "VANILLA")}: peak {peak:0.00} u = {peak / micro.MeshHeight:0.00} body heights ({Sbox.ScaleToMeter(peak) / Sbox.ScaleToMeter(micro.MeshHeight) * 1.65f:0.00} m for a 1.65 m person)");
                Plugin.Log.LogInfo($"[SelfTest] movement {(tuned ? "TUNED " : "VANILLA")}: top speed {top:0.00} u/s, 0->90% in {t90:0.00}s, stop to 10% in {t10:0.00}s, " +
                                   $"90deg turn: travel direction follows input in {drift:0.00}s, max body-vs-travel angle {maxSlip:0}deg");
            }
            yield return Shot("movement_end");
        }

        // Something on Koikatsu maps drags characters to one side. Possess a micro that stands
        // still, spawn a few AI micros and a giantess around it, and log where everyone goes.
        private IEnumerator DriftProbe()
        {
            AssetManager am = AssetManager.Instance;
            Vector3 origin = KoikatsuMaps.LastSpawn ?? Camera.main.transform.position;
            if (Physics.Raycast(origin + Vector3.up, Vector3.down, out RaycastHit floor, 5f, Layers.walkableMask))
                origin = floor.point;
            var names = am.GetFemaleMicroAssetNames().Concat(am.GetMaleMicroAssetNames()).Take(4).ToList();
            var all = new List<EntityBase>();
            for (int i = 0; i < names.Count; i++)
            {
                Vector3 p = origin + new Vector3((i - 1.5f) * 1.2f, 0.05f, 1.5f);
                GameController.LocalClient.SpawnMicro(am.GetAssetDescriptionByName(names[i]), p, Quaternion.identity, 1f, e => all.Add(e));
            }
            float waited = 0f;
            while (all.Count < names.Count && waited < 20f) { yield return new WaitForSeconds(0.25f); waited += 0.25f; }
            foreach (EntityBase e in all)
                e.MeshHeight = Sbox.MeterToScale(1.65f);
            EntityBase player = all[0];
            if (player is Humanoid ph && ph.ai != null)
                ph.ai.DisableAI();
            bool ok = GameController.LocalClient.Player.PlayAs(player as IPlayable);
            Plugin.Log.LogInfo($"[SelfTest] drift: {all.Count} micros at {origin}, player possessed {ok}; AI on for the others: {GlobalPreferences.MicroAI.value}");
            // A giantess with AI on, 20 m tall, a bit away
            EntityBase gts = null;
            // TestModels = "nogts" leaves the giantess out (small indoor maps)
            string gname = Plugin.TestModels.Value.Contains("nogts") ? null : am.GetGtsAssetNames().FirstOrDefault();
            if (gname != null)
                GameController.LocalClient.SpawnGiantess(am.GetAssetDescriptionByName(gname), origin + new Vector3(0f, 0.05f, 15f), Quaternion.identity, 0.01f, e => gts = e);
            waited = 0f;
            while (gname != null && gts == null && waited < 20f) { yield return new WaitForSeconds(0.25f); waited += 0.25f; }
            if (gts != null)
            {
                gts.MeshHeight = Sbox.MeterToScale(20f);
                all.Add(gts);
            }
            yield return new WaitForSeconds(2f);

            // A few spawnable objects (cars, props) next to the micros
            // The ruler and the two cars (cars were reported drifting on KK maps)
            var objs = am.GetObjectAssets().Where(a => a.AssetName.IndexOf("Ruler", System.StringComparison.OrdinalIgnoreCase) >= 0 || a.AssetName.IndexOf("Car", System.StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Plugin.Log.LogInfo("[SelfTest] objects: " + string.Join(", ", am.GetObjectAssets().Select(a => a.AssetName)));
            for (int i = 0; i < objs.Count; i++)
            {
                EntityBase o = null;
                GameController.LocalClient.SpawnObject(objs[i], origin + new Vector3((i - 1f) * 3f, 0.3f, -3f), Quaternion.identity, 1f, x => o = x);
                float ow = 0f;
                while (o == null && ow < 5f) { yield return new WaitForSeconds(0.1f); ow += 0.1f; }
                if (o != null)
                    all.Add(o);
            }
            yield return new WaitForSeconds(2f);

            // What is the player standing on / touching?
            {
                Vector3 pp = player.transform.position;
                if (Physics.Raycast(pp + Vector3.up * 0.5f, Vector3.down, out RaycastHit under, 3f, Layers.walkableMask))
                    Plugin.Log.LogInfo($"[SelfTest] player stands on '{under.collider.name}' ({under.collider.GetType().Name}, convex {(under.collider as MeshCollider)?.convex}) at y {under.point.y:0.00}, normal {under.normal}");
                foreach (Collider c in Physics.OverlapCapsule(pp + Vector3.up * 0.3f, pp + Vector3.up * 1.5f, 0.3f))
                    if (c.gameObject.layer == LayerMask.NameToLayer("Map"))
                        Plugin.Log.LogInfo($"[SelfTest] player overlaps map collider '{c.name}' ({c.GetType().Name}) bounds {c.bounds.size}");
            }

            // Drive the player 2 s each way; a pull shows up as one direction much shorter.
            var ctrl = FindObjectOfType<NewPlayerMicroController>();
            foreach (var (h, v, label) in new[] { (0f, 1f, "forward"), (0f, -1f, "back"), (1f, 0f, "right"), (-1f, 0f, "left") })
            {
                Vector3 a = player.transform.position;
                yield return Drive(ctrl, h, v, 2f);
                yield return Drive(ctrl, 0f, 0f, 0.6f);
                Vector3 d = player.transform.position - a;
                Plugin.Log.LogInfo($"[SelfTest] drive {label,-7}: moved {new Vector3(d.x, 0f, d.z).magnitude:0.00} m ({d.x:0.00},{d.y:0.00},{d.z:0.00}) grounded y {player.transform.position.y:0.00}");
            }

            // Layer matrix for the layers that matter
            string[] ln = { "Default", "Map", "Building", "Object", "GTSCapsule", "GTSBody", "Player", "Micro" };
            foreach (string a in ln)
                Plugin.Log.LogInfo($"[SelfTest] layer {a,-10} ({LayerMask.NameToLayer(a),2}) collides with: " +
                    string.Join(" ", ln.Where(b => !Physics.GetIgnoreLayerCollision(LayerMask.NameToLayer(a), LayerMask.NameToLayer(b)))));

            // Now possess the giantess and drive her the same way.
            if (gts != null && gts is Humanoid gh)
            {
                if (gh.ai != null)
                    gh.ai.DisableAI();
                bool okG = GameController.LocalClient.Player.PlayAs(gts as IPlayable);
                yield return new WaitForSeconds(2f);
                var gctrl = FindObjectOfType<PlayerGiantessController>();
                // The giantess controller zeroes input unless the game is in play mode.
                GameController.playerInputEnabled = true;
                Plugin.Log.LogInfo($"[SelfTest] playing giantess: {okG}, controller {(gctrl != null)}");
                foreach (var (h, v, label) in new[] { (0f, 1f, "forward"), (0f, -1f, "back"), (1f, 0f, "right"), (-1f, 0f, "left") })
                {
                    Vector3 a = gts.transform.position;
                    float tt = 0f;
                    while (tt < 3f)
                    {
                        Traverse.Create(gctrl).Field("horizontalInput").SetValue(h);
                        Traverse.Create(gctrl).Field("verticalInput").SetValue(v);
                        yield return new WaitForFixedUpdate();
                        tt += Time.fixedDeltaTime;
                    }
                    Traverse.Create(gctrl).Field("horizontalInput").SetValue(0f);
                    Traverse.Create(gctrl).Field("verticalInput").SetValue(0f);
                    yield return new WaitForSeconds(1f);
                    Vector3 d = gts.transform.position - a;
                    Plugin.Log.LogInfo($"[SelfTest] gts drive {label,-7}: moved {new Vector3(d.x, 0f, d.z).magnitude:0.00} m ({d.x:0.00},{d.y:0.00},{d.z:0.00}), anim speed {((Humanoid)gts).animationManager.GetCurrentSpeed():0.00}");
                }
            }

            var start = all.ToDictionary(e => e, e => e.transform.position);
            for (int t = 0; t <= 10; t += 2)
            {
                foreach (EntityBase e in all)
                {
                    if (e == null) continue;
                    Vector3 d = e.transform.position - start[e];
                    Rigidbody rb = e.GetComponent<Rigidbody>();
                    Plugin.Log.LogInfo($"[SelfTest] t={t,2}s {(e == player ? "PLAYER" : e is Giantess ? "GTS   " : e is Micro ? "npc   " : "object")} {e.asset?.AssetName,-22} moved {d.magnitude,6:0.00} ({d.x:0.00},{d.y:0.00},{d.z:0.00}) vel {(rb ? rb.velocity : Vector3.zero)} parent {(e.transform.parent ? e.transform.parent.name : "-")}");
                }
                yield return new WaitForSeconds(2f);
            }
            yield return Shot("drift_end");
        }

        // Main menu with the version label, then the stage select showing the longest stage name.
        private IEnumerator MenuShots()
        {
            yield return Shot("menu_main");
            MenuController menu = FindObjectOfType<MenuController>();
            AccessTools.Method(typeof(MenuController), "OnStartClick").Invoke(menu, null);
            yield return new WaitForSeconds(1f);
            UIStageSelect select = Resources.FindObjectsOfTypeAll<UIStageSelect>().FirstOrDefault(u => u.gameObject.scene.IsValid());
            int longest = 0, best = 0, count = FindObjectOfType<SceneLoader>().GetSceneList().Count;
            for (int i = 0; i < count; i++)
            {
                int len = menu.GetActiveSceneData().Scene.Length;
                if (len > longest) { longest = len; best = i; }
                menu.SwitchNextScene();
            }
            for (int i = 0; i < best; i++)
                menu.SwitchNextScene();
            AccessTools.Method(typeof(UIStageSelect), "ShowStage").Invoke(select, null);
            Plugin.Log.LogInfo($"[SelfTest] stage select showing '{menu.GetActiveSceneData().Scene}'");
            yield return new WaitForSeconds(1f);
            yield return Shot("menu_stage_select");
        }

        // Find a Koikatsu stair ramp (an "X_d" box collider tilted like a ramp), put the player at
        // its low end and walk up it. Also checks the jump: landing should hand control back quickly.
        private IEnumerator StairsProbe()
        {
            AssetManager am = AssetManager.Instance;
            Collider ramp = FindObjectsOfType<BoxCollider>()
                .Where(c => c.name.IndexOf("kaidan", System.StringComparison.OrdinalIgnoreCase) >= 0 && c.name.Contains("_d"))
                .Where(c => Vector3.Angle(c.transform.up, Vector3.up) > 8f && Vector3.Angle(c.transform.up, Vector3.up) < 45f)
                .OrderBy(c => Vector3.Angle(c.transform.up, Vector3.up)).FirstOrDefault();
            string name = am.GetFemaleMicroAssetNames().First(n => n.ToLowerInvariant().Contains("unitychan"));
            EntityBase micro = null;
            GameController.LocalClient.SpawnMicro(am.GetAssetDescriptionByName(name), KoikatsuMaps.LastSpawn ?? Vector3.zero, Quaternion.identity, 1f, e => micro = e);
            while (micro == null)
                yield return null;
            micro.MeshHeight = Sbox.MeterToScale(1.65f);
            yield return new WaitForSeconds(1f);
            if (micro is Humanoid hm && hm.ai != null)
                hm.ai.DisableAI();
            GameController.LocalClient.Player.PlayAs(micro as IPlayable);
            GameController.playerInputEnabled = true;
            yield return new WaitForSeconds(1.5f);
            var ctrl = FindObjectOfType<NewPlayerMicroController>();
            Rigidbody rb = micro.GetComponent<Rigidbody>();

            // Jump while running: how long until the controller treats us as grounded again
            {
                yield return Drive(ctrl, 0f, 1f, 1.2f);
                float baseY = micro.transform.position.y, t = 0f, landed = -1f, regained = -1f;
                Traverse.Create(ctrl).Field("_jump").SetValue(true);
                Animator anim = ((Humanoid)micro).Animator;
                while (t < 2.5f)
                {
                    SetInput(ctrl, 0f, 1f);
                    yield return new WaitForFixedUpdate();
                    t += Time.fixedDeltaTime;
                    float dy = micro.transform.position.y - baseY;
                    if (landed < 0f && t > 0.2f && dy < 0.02f * micro.MeshHeight)
                        landed = t;
                    if (landed >= 0f && regained < 0f && anim.GetBool("Grounded"))
                        regained = t;
                }
                Plugin.Log.LogInfo($"[SelfTest] jump while running: back on the ground at {landed:0.00} s, controller grounded again at {regained:0.00} s ({(regained - landed) * 1000f:0} ms in between)");
            }

            if (Plugin.TestModels.Value.Contains("synthetic"))
            {
                yield return SyntheticStairs(micro, ctrl, rb);
                yield break;
            }

            if (ramp == null)
            {
                Plugin.Log.LogInfo("[SelfTest] no stair ramp on this map");
                yield return Shot("stairs_end");
                yield break;
            }
            // Low end of the ramp: from its middle, go downhill past its edge
            Bounds b = ramp.bounds;
            // Measure the slope: ramp height a metre either side of its middle along x and z
            float RampY(Vector3 at)
            {
                foreach (RaycastHit rh in Physics.RaycastAll(new Vector3(at.x, b.max.y + 1f, at.z), Vector3.down, b.size.y + 5f, Layers.walkableMask))
                    if (rh.collider == ramp)
                        return rh.point.y;
                return float.NaN;
            }
            float gx = RampY(b.center + Vector3.right) - RampY(b.center - Vector3.right);
            float gz = RampY(b.center + Vector3.forward) - RampY(b.center - Vector3.forward);
            Vector3 slopeDir = -new Vector3(float.IsNaN(gx) ? 0f : gx, 0f, float.IsNaN(gz) ? 0f : gz).normalized; // points downhill
            Plugin.Log.LogInfo($"[SelfTest] ramp slope: rise {gx:0.00} per 2 m along x, {gz:0.00} along z");
            // Lowest point (both ways along the slope) where the ground right below is the ramp
            Vector3 start = b.center;
            float lowest = float.MaxValue;
            for (float d = -15f; d < 15f; d += 0.25f)
            {
                Vector3 p = b.center + slopeDir * d;
                if (Physics.Raycast(new Vector3(p.x, b.max.y + 1f, p.z), Vector3.down, out RaycastHit h, b.size.y + 5f, Layers.walkableMask)
                    && h.collider == ramp && h.point.y < lowest)
                {
                    lowest = h.point.y;
                    start = h.point;
                }
            }
            Plugin.Log.LogInfo($"[SelfTest] starting on the ramp at y {start.y:0.00}");
            micro.transform.position = start + Vector3.up * 0.05f;
            rb.position = micro.transform.position;
            rb.velocity = Vector3.zero;
            yield return new WaitForSecondsRealtime(1f);
            Plugin.Log.LogInfo($"[SelfTest] after placing: timeScale {Time.timeScale}, paused {GameController.Instance.paused}, crushed {((Micro)micro).isCrushed}, dead {((Micro)micro).IsDead}, y {micro.transform.position.y:0.00}");
            Vector3 uphill = -slopeDir;
            float startY = micro.transform.position.y, maxY = startY;
            Vector3 startPos = micro.transform.position;
            int stepsBefore = StepUp.Steps;
            float tt = 0f;
            float realStart = Time.realtimeSinceStartup;
            while (tt < 5f && Time.realtimeSinceStartup - realStart < 15f)
            {
                // Turn "uphill" into stick input relative to wherever the game's camera faces
                Transform ct = Camera.main.transform;
                Vector3 cf = Vector3.ProjectOnPlane(ct.forward, Vector3.up).normalized, cr = Vector3.ProjectOnPlane(ct.right, Vector3.up).normalized;
                Vector2 stick = new Vector2(Vector3.Dot(uphill, cr), Vector3.Dot(uphill, cf)).normalized;
                SetInput(ctrl, stick.x, stick.y);
                yield return new WaitForFixedUpdate();
                tt += Time.fixedDeltaTime;
                maxY = Mathf.Max(maxY, micro.transform.position.y);
            }
            SetInput(ctrl, 0f, 0f);
            {
                Vector3 pp = micro.transform.position;
                float h = micro.MeshHeight;
                foreach (Collider c in Physics.OverlapCapsule(pp + Vector3.up * 0.15f * h, pp + Vector3.up * 0.9f * h, 0.25f * h, Layers.walkableMask))
                    Plugin.Log.LogInfo($"[SelfTest]   near player: '{c.name}' {c.GetType().Name}{(c is MeshCollider mc ? (mc.convex ? " convex" : " concave") : "")} bounds {c.bounds.size} added-by-us {(c is MeshCollider && c.GetComponent<MeshRenderer>() != null)}");
                if (Physics.Raycast(pp + Vector3.up * 0.3f * h, uphill, out RaycastHit block, 0.6f * h, Layers.walkableMask))
                    Plugin.Log.LogInfo($"[SelfTest]   blocked uphill by '{block.collider.name}' at {block.distance:0.00} u, normal {block.normal}");
            }
            // Camera 3 m behind and above the player, looking at them, for the screenshot
            {
                var camGo = new GameObject("SelfTest Camera");
                var c = camGo.AddComponent<Camera>();
                c.depth = 100f;
                c.nearClipPlane = 0.05f;
                Vector3 pp = micro.transform.position + Vector3.up * 0.5f * micro.MeshHeight;
                camGo.transform.position = pp + slopeDir * 1.6f * micro.MeshHeight + Vector3.up * 1.2f * micro.MeshHeight;
                camGo.transform.LookAt(pp);
                foreach (Canvas cv in FindObjectsOfType<Canvas>())
                    cv.enabled = false;
            }
            Plugin.Log.LogInfo($"[SelfTest] stairs '{ramp.name}' (tilt {Vector3.Angle(ramp.transform.up, Vector3.up):0} deg, top at y {b.max.y:0.00}): climbed from y {startY:0.00} to {maxY:0.00} in 5 s, walked {Vector3.ProjectOnPlane(micro.transform.position - startPos, Vector3.up).magnitude:0.00} m flat, step-ups {StepUp.Steps - stepsBefore}");
            yield return Shot("stairs_end");
        }

        // Real stepped stairs (18 cm rise, 28 cm run, like many mod maps) and a 30 degree ramp,
        // built on flat ground; walk up each with the step-up helper off and on.
        private IEnumerator SyntheticStairs(EntityBase micro, NewPlayerMicroController ctrl, Rigidbody rb)
        {
            Vector3 ground = micro.transform.position;
            if (Physics.Raycast(ground + Vector3.up * 2f, Vector3.down, out RaycastHit g, 10f, Layers.walkableMask))
                ground = g.point;
            int mapLayer = LayerMask.NameToLayer("Map");
            Vector3 stairsAt = ground + new Vector3(0f, 0f, 2f), rampAt = ground + new Vector3(6f, 0f, 2f);
            for (int i = 0; i < 10; i++)
            {
                var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
                step.layer = mapLayer;
                step.transform.localScale = new Vector3(1.5f, 0.18f * (i + 1), 0.28f);
                step.transform.position = stairsAt + new Vector3(0f, 0.09f * (i + 1), 0.28f * i);
            }
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.layer = mapLayer;
            ramp.transform.localScale = new Vector3(1.5f, 0.1f, 6f);
            ramp.transform.rotation = Quaternion.Euler(-30f, 0f, 0f);
            ramp.transform.position = rampAt + new Vector3(0f, 1.5f, 2.6f);
            Physics.SyncTransforms();

            foreach (var (label, at) in new[] { ("steps 18 cm", stairsAt), ("ramp 30 deg", rampAt) })
            {
                foreach (float stepSetting in new[] { 0f, 0.15f })
                {
                    Plugin.StepHeight.Value = stepSetting;
                    micro.transform.position = at + new Vector3(0f, 0.05f, -1.2f);
                    rb.position = micro.transform.position;
                    rb.velocity = Vector3.zero;
                    yield return new WaitForSecondsRealtime(0.8f);
                    float startY = micro.transform.position.y, maxY = startY;
                    float t = 0f, real0 = Time.realtimeSinceStartup;
                    while (t < 4f && Time.realtimeSinceStartup - real0 < 12f)
                    {
                        Transform ct = Camera.main.transform;
                        Vector3 cf = Vector3.ProjectOnPlane(ct.forward, Vector3.up).normalized, cr = Vector3.ProjectOnPlane(ct.right, Vector3.up).normalized;
                        Vector2 stick = new Vector2(Vector3.Dot(Vector3.forward, cr), Vector3.Dot(Vector3.forward, cf)).normalized;
                        SetInput(ctrl, stick.x, stick.y);
                        yield return new WaitForFixedUpdate();
                        t += Time.fixedDeltaTime;
                        maxY = Mathf.Max(maxY, micro.transform.position.y);
                    }
                    SetInput(ctrl, 0f, 0f);
                    Plugin.Log.LogInfo($"[SelfTest] {label}, step-up {(stepSetting > 0f ? "ON " : "OFF")}: climbed {maxY - startY:0.00} m of 1.80 m in 4 s");
                }
            }
            yield return Shot("synthetic_stairs");
        }

        // Opens every menu Sizebox has (main menu settings, pause menu and each settings panel)
        // and screenshots it, scrolled to the top and to the bottom. TestModels = file prefix.
        private IEnumerator UiTour(SceneLoader loader)
        {
            string tag = string.IsNullOrEmpty(Plugin.TestModels.Value) ? "ui" : Plugin.TestModels.Value;
            if (tag.StartsWith("edit"))
            {
                yield return EditTour(loader, tag);
                Plugin.Log.LogInfo("[SelfTest] screenshot ui tour done");
                yield break;
            }
            if (tag == "probe")
            {
                yield return UiProbe(loader);
                Plugin.Log.LogInfo("[SelfTest] screenshot ui tour done");
                yield break;
            }

            // Main menu settings
            MenuController menu = FindObjectOfType<MenuController>();
            if (menu != null)
            {
                yield return Shot($"{tag}_00_main_menu");
                AccessTools.Method(typeof(MenuController), "OnSettingsClick").Invoke(menu, null);
                yield return new WaitForSecondsRealtime(1f);
                yield return ShotScrolls($"{tag}_01_main_settings");
                // back to the main menu buttons
                foreach (var v in FindObjectsOfType<SizeboxUI.MainMenuSettingsView>())
                    v.gameObject.SetActive(false);

                // Stage select with the gallery button, the grid, then pick a tile
                foreach (var v in FindObjectsOfType<SizeboxUI.MainMenuSettingsView>(true))
                    v.gameObject.SetActive(false);
                AccessTools.Method(typeof(MenuController), "OnStartClick").Invoke(menu, null);
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot($"{tag}_16_stage_select");
                var gallery = FindObjectsOfType<Button>().FirstOrDefault(b => b.name == "Bucky Stage Gallery");
                gallery?.onClick.Invoke();
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot($"{tag}_17_stage_gallery");
                var pick = FindObjectsOfType<Button>().FirstOrDefault(b => b.transform.parent != null && b.transform.parent.name == "Content" && b.name.Contains("Kitchen"));
                pick?.onClick.Invoke();
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot($"{tag}_18_stage_picked");
                Plugin.Log.LogInfo($"[SelfTest] gallery button {(gallery != null)}, picked {(pick != null)}");
            }

            var stage = loader.GetSceneList().First(s => s.Scene == "Island_SBE");
            AsyncOperation op = loader.LoadScene(stage);
            while (op != null && !op.isDone)
                yield return null;
            while (GameController.Instance == null || GameController.LocalClient == null)
                yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(4f);
            yield return Shot($"{tag}_02_ingame");

            var gui = FindObjectOfType<SizeboxUI.GuiManager>();
            gui.OpenPauseMenu();
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot($"{tag}_03_pause");
            if (tag == "dump")
            {
                DumpUi(gui.Pause.transform, "pause");
                var sb = new System.Text.StringBuilder();
                foreach (Font f in Resources.FindObjectsOfTypeAll<Font>())
                    sb.AppendLine($"font {f.name} dynamic {f.dynamic}");
                foreach (Sprite sp in Resources.FindObjectsOfTypeAll<Sprite>().Take(400))
                    sb.AppendLine($"sprite {sp.name} {sp.rect.size} border {sp.border}");
                File.WriteAllText(Path.Combine(OutDir, "ui_assets.txt"), sb.ToString());
            }
            var pause = gui.Pause;
            var modern = pause.GetComponent<UI.PauseUi>();
            string[] panels = { "OnSettingsClick", "OnVideoClick", "OnLightingClick", "OnAudioClick", "OnControlsClick", "OnSaveClick" };
            int n = 4;
            for (int i = 0; i < panels.Length; i++)
            {
                string p = panels[i];
                Plugin.Log.LogInfo($"[SelfTest] ui tour: {p}");
                try
                {
                    // With the modern menus the first five are tabs of one Settings window
                    if (modern != null && i < 5)
                        modern.OpenTab(i);
                    else
                        AccessTools.Method(pause.GetType(), p).Invoke(pause, null);
                }
                catch (System.Exception e) { Plugin.Log.LogWarning($"[SelfTest] {p}: {e.InnerException?.Message ?? e.Message}"); }
                yield return new WaitForSecondsRealtime(1f);
                if (p == "OnVideoClick")
                    DumpUi(gui.Pause.transform, tag + "_video");
                yield return ShotScrolls($"{tag}_{n:00}_{p.Replace("On", "").Replace("Click", "").ToLowerInvariant()}");
                n++;
                try { pause.CloseIfOpen(); }
                catch (System.Exception e) { Plugin.Log.LogWarning("[SelfTest] close: " + e.Message); }
                yield return new WaitForSecondsRealtime(0.5f);
            }

            // A confirmation prompt (the game's Restart one exists in both versions)
            AccessTools.Method(pause.GetType(), "OnResetClick").Invoke(pause, null);
            yield return new WaitForSecondsRealtime(0.8f);
            DumpUi(FindObjectOfType<UiMessageBox>()?.transform ?? gui.Pause.transform, tag + "_popup");
            yield return Shot($"{tag}_10_prompt");
            foreach (var box in FindObjectsOfType<UiMessageBox>())
                box.Close();
            yield return new WaitForSecondsRealtime(0.5f);

            if (modern != null)
            {
                var change = pause.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.GetComponent<Text>()?.text == "Change Stage");
                change?.onClick.Invoke();
                yield return new WaitForSecondsRealtime(1f);
                DumpUi(gui.Pause.transform, tag + "_stages");
                yield return ShotScrolls($"{tag}_11_change_stage");
                var tile = FindObjectsOfType<Button>().FirstOrDefault(b => b.transform.parent != null && b.transform.parent.name == "Content" && b.name.StartsWith("KK"));
                tile = tile ?? FindObjectsOfType<Button>().FirstOrDefault(b => b.transform.parent != null && b.transform.parent.name == "Content");
                tile?.onClick.Invoke();
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot($"{tag}_12_change_stage_confirm");
                foreach (var box in FindObjectsOfType<UiMessageBox>())
                    box.Close();
                pause.CloseIfOpen();
                yield return new WaitForSecondsRealtime(0.5f);
            }

            // Visit a Koikatsu map so its square thumbnail gets taken, then look at the list again
            var kk = loader.GetSceneList().FirstOrDefault(s => s.AssetData != null && s.Scene.StartsWith("KK - "));
            if (modern != null && kk != null)
            {
                gui.ClosePauseMenu();
                yield return new WaitForSecondsRealtime(0.5f);
                op = loader.LoadScene(kk);
                while (op != null && !op.isDone)
                    yield return null;
                yield return new WaitForSecondsRealtime(10f);
                yield return Shot($"{tag}_13_kk_map");
                gui = FindObjectOfType<SizeboxUI.GuiManager>();
                gui.OpenPauseMenu();
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot($"{tag}_14_kk_pause");
                gui.Pause.GetComponentsInChildren<Button>(true).FirstOrDefault(b => b.GetComponent<Text>()?.text == "Change Stage")?.onClick.Invoke();
                yield return new WaitForSecondsRealtime(1f);
                var search = FindObjectOfType<UI.StagePicker>()?.GetComponentInChildren<InputField>();
                if (search != null)
                    search.text = "KK";
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Shot($"{tag}_15_kk_thumbs");
            }
            yield return Shot($"{tag}_99_done");
            Plugin.Log.LogInfo("[SelfTest] screenshot ui tour done");
        }

        // The in-game edit controls: spawn a micro, select it, open every edit view and screenshot.
        private IEnumerator EditTour(SceneLoader loader, string tag)
        {
            var stage = loader.GetSceneList().First(s => s.Scene == "Island_SBE");
            AsyncOperation op = loader.LoadScene(stage);
            while (op != null && !op.isDone)
                yield return null;
            while (GameController.Instance == null || GameController.LocalClient == null)
                yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(4f);

            AssetManager am = AssetManager.Instance;
            var asset = am.GetFemaleMicroAssetNames().Select(n => am.GetAssetDescriptionByName(n)).FirstOrDefault(a => a != null);
            Transform cam = Camera.main.transform;
            Vector3 pos = cam.position + cam.forward.Flatten().normalized * 4f;
            if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 500f, Layers.walkableMask))
                pos = hit.point;
            EntityBase spawned = null;
            if (asset != null)
                GameController.LocalClient.SpawnMicro(asset, pos, Quaternion.LookRotation(-cam.forward.Flatten().normalized), 1f, e => spawned = e);
            float waited = 0f;
            while (spawned == null && waited < 20f) { waited += 0.5f; yield return new WaitForSecondsRealtime(0.5f); }
            yield return new WaitForSecondsRealtime(1.5f);

            var gui = SizeboxUI.GuiManager.Instance;
            if (spawned != null)
                gui.InterfaceControl.SetSelectedObject(spawned);
            var edit = gui.EditMode;
            yield return new WaitForSecondsRealtime(1f);
            yield return Shot($"{tag}_00_edit");
            DumpUi(edit.transform, tag + "_editmode", true);

            var views = new (string name, System.Action open)[]
            {
                ("scene", () => edit.OnSceneClick()),
                ("macro", () => edit.OnCatalogClick(CatalogCategory.Giantess)),
                ("objects", () => edit.OnCatalogClick(CatalogCategory.Object)),
                ("transform", () => edit.OnTransformClick()),
                ("animation", () => edit.OnAnimationClick()),
                ("pose", () => edit.OnPoseClick()),
                ("morphs", () => edit.OnMorphsClick()),
                ("model", () => edit.OnCharacterClick()),
            };
            int n = 1;
            foreach (var v in views)
            {
                try { v.open(); }
                catch (System.Exception e) { Plugin.Log.LogWarning($"[SelfTest] edit {v.name}: {e.InnerException?.Message ?? e.Message}"); }
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot($"{tag}_{n:00}_{v.name}");
                n++;
            }
            DumpUi(edit.transform, tag + "_editmode_after", true);
        }

        // Clicks through the menus like a player would (pointer clicks, in play mode) and logs the
        // cursor after every step; dumps the dropdown list and the Content path rows.
        private IEnumerator UiProbe(SceneLoader loader)
        {
            void LogCursor(string step) => Plugin.Log.LogInfo($"[SelfTest] cursor after {step}: visible={Cursor.visible} lock={Cursor.lockState}");
            void Click(Component c)
            {
                if (c == null) { Plugin.Log.LogWarning("[SelfTest] probe: nothing to click"); return; }
                var es = UnityEngine.EventSystems.EventSystem.current;
                var data = new UnityEngine.EventSystems.PointerEventData(es) { button = UnityEngine.EventSystems.PointerEventData.InputButton.Left };
                UnityEngine.EventSystems.ExecuteEvents.Execute(c.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerDownHandler);
                UnityEngine.EventSystems.ExecuteEvents.Execute(c.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerUpHandler);
                UnityEngine.EventSystems.ExecuteEvents.Execute(c.gameObject, data, UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            }
            Button ByText(Transform root, string text) => root.GetComponentsInChildren<Button>(false)
                .FirstOrDefault(b => b.GetComponentInChildren<Text>(true)?.text == text);

            MenuController menu = FindObjectOfType<MenuController>();
            if (menu != null)
            {
                AccessTools.Method(typeof(MenuController), "OnSettingsClick").Invoke(menu, null);
                yield return new WaitForSecondsRealtime(1f);
                var mms = FindObjectOfType<SizeboxUI.MainMenuSettingsView>();
                Click(ByText(mms.transform, "Content"));
                yield return new WaitForSecondsRealtime(1f);
                yield return Shot("probe_01_content");
                DumpUi(mms.transform, "probe_content");
                Click(ByText(mms.transform, "Video"));
                yield return new WaitForSecondsRealtime(1f);
                var dd = mms.GetComponentsInChildren<Dropdown>().FirstOrDefault();
                if (dd != null)
                {
                    DumpUi(dd.transform, "probe_dropdown", true);
                    dd.Show();
                }
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Shot("probe_02_main_dropdown");
                var list = GameObject.Find("Dropdown List");
                if (list != null)
                    DumpUi(list.transform, "probe_dropdown_list", true);
                dd?.Hide();
                yield return new WaitForSecondsRealtime(0.5f);
                foreach (var v in FindObjectsOfType<SizeboxUI.MainMenuSettingsView>())
                    v.gameObject.SetActive(false);
            }

            var stage = loader.GetSceneList().First(s => s.Scene == "Island_SBE");
            AsyncOperation op = loader.LoadScene(stage);
            while (op != null && !op.isDone)
                yield return null;
            while (GameController.Instance == null || GameController.LocalClient == null)
                yield return new WaitForSecondsRealtime(0.5f);
            yield return new WaitForSecondsRealtime(4f);
            var gui = FindObjectOfType<SizeboxUI.GuiManager>();
            gui.ToggleEditMode();
            yield return new WaitForSecondsRealtime(1.5f);
            LogCursor("play mode");
            gui.OpenPauseMenu();
            yield return new WaitForSecondsRealtime(1f);
            LogCursor("pause");
            var pause = gui.Pause;
            Click(ByText(pause.transform, "Settings"));
            yield return new WaitForSecondsRealtime(1f);
            LogCursor("settings");
            foreach (string tab in new[] { "Video", "Lighting", "Audio", "Controls", "Game", "Video" })
            {
                Click(ByText(pause.transform, tab));
                yield return new WaitForSecondsRealtime(0.8f);
                LogCursor("tab " + tab);
            }
            var toggle = pause.GetComponentsInChildren<Toggle>().FirstOrDefault(t => t.GetComponentInParent<Dropdown>() == null);
            Click(toggle);
            yield return new WaitForSecondsRealtime(0.5f);
            LogCursor("toggle");
            yield return Shot("probe_03_toggle");
            Click(toggle);
            yield return new WaitForSecondsRealtime(0.5f);
            var dd2 = pause.GetComponentsInChildren<Dropdown>().FirstOrDefault();
            Click(dd2);
            yield return new WaitForSecondsRealtime(0.8f);
            LogCursor("dropdown open");
            yield return Shot("probe_04_pause_dropdown");
            dd2?.Hide();
            yield return new WaitForSecondsRealtime(0.5f);
            LogCursor("dropdown closed");
            Click(ByText(pause.transform, "OK"));
            yield return new WaitForSecondsRealtime(0.8f);
            LogCursor("ok");
            Click(ByText(pause.transform, "Change Stage"));
            yield return new WaitForSecondsRealtime(1f);
            LogCursor("change stage");
            Click(ByText(pause.transform, "Back"));
            yield return new WaitForSecondsRealtime(0.8f);
            LogCursor("back");
            Click(ByText(pause.transform, "Save"));
            yield return new WaitForSecondsRealtime(0.8f);
            LogCursor("save");
            pause.CloseIfOpen();
            yield return new WaitForSecondsRealtime(0.5f);
            Click(ByText(pause.transform, "Restart"));
            yield return new WaitForSecondsRealtime(0.8f);
            LogCursor("restart prompt");
            var box = FindObjectOfType<UiMessageBox>();
            Click(box != null ? ByText(box.transform, "No") : null);
            yield return new WaitForSecondsRealtime(0.8f);
            LogCursor("prompt no");
            Click(ByText(pause.transform, "Resume"));
            yield return new WaitForSecondsRealtime(0.8f);
            LogCursor("resume (hidden is right here)");
            gui.OpenPauseMenu();
            yield return new WaitForSecondsRealtime(1f);
            LogCursor("pause again");
            yield return Shot("probe_05_pause_again");
        }

        private void DumpUi(Transform root, string name, bool inactive = false)
        {
            var sb = new System.Text.StringBuilder();
            void Walk(Transform t, int depth)
            {
                if ((!inactive && !t.gameObject.activeInHierarchy) || depth > 14)
                    return;
                var rt = t as RectTransform;
                string line = new string(' ', depth * 2) + t.name + (t.gameObject.activeSelf ? "" : " (off)") + (rt != null ? $" [{rt.rect.width:0}x{rt.rect.height:0} anchors {rt.anchorMin}-{rt.anchorMax}]" : "");
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c is Toggle tg)
                        line += $" [toggle target={tg.targetGraphic?.name} graphic={tg.graphic?.name}]";
                    if (c is Dropdown dd)
                        line += $" [dropdown template={dd.template?.name} caption={dd.captionText?.name} item={dd.itemText?.name}]";
                    switch (c)
                    {
                        case Text tx: line += $" Text('{(tx.text.Length > 24 ? tx.text.Substring(0, 24) : tx.text)}' font={tx.font?.name} size={tx.fontSize} style={tx.fontStyle} align={tx.alignment} col={ColorUtility.ToHtmlStringRGBA(tx.color)})"; break;
                        case Image im: line += $" Image(sprite={im.sprite?.name} type={im.type} col={ColorUtility.ToHtmlStringRGBA(im.color)})"; break;
                        case GridLayoutGroup g: line += $" Grid(cell={g.cellSize} spacing={g.spacing} pad={g.padding.left},{g.padding.top} cons={g.constraint}/{g.constraintCount})"; break;
                        case HorizontalOrVerticalLayoutGroup l: line += $" {l.GetType().Name}(spacing={l.spacing})"; break;
                        case LayoutElement le: line += $" LE(pref={le.preferredWidth}x{le.preferredHeight} min={le.minHeight})"; break;
                        case ContentSizeFitter: line += " Fitter"; break;
                        case ScrollRect: line += " ScrollRect"; break;
                        case Canvas cv: line += $" Canvas(order={cv.sortingOrder})"; break;
                        case CanvasScaler cs: line += $" Scaler({cs.uiScaleMode} ref={cs.referenceResolution})"; break;
                        case Selectable se: line += $" {se.GetType().Name}"; break;
                        case RawImage ri: line += $" RawImage(tex={ri.texture?.name} col={ColorUtility.ToHtmlStringRGBA(ri.color)})"; break;
                        case Mask mk: line += $" Mask(show={mk.showMaskGraphic})"; break;
                        case RectMask2D: line += " RectMask2D"; break;
                        case Outline: line += " Outline"; break;
                        case Shadow: line += " Shadow"; break;
                    }
                }
                sb.AppendLine(line);
                foreach (Transform ch in t)
                    Walk(ch, depth + 1);
            }
            Transform top = root;
            while (!inactive && top.parent != null)
                top = top.parent;
            Walk(top, 0);
            File.WriteAllText(Path.Combine(OutDir, $"ui_dump_{name}.txt"), sb.ToString());
        }

        // Screenshot, and if there is a scroll view, also at the bottom.
        private IEnumerator ShotScrolls(string name)
        {
            yield return Shot(name);
            ScrollRect scroll = null;
            try
            {
                scroll = FindObjectsOfType<ScrollRect>()
                    .Where(s => s.isActiveAndEnabled && s.content != null && s.content.rect.height > ((RectTransform)(s.viewport != null ? s.viewport : s.transform)).rect.height + 5f)
                    .OrderByDescending(s => s.content.rect.height).FirstOrDefault();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("[SelfTest] scroll lookup: " + e.Message);
            }
            if (scroll != null)
            {
                scroll.verticalNormalizedPosition = 0.5f;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot(name + "_mid");
                scroll.verticalNormalizedPosition = 0f;
                yield return new WaitForSecondsRealtime(0.4f);
                yield return Shot(name + "_bottom");
            }
        }

        private IEnumerator QolProbe()
        {
            var gui = SizeboxUI.GuiManager.Instance;
            var control = gui.InterfaceControl;
            var placement = gui.EditPlacement;
            AssetManager am = AssetManager.Instance;
            AssetDescription asset = am.GetAssetDescriptionByName(am.GetFemaleMicroAssetNames().First(n => n.ToLowerInvariant().Contains("unitychan")));
            void Log(string s) => Plugin.Log.LogInfo("[SelfTest] qol: " + s);
            int Count() => FindObjectsOfType<Micro>().Length;

            // per-stage spawn height
            var h = QolData.Get.HeightsFor(_stage, true);
            h.micro = 1.2f;
            QolData.Save();
            placement.AddMicro(asset, false);
            yield return new WaitForSeconds(3f);
            Micro m = FindObjectsOfType<Micro>().OrderByDescending(x => x.GetInstanceID()).First();
            // place it, like clicking: otherwise the editor keeps the selection glued to the cursor
            AccessTools.Method(typeof(SizeboxUI.EditPlacement), "PlaceObject").Invoke(placement, null);
            Log($"spawn height set 1.2 m -> {GameController.ConvertScaleToHumanReadable(m.MeshHeight)}");
            h.micro = 0f;
            QolData.Save();
            control.SetSelectedObject(m);
            yield return new WaitForSeconds(0.5f);

            // hover label: put the micro in the middle of the view
            Transform cam = Camera.main.transform;
            Vector3 front = cam.position + cam.forward * 4f;
            EntityBase hm = null;
            GameController.LocalClient.SpawnMicro(asset, front - Vector3.up, Quaternion.LookRotation(-cam.forward.Flatten()), 1f, x => hm = x);
            while (hm == null)
                yield return null;
            if (hm is Humanoid hh && hh.ai != null)
                hh.ai.DisableAI();
            hm.Rigidbody.isKinematic = true;
            hm.transform.position = front - Vector3.up * (hm.MeshHeight * 0.5f);
            hm.Rigidbody.position = hm.transform.position;
            yield return new WaitForSeconds(1f);
            QolHost.TestMouse = new Vector2(Screen.width / 2f, Screen.height / 2f);
            yield return new WaitForSeconds(0.5f);
            Log($"hover label: '{QolHost.HoverText}'");
            yield return Shot("qol_hover");
            QolHost.TestMouse = null;

            // undo a move (on the placed micro; the catalog one still follows the cursor)
            m = (Micro)hm;
            control.SetSelectedObject(m);
            yield return new WaitForSeconds(0.5f);
            m.Rigidbody.isKinematic = false;
            if (Physics.Raycast(m.transform.position + Vector3.up * 50f, Vector3.down, out RaycastHit ground, 500f, Layers.walkableMask))
            {
                m.transform.position = ground.point;
                m.Rigidbody.position = ground.point;
            }
            yield return new WaitForSeconds(1.5f);
            Vector3 before = m.transform.position;
            m.transform.position = before + Vector3.right * 3f;
            m.Rigidbody.position = before + Vector3.right * 3f;
            yield return new WaitForSeconds(1f);
            Log($"moved: now before+{m.transform.position - before}");
            Undo.Pop();
            yield return new WaitForSeconds(0.3f);
            Log($"undo move: off by {(m.transform.position - before).magnitude:0.000} (0 = right)");
            Undo.Redo();
            yield return new WaitForSeconds(0.3f);
            Log($"redo move: off by {(m.transform.position - (before + Vector3.right * 3f)).magnitude:0.000} (0 = right); at before+{m.transform.position - before}, rb at before+{m.Rigidbody.position - before}");
            Undo.Pop();
            yield return new WaitForSeconds(0.3f);

            // undo a delete
            int n0 = Count();
            control.DeleteObject();
            yield return new WaitForSeconds(0.3f);
            int n1 = Count();
            Undo.Pop();
            yield return new WaitForSeconds(0.3f);
            Log($"undo delete: micros {n0} -> {n1} after delete -> {Count()} after undo");
            Undo.Redo();
            yield return new WaitForSeconds(0.3f);
            Log($"redo delete: micros {Count()} (want {n1})");
            Undo.Pop();
            yield return new WaitForSeconds(0.3f);
            Log($"undo again: micros {Count()} (want {n0})");
            var simZoom = HarmonyLib.Harmony.GetPatchInfo(AccessTools.Method(typeof(SimCam), "Zoom"));
            Log($"edit camera zoom patched: {simZoom != null && simZoom.Prefixes.Count > 0}");

            // catalog favourite goes first
            var catalog = Resources.FindObjectsOfTypeAll<CatalogView>().FirstOrDefault(c => c.gameObject.scene.IsValid());
            var assets = am.GetModelAssets();
            AssetDescription last = assets[assets.Count - 1];
            QolData.Get.favAssets.Clear();
            QolData.Toggle(QolData.Get.favAssets, CatalogPatches.Key(last));
            if (catalog != null)
            {
                catalog.gameObject.SetActive(true);
                CatalogPatches.Refresh(catalog);
                var first = Traverse.Create(catalog).Field("_activeAssetsArray").GetValue<ThreadSafeList<AssetDescription>>()[0];
                Log($"favourite '{last.AssetName}' first in catalog: {first == last}");
                yield return new WaitForSeconds(0.5f);
                yield return Shot("qol_catalog");
            }
            QolData.Get.favAssets.Clear();
            QolData.Save();

            // photo mode
            var host = QolHost.Instance;
            host.SetPhoto(true);
            yield return new WaitForSeconds(0.5f);
            Log($"photo mode: canvases still visible {FindObjectsOfType<Canvas>().Count(c => c.isRootCanvas && c.enabled && c.gameObject.activeInHierarchy && c.renderMode != RenderMode.WorldSpace)}");
            yield return Shot("qol_photo");
            host.SetPhoto(false);
            yield return new WaitForSeconds(0.5f);

            // quick save, change the scene, quick load
            int saved = Count();
            QolHost.QuickSave();
            yield return new WaitForSeconds(1f);
            placement.AddMicro(asset, false);
            placement.AddMicro(asset, false);
            yield return new WaitForSeconds(2f);
            Log($"micros saved {saved}, now {Count()}, loading...");
            QolHost.QuickLoad();
            yield return new WaitForSeconds(12f);
            Log($"after quick load: micros {Count()} (want {saved})");
            yield return Shot("qol_quickload");
        }

        // Void rescue (walk the player off into nothing) and Restart on a custom stage.
        private IEnumerator GameplayProbe()
        {
            AssetManager am = AssetManager.Instance;
            string name = am.GetFemaleMicroAssetNames().First(n => n.ToLowerInvariant().Contains("unitychan"));
            Vector3 pos = KoikatsuMaps.LastSpawn ?? Camera.main.transform.position;
            if (Physics.Raycast(pos + Vector3.up, Vector3.down, out RaycastHit hit, 50f, Layers.walkableMask))
                pos = hit.point;
            EntityBase me = null;
            GameController.LocalClient.SpawnMicro(am.GetAssetDescriptionByName(name), pos, Quaternion.identity, 1f, x => me = x);
            while (me == null)
                yield return null;
            yield return new WaitForSeconds(1f);
            GameController.LocalClient.Player.PlayAs(me as IPlayable);
            GameController.playerInputEnabled = true;
            yield return new WaitForSeconds(2f);
            Vector3 safe = me.transform.position;
            Plugin.Log.LogInfo($"[SelfTest] gameplay: standing at {safe}");
            Vector3 off = safe + new Vector3(5000f, 0f, 5000f);
            me.Rigidbody.position = off;
            me.transform.position = off;
            float t = 0f;
            while (t < 6f && (me.transform.position - safe).magnitude > 5f)
            {
                yield return new WaitForSeconds(0.25f);
                t += 0.25f;
            }
            Plugin.Log.LogInfo($"[SelfTest] gameplay: void rescue {((me.transform.position - safe).magnitude < 5f ? "OK" : "FAILED")} after {t:0.00} s, now at {me.transform.position}");

            string before = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            int loads = 0;
            UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode> count = (s, m) => loads++;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += count;
            SavedScenesManager.Instance.RestartScene();
            t = 0f;
            while (loads == 0 && t < 30f)
            {
                yield return new WaitForSeconds(0.5f);
                t += 0.5f;
            }
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= count;
            Plugin.Log.LogInfo($"[SelfTest] gameplay: restart {(loads > 0 ? "OK" : "FAILED")}: '{before}' -> '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}' after {t:0.0} s");
            yield return new WaitForSeconds(5f);
            yield return Shot("gameplay_after_restart");
        }

        // Spawn micros the way the catalog does (at InterfaceControl.lastMicroScale) and log
        // every number that goes into their size and the Transform panel's "Size:" label.
        private IEnumerator SpawnSizeProbe()
        {
            var control = SizeboxUI.GuiManager.Instance.InterfaceControl;
            Plugin.Log.LogInfo($"[SelfTest] spawn: startingSize {MapSettingInternal.startingSize} minPlayer {MapSettingInternal.minPlayerSize} mapScale {MapSettingInternal.scale} reference {GameController.ReferenceScale} lastMicroScale {control.lastMicroScale}");
            AssetManager am = AssetManager.Instance;
            var names = am.GetFemaleMicroAssetNames().ToList();
            var picks = new List<string> { names.First(n => n.ToLowerInvariant().Contains("unitychan")) };
            picks.AddRange(names.Where(n => n.StartsWith("KK")).Take(1));
            Transform cam = Camera.main.transform;
            int i = 0;
            foreach (string n in picks)
            {
                Vector3 pos = cam.position + cam.forward.Flatten().normalized * (3f + i * 1.5f);
                if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 200f, Layers.walkableMask))
                    pos = hit.point;
                EntityBase e = null;
                GameController.LocalClient.SpawnMicro(am.GetAssetDescriptionByName(n), pos, Quaternion.identity, control.lastMicroScale, x => e = x);
                float waited = 0f;
                while (e == null && waited < 30f) { waited += 0.5f; yield return new WaitForSeconds(0.5f); }
                yield return new WaitForSeconds(1.5f);
                if (e == null) { Plugin.Log.LogWarning("[SelfTest] spawn: " + n + " never spawned"); continue; }
                float rh = 0f;
                foreach (Renderer r in e.GetComponentsInChildren<Renderer>())
                    rh = Mathf.Max(rh, r.bounds.max.y - e.transform.position.y);
                Plugin.Log.LogInfo($"[SelfTest] spawn {n}: Scale {e.Scale} lossy {e.transform.lossyScale.y} meshHeight {e.meshHeight} MeshHeight {e.MeshHeight} label '{GameController.ConvertScaleToHumanReadable(e.MeshHeight)}' renderer top {rh:0.000} u");
                control.SetSelectedObject(e);
                yield return new WaitForSeconds(0.5f);
                Plugin.Log.LogInfo($"[SelfTest] spawn: after selecting, lastMicroScale {control.lastMicroScale}");
                i++;
            }
            yield return Shot("spawn_" + _stage);
        }

        // Walk the player into another micro for 3 s, once without and once with SolidCharacters,
        // and log how far the other one got shoved.
        private IEnumerator PushProbe()
        {
            AssetManager am = AssetManager.Instance;
            var names = am.GetFemaleMicroAssetNames().ToList();
            Transform cam = Camera.main.transform;
            Vector3 fwd = cam.forward.Flatten().normalized;
            Vector3 start = cam.position + fwd * 4f;
            if (Physics.Raycast(start + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 500f, Layers.walkableMask))
                start = hit.point;
            EntityBase me = null, other = null;
            GameController.LocalClient.SpawnMicro(am.GetAssetDescriptionByName(names[0]), start, Quaternion.LookRotation(fwd), 1f, e => me = e);
            GameController.LocalClient.SpawnMicro(am.GetAssetDescriptionByName(names[Mathf.Min(1, names.Count - 1)]), start + fwd * 1.6f, Quaternion.LookRotation(-fwd), 1f, e => other = e);
            while (me == null || other == null)
                yield return null;
            yield return new WaitForSeconds(1.5f);
            foreach (var e in new[] { me, other })
                if (e is Humanoid h && h.ai != null)
                    h.ai.DisableAI();
            GameController.LocalClient.Player.PlayAs(me as IPlayable);
            GameController.playerInputEnabled = true;
            yield return new WaitForSeconds(1.5f);
            var ctrl = FindObjectOfType<NewPlayerMicroController>();
            Vector3 meStart = me.transform.position;
            // Possessing turns the camera; put the other one 1.6 m ahead of where we'll walk
            Vector3 walk = Camera.main.transform.forward.Flatten().normalized;
            Vector3 otherStart = meStart + walk * 1.6f;

            foreach (bool solid in new[] { false, true })
            {
                Plugin.SolidCharacters.Value = solid;
                yield return new WaitForSeconds(0.6f); // let the id sets refresh
                me.transform.position = meStart;
                other.transform.position = otherStart;
                me.GetComponent<Rigidbody>().velocity = Vector3.zero;
                other.GetComponent<Rigidbody>().velocity = Vector3.zero;
                yield return Drive(ctrl, 0f, 0f, 0.5f);
                Vector3 o0 = other.transform.position, m0 = me.transform.position;
                Plugin.Log.LogInfo($"[SelfTest] push: me {m0} facing {me.transform.forward}, other {o0}, cam fwd {Camera.main.transform.forward}");
                foreach (var e in new[] { me, other })
                    foreach (Collider c in e.GetComponentsInChildren<Collider>())
                        if (c.attachedRigidbody == e.GetComponent<Rigidbody>() && !c.isTrigger)
                            Plugin.Log.LogInfo($"[SelfTest] push: {e.name} collider {c.GetType().Name} layer {LayerMask.LayerToName(c.gameObject.layer)} size {c.bounds.size}");
                int lm = me.gameObject.layer, lo = other.gameObject.layer;
                Plugin.Log.LogInfo($"[SelfTest] push: layers {LayerMask.LayerToName(lm)}/{LayerMask.LayerToName(lo)} ignore={Physics.GetIgnoreLayerCollision(lm, lo)}");
                float minDist = float.MaxValue, t = 0f;
                while (t < 3f)
                {
                    SetInput(ctrl, 0f, 1f);
                    yield return new WaitForFixedUpdate();
                    t += Time.fixedDeltaTime;
                    minDist = Mathf.Min(minDist, (me.transform.position - other.transform.position).Flatten().magnitude);
                }
                Plugin.Log.LogInfo($"[SelfTest] push: closest {minDist:0.00} m");
                yield return Drive(ctrl, 0f, 0f, 0.5f);
                float shoved = (other.transform.position - o0).Flatten().magnitude;
                float walked = (me.transform.position - m0).Flatten().magnitude;
                Plugin.Log.LogInfo($"[SelfTest] push solid={solid}: other moved {shoved:0.00} m, player moved {walked:0.00} m");
            }
            yield return Shot("push_end");
        }

        private static void SetInput(NewPlayerMicroController c, float h, float v)
        {
            Traverse.Create(c).Field("horizontalInput").SetValue(h);
            Traverse.Create(c).Field("verticalInput").SetValue(v);
        }

        private static IEnumerator Drive(NewPlayerMicroController c, float h, float v, float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                SetInput(c, h, v);
                yield return new WaitForFixedUpdate();
                t += Time.fixedDeltaTime;
            }
        }

        private IEnumerator HeightLineup()
        {
            AssetManager am = AssetManager.Instance;
            var assets = new List<(AssetDescription asset, bool gts)>();
            assets.AddRange(am.GetGtsAssetNames().Select(n => (am.GetAssetDescriptionByName(n), true)));
            assets.AddRange(am.GetFemaleMicroAssetNames().Concat(am.GetMaleMicroAssetNames()).Select(n => (am.GetAssetDescriptionByName(n), false)));
            assets.RemoveAll(a => a.asset == null);
            string[] only = Plugin.TestModels.Value.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToArray();
            if (only.Length > 0)
                assets.RemoveAll(a => !only.Any(o => a.asset.AssetName.IndexOf(o, System.StringComparison.OrdinalIgnoreCase) >= 0));
            Plugin.Log.LogInfo($"[SelfTest] spawning {assets.Count} characters at {Plugin.TestHeightMeters.Value} m");

            Transform cam = Camera.main != null ? Camera.main.transform : null;
            Vector3 origin = cam != null ? cam.position + cam.forward.Flatten().normalized * 5f : Vector3.zero;
            bool kkMap = KoikatsuMaps.LastSpawn.HasValue;
            if (kkMap)
            {
                // Koikatsu maps are often indoors: stand on the spawn floor, not on the roof.
                origin = KoikatsuMaps.LastSpawn.Value;
                if (Physics.Raycast(origin + Vector3.up, Vector3.down, out RaycastHit floor, 3f, Layers.walkableMask))
                    origin = floor.point;
            }
            else if (Physics.Raycast(origin + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 500f, Layers.walkableMask))
                origin = hit.point;
            Vector3 right = cam != null ? cam.right.Flatten().normalized : Vector3.right;
            Quaternion face = cam != null ? Quaternion.LookRotation(-cam.forward.Flatten().normalized) : Quaternion.identity;

            float heightUnits = Sbox.MeterToScale(Plugin.TestHeightMeters.Value);
            float spacing = heightUnits * 0.45f;
            var spawned = new List<EntityBase>();
            for (int i = 0; i < assets.Count; i++)
            {
                Vector3 pos = origin + right * ((i - (assets.Count - 1) * 0.5f) * spacing);
                System.Action<EntityBase> done = e => spawned.Add(e);
                if (assets[i].gts)
                    GameController.LocalClient.SpawnGiantess(assets[i].asset, pos, face, 0.01f, done);
                else
                    GameController.LocalClient.SpawnMicro(assets[i].asset, pos, face, 1f, done);
            }

            float waited = 0f;
            while (spawned.Count < assets.Count && waited < 30f)
            {
                yield return new WaitForSeconds(0.25f);
                waited += 0.25f;
            }

            foreach (EntityBase e in spawned)
            {
                if (e is Humanoid h && h.ai != null)
                    h.ai.DisableAI();
                e.MeshHeight = heightUnits;
                Vector3 p = e.transform.position;
                p.y = origin.y;
                e.transform.position = p;
            }
            yield return new WaitForSeconds(2.5f);

            foreach (EntityBase e in spawned)
            {
                float top = LiveTop(e) - e.transform.position.y;
                Plugin.Log.LogInfo($"[SelfTest] {HumanoidHeightPatch.Describe(e)}: feet at y {e.transform.position.y:0.00} (floor {origin.y:0.00}), label {GameController.ConvertScaleToHumanReadable(e.MeshHeight)}, " +
                                   $"rendered top {Sbox.ScaleToMeter(top):0.000} m, head bone {Sbox.ScaleToMeter(HeadY(e) - e.transform.position.y):0.000} m");
            }

            // Side-on camera framing the whole row
            var go = new GameObject("SelfTest Camera");
            var c = go.AddComponent<Camera>();
            c.depth = 100f;
            c.nearClipPlane = heightUnits * 0.01f;
            c.farClipPlane = heightUnits * 1000f;
            c.fieldOfView = 30f;
            float rowWidth = Mathf.Max(spacing * assets.Count, heightUnits * 1.2f);
            Vector3 fwd = cam != null ? cam.forward.Flatten().normalized : Vector3.forward;
            float dist = rowWidth * 0.5f / Mathf.Tan(15f * Mathf.Deg2Rad) / c.aspect * 1.25f + heightUnits;
            float camY = heightUnits * 0.9f;
            if (kkMap)
            {
                c.fieldOfView = 55f;
                dist = Mathf.Max(heightUnits * 1.5f, rowWidth * 0.9f);
                camY = heightUnits * 0.6f;
            }
            go.transform.position = origin - fwd * dist + Vector3.up * camY;
            go.transform.rotation = Quaternion.LookRotation(origin + Vector3.up * camY - go.transform.position);
            // Reference lines at the typed-in height and 5% below it
            Line(origin - right * rowWidth * 0.6f + Vector3.up * heightUnits, origin + right * rowWidth * 0.6f + Vector3.up * heightUnits, Color.red, heightUnits * 0.004f);
            Line(origin - right * rowWidth * 0.6f + Vector3.up * heightUnits * 0.95f, origin + right * rowWidth * 0.6f + Vector3.up * heightUnits * 0.95f, Color.yellow, heightUnits * 0.003f);
            foreach (Canvas canvas in FindObjectsOfType<Canvas>())
                canvas.enabled = false;
            yield return Shot($"heights_{Plugin.Height.Value}_{Sanitize(_stage)}");

            if (Plugin.KoikatsuDebug.Value)
            {
                foreach (Light l in FindObjectsOfType<Light>())
                    Plugin.Log.LogInfo($"[SelfTest] light {l.name} ({l.gameObject.scene.name}) {l.type} on {l.isActiveAndEnabled} int {l.intensity} col {l.color} dir {l.transform.forward} shadows {l.shadows} cull {l.cullingMask} baked {l.bakingOutput.isBaked}/{l.bakingOutput.lightmapBakeType}");
                foreach (Light l in FindObjectsOfType<Light>())
                    l.shadows = LightShadows.None;
                yield return Shot("debug_noshadows");
            }
        }

        private static void Line(Vector3 a, Vector3 b, Color col, float width)
        {
            var go = new GameObject("SelfTest Line");
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startWidth = lr.endWidth = width;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = lr.endColor = col;
        }

        private static float LiveTop(EntityBase e)
        {
            float top = float.NegativeInfinity;
            var mesh = new Mesh();
            foreach (var smr in e.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!smr.enabled || smr.sharedMesh == null)
                    continue;
                mesh.Clear();
                smr.BakeMesh(mesh, true);
                Matrix4x4 m = smr.transform.localToWorldMatrix;
                foreach (Vector3 v in mesh.vertices)
                    top = Mathf.Max(top, m.MultiplyPoint3x4(v).y);
            }
            Destroy(mesh);
            return top;
        }

        private static float HeadY(EntityBase e)
        {
            Animator a = (e as Humanoid)?.Animator;
            Transform head = a != null ? a.GetBoneTransform(HumanBodyBones.Head) : null;
            return head != null ? head.position.y : e.transform.position.y;
        }

        private static void LogSceneStats()
        {
            int renderers = FindObjectsOfType<Renderer>().Length;
            int colliders = FindObjectsOfType<Collider>().Length;
            Plugin.Log.LogInfo($"[SelfTest] scene has {renderers} renderers, {colliders} colliders, MapSettings scale {MapSettingInternal.scale}");
            SimCam sim = FindObjectOfType<SimCam>();
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            if (sim != null && cam != null)
            {
                float above = Physics.Raycast(cam.position, Vector3.down, out RaycastHit g, 1000f) ? g.distance : -1f;
                Plugin.Log.LogInfo($"[SelfTest] free camera {cam.position}, {above:0.00} above ground, move speed {Traverse.Create(sim).Field("_baseSpeed").GetValue<float>():0.00} u/s, enabled {Traverse.Create(sim).Field("_enable").GetValue<bool>()}");
            }
        }

        private IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(OutDir, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForSecondsRealtime(1f);
            Plugin.Log.LogInfo($"[SelfTest] screenshot {path}");
        }

        private static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Replace(' ', '_');
        }
    }
}
