using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using SaveDataStructures;
using SizeboxTweaks.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SizeboxTweaks
{
    // Favourite/recent stages, favourite catalog entries and per-stage spawn heights, kept in
    // BepInEx/config/BuckyTweaks.qol.json.
    [Serializable]
    internal class QolData
    {
        public List<string> favStages = new List<string>();
        public List<string> recentStages = new List<string>();
        public List<string> favAssets = new List<string>();
        public List<SpawnHeight> spawnHeights = new List<SpawnHeight>();

        [Serializable]
        internal class SpawnHeight
        {
            public string stage;
            public float micro;
            public float giantess;
        }

        private static QolData _data;
        private static string FilePath => Path.Combine(Paths.ConfigPath, "BuckyTweaks.qol.json");

        internal static QolData Get
        {
            get
            {
                if (_data != null)
                    return _data;
                try
                {
                    if (File.Exists(FilePath))
                        _data = JsonUtility.FromJson<QolData>(File.ReadAllText(FilePath));
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Could not read " + FilePath + ": " + e.Message);
                }
                return _data ?? (_data = new QolData());
            }
        }

        internal static void Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(Get, true));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not write " + FilePath + ": " + e.Message);
            }
        }

        internal static bool Toggle(List<string> list, string key)
        {
            bool on = !list.Remove(key);
            if (on)
                list.Add(key);
            Save();
            return on;
        }

        internal SpawnHeight HeightsFor(string stage, bool create)
        {
            SpawnHeight h = spawnHeights.FirstOrDefault(x => x.stage == stage);
            if (h == null && create)
                spawnHeights.Add(h = new SpawnHeight { stage = stage });
            return h;
        }
    }

    // Hotkeys (quick save/load, undo, photo mode), the hover height label and the toast.
    internal class QolHost : MonoBehaviour
    {
        internal const string QuickSaveName = "Quicksave.json";
        private static readonly Toast Toast = new Toast("_bucky_qol");

        private Canvas _overlay;
        private Text _hover;
        private float _nextHover;
        internal static Vector2? TestMouse; // self test: pretend the mouse is here
        internal static string HoverText => _instance != null && _instance._hover != null && _instance._hover.transform.parent.gameObject.activeSelf ? _instance._hover.text : null;
        private static QolHost _instance;
        internal static QolHost Instance => _instance;
        private bool _photo;
        private readonly List<GameObject> _hidden = new List<GameObject>();

        internal static void Install(GameObject host) => host.AddComponent<QolHost>();

        internal static void Say(string message) => Toast.Print(message);

        private void Start()
        {
            _instance = this;
            var go = new GameObject("Bucky Overlay", typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(transform, false);
            _overlay = go.GetComponent<Canvas>();
            _overlay.renderMode = RenderMode.ScreenSpaceOverlay;
            _overlay.sortingOrder = 900;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(800f, 600f);
            scaler.matchWidthOrHeight = 1f;

            var bg = new GameObject("Hover", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            var img = bg.GetComponent<Image>();
            Skin.Panel(img, new Color(Skin.Card.r, Skin.Card.g, Skin.Card.b, 0.9f), true);
            img.raycastTarget = false;
            var fit = bg.AddComponent<HorizontalLayoutGroup>();
            fit.padding = new RectOffset(8, 8, 3, 3);
            fit.childControlWidth = fit.childControlHeight = true;
            bg.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            bg.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var rt = (RectTransform)bg.transform;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(bg.transform, false);
            _hover = label.GetComponent<Text>();
            Skin.Text(_hover, Skin.Semibold, 12, Skin.TextMain);
            _hover.raycastTarget = false;
            bg.SetActive(false);
        }

        private static bool Typing()
        {
            GameObject sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            InputField f = sel != null ? sel.GetComponent<InputField>() : null;
            return f != null && f.isFocused;
        }

        private static bool InStage => GameController.Instance != null && GameController.LocalClient != null && SizeboxUI.GuiManager.Instance != null;

        private void Update()
        {
            if (!InStage)
            {
                if (_photo)
                    SetPhoto(false);
                if (_hover != null)
                    _hover.transform.parent.gameObject.SetActive(false);
                return;
            }
            if (!Typing())
            {
                if (Plugin.QuickSaveKey.Value.IsDown())
                    QuickSave();
                else if (Plugin.QuickLoadKey.Value.IsDown())
                    QuickLoad();
                else if (Plugin.RedoKey.Value.IsDown())
                    Undo.Redo(); // checked first: Ctrl+Alt+Z also has Ctrl and Z down
                else if (Plugin.UndoKey.Value.IsDown())
                    Undo.Pop();
                else if (Plugin.PhotoModeKey.Value.IsDown())
                    SetPhoto(!_photo);
            }
            Undo.Track();
            UpdateHover();
        }

        // ---------- quick save / load ----------

        internal static void QuickSave()
        {
            if (GameController.Instance.paused)
                return;
            try
            {
                SavedScenesManager.Instance.SaveScene(QuickSaveName);
                Say("Quick saved");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Quick save failed: " + e);
                Say("Quick save failed");
            }
        }

        internal static void QuickLoad()
        {
            string path = IOManager.GetListLoadableSaveFiles().FirstOrDefault(p => Path.GetFileName(p).Equals(QuickSaveName, StringComparison.OrdinalIgnoreCase));
            if (path == null)
            {
                Say("No quick save yet (" + Plugin.QuickSaveKey.Value + ")");
                return;
            }
            SaveData data = SavedScenesManager.Instance.LoadSaveData(path);
            if (data == null)
            {
                Say("Quick save could not be read");
                return;
            }
            Say("Quick loading...");
            // Reading the save raises the game's "rebuild the scene" flag right away; in a stage
            // (unlike the main menu) the running stage would rebuild itself just before it gets
            // unloaded. Hold the flag until the new stage is in.
            IOManager.Instance.isLoadingData = false;
            UnityEngine.Events.UnityAction<UnityEngine.SceneManagement.Scene, UnityEngine.SceneManagement.LoadSceneMode> once = null;
            once = (scene, mode) =>
            {
                UnityEngine.SceneManagement.SceneManager.sceneLoaded -= once;
                IOManager.Instance.isLoadingData = true;
            };
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += once;
            SavedScenesManager.Instance.LoadScene(data.scene);
        }

        // ---------- photo mode: every menu and HUD hidden until the key is pressed again ----------

        internal void SetPhoto(bool on)
        {
            _photo = on;
            if (on)
            {
                _hidden.Clear();
                foreach (Canvas c in FindObjectsOfType<Canvas>())
                    if (c.isRootCanvas && c != _overlay && c.gameObject.activeSelf && c.renderMode != RenderMode.WorldSpace)
                    {
                        _hidden.Add(c.gameObject);
                        c.gameObject.SetActive(false);
                    }
                _overlay.enabled = false;
            }
            else
            {
                foreach (GameObject g in _hidden)
                    if (g != null)
                        g.SetActive(true);
                _hidden.Clear();
                _overlay.enabled = true;
            }
            if (on)
                Plugin.Log.LogInfo("Photo mode on (" + Plugin.PhotoModeKey.Value + " to leave)");
        }

        // ---------- height label under the mouse (edit mode) ----------

        private void UpdateHover()
        {
            if (_hover == null)
                return;
            GameObject box = _hover.transform.parent.gameObject;
            var gui = SizeboxUI.GuiManager.Instance;
            bool edit = Plugin.HoverHeight.Value && !_photo && gui.EditMode != null && gui.EditMode.gameObject.activeInHierarchy
                        && !GameController.Instance.paused && Camera.main != null
                        && (TestMouse.HasValue || EventSystem.current == null || !EventSystem.current.IsPointerOverGameObject());
            if (!edit)
            {
                box.SetActive(false);
                return;
            }
            if (Time.unscaledTime >= _nextHover)
            {
                _nextHover = Time.unscaledTime + 0.08f;
                Humanoid h = null;
                Ray ray = Camera.main.ScreenPointToRay(TestMouse ?? (Vector2)Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit, 100000f, ~0, QueryTriggerInteraction.Ignore))
                    h = hit.collider.GetComponentInParent<Humanoid>();
                if (h == null || h.MeshHeight <= 0f)
                {
                    box.SetActive(false);
                    return;
                }
                string name = h.asset != null ? h.asset.AssetName : h.name;
                if (name.Length > 28)
                    name = name.Substring(0, 28) + "...";
                _hover.text = $"{name}   {GameController.ConvertScaleToHumanReadable(h.MeshHeight)}";
                box.SetActive(true);
            }
            var rt = (RectTransform)box.transform;
            var canvasRt = (RectTransform)_overlay.transform;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRt, Input.mousePosition, null, out Vector2 local);
            rt.anchoredPosition = local + canvasRt.rect.size * 0.5f + new Vector2(14f, -14f);
        }
    }

    // Ctrl+Z in edit mode: brings back deleted things and undoes moves, rotations and resizes.
    // Ctrl+Alt+Z redoes. Every step, when applied, hands back its opposite for the other list.
    internal static class Undo
    {
        private abstract class Step
        {
            internal EntityBase Entity;
            // Apply and return the step that reverses it (null when it no longer applies)
            internal abstract Step Apply(string verb);
            // Deleted things waiting in a list are hidden, not gone; really destroy them
            internal virtual void Discard() { }
        }

        // The thing is hidden (deleted); applying shows it again
        private class Deleted : Step
        {
            internal override Step Apply(string verb)
            {
                if (Entity == null)
                    return null;
                Entity.gameObject.SetActive(true);
                SizeboxUI.GuiManager.Instance?.InterfaceControl?.SetSelectedObject(Entity);
                QolHost.Say($"{verb}: brought back {Entity.name.Replace("(Clone)", "")}");
                return new Restored { Entity = Entity };
            }

            internal override void Discard()
            {
                if (Entity != null)
                    Entity.DestroyObject();
            }
        }

        // The thing is visible again; applying deletes (hides) it again
        private class Restored : Step
        {
            internal override Step Apply(string verb)
            {
                if (Entity == null || !Entity.gameObject.activeInHierarchy)
                    return null;
                var control = SizeboxUI.GuiManager.Instance?.InterfaceControl;
                if (control != null && control.selectedEntity == Entity)
                    control.SetSelectedObject(null);
                Entity.gameObject.SetActive(false);
                QolHost.Say($"{verb}: deleted {Entity.name.Replace("(Clone)", "")}");
                return new Deleted { Entity = Entity };
            }
        }

        private class Moved : Step
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal float Scale;

            internal static Moved Of(EntityBase e) =>
                new Moved { Entity = e, Position = e.transform.position, Rotation = e.transform.rotation, Scale = e.Scale };

            internal override Step Apply(string verb)
            {
                if (Entity == null || !Entity.gameObject.activeInHierarchy)
                    return null;
                Moved back = Of(Entity);
                if (!Mathf.Approximately(Entity.Scale, Scale))
                    Entity.ChangeScale(Scale);
                Entity.transform.SetPositionAndRotation(Position, Rotation);
                if (Entity.Rigidbody != null)
                {
                    Entity.Rigidbody.position = Position;
                    Entity.Rigidbody.rotation = Rotation;
                    Entity.Rigidbody.velocity = Vector3.zero;
                }
                QolHost.Say(verb);
                return back;
            }
        }

        private const int Max = 30;
        private static readonly List<Step> UndoSteps = new List<Step>();
        private static readonly List<Step> RedoSteps = new List<Step>();

        // The selected thing's last settled pose; a change that settles becomes an undo step
        private static EntityBase _watched;
        private static Vector3 _pos;
        private static Quaternion _rot;
        private static float _scale;
        private static float _changedAt = -1f;

        private static void Add(List<Step> list, Step s)
        {
            list.Add(s);
            while (list.Count > Max)
            {
                list[0].Discard();
                list.RemoveAt(0);
            }
        }

        // A new edit: it goes on the undo list and whatever could be redone is gone
        private static void Push(Step s)
        {
            Add(UndoSteps, s);
            foreach (Step r in RedoSteps)
                r.Discard();
            RedoSteps.Clear();
        }

        internal static void Pop() => Run(UndoSteps, RedoSteps, "Undo", "Nothing to undo");

        internal static void Redo() => Run(RedoSteps, UndoSteps, "Redo", "Nothing to redo");

        private static void Run(List<Step> from, List<Step> to, string verb, string empty)
        {
            var gui = SizeboxUI.GuiManager.Instance;
            if (!Plugin.UndoEnabled.Value || gui?.EditMode == null || !gui.EditMode.gameObject.activeInHierarchy)
                return;
            while (from.Count > 0)
            {
                Step s = from[from.Count - 1];
                from.RemoveAt(from.Count - 1);
                Step back = s.Apply(verb);
                if (back != null)
                {
                    Add(to, back);
                    Watch(gui.InterfaceControl.selectedEntity);
                    return;
                }
            }
            QolHost.Say(empty);
        }

        private static void Watch(EntityBase e)
        {
            _watched = e;
            _changedAt = -1f;
            if (e != null)
            {
                _pos = e.transform.position;
                _rot = e.transform.rotation;
                _scale = e.Scale;
            }
        }

        internal static void Track()
        {
            var gui = SizeboxUI.GuiManager.Instance;
            if (!Plugin.UndoEnabled.Value || gui?.InterfaceControl == null)
                return;
            EntityBase sel = gui.InterfaceControl.selectedEntity;
            if (sel != _watched)
            {
                Watch(sel);
                return;
            }
            if (sel == null)
                return;
            float tol = Mathf.Max(sel.MeshHeight, 0.01f) * 0.002f;
            bool differs = (sel.transform.position - _pos).magnitude > tol || Quaternion.Angle(sel.transform.rotation, _rot) > 0.5f
                           || Mathf.Abs(sel.Scale - _scale) > _scale * 0.002f;
            if (!differs)
            {
                _changedAt = -1f;
                return;
            }
            // Characters move on their own in play mode and when animated; only edits count:
            // commit once the change has settled and no mouse button is held.
            bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (held)
            {
                _changedAt = Time.unscaledTime;
                return;
            }
            if (_changedAt < 0f)
                _changedAt = Time.unscaledTime;
            if (Time.unscaledTime - _changedAt < 0.3f)
                return;
            Push(new Moved { Entity = sel, Position = _pos, Rotation = _rot, Scale = _scale });
            Watch(sel);
        }

        internal static void Deleting(EntityBase e) => Push(new Deleted { Entity = e });

        internal static void Clear()
        {
            foreach (Step s in UndoSteps.Concat(RedoSteps))
                s.Discard();
            UndoSteps.Clear();
            RedoSteps.Clear();
            _watched = null;
        }
    }

    // Delete hides the thing instead (kept for undo; really destroyed once it falls off the list)
    [HarmonyPatch(typeof(InterfaceControl), nameof(InterfaceControl.DeleteObject))]
    internal static class UndoDeletePatch
    {
        private static bool Prefix(InterfaceControl __instance)
        {
            EntityBase e = __instance.selectedEntity;
            if (!Plugin.UndoEnabled.Value || e == null || e.GetComponent<Weld>() != null
                || e == GameController.LocalClient?.Player?.Entity)
                return true;
            __instance.SetSelectedObject(null);
            e.gameObject.SetActive(false);
            Undo.Deleting(e);
            QolHost.Say("Deleted (" + Plugin.UndoKey.Value + " to undo)");
            return false;
        }
    }

    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadScene), typeof(SceneLoader.SceneInfo))]
    internal static class RecentStagePatch
    {
        private static void Prefix(SceneLoader.SceneInfo scene)
        {
            Undo.Clear();
            if (scene == null)
                return;
            var recent = QolData.Get.recentStages;
            recent.Remove(scene.Scene);
            recent.Insert(0, scene.Scene);
            while (recent.Count > 8)
                recent.RemoveAt(recent.Count - 1);
            QolData.Save();
        }
    }

    // Saves store the Unity scene's name, which for custom and Koikatsu stages isn't the name
    // in the stage list, so loading such a save found nothing. Store the list name instead.
    [HarmonyPatch(typeof(SaveData), nameof(SaveData.Create))]
    internal static class SaveStageNamePatch
    {
        private static void Postfix(SaveData __result)
        {
            SceneLoader.SceneInfo last = LastStagePatch.Last;
            if (__result != null && last != null && last.AssetData != null)
                __result.scene = last.Scene;
        }
    }

    // ---------- catalog: favourites first (right-click to star), spawn height per stage ----------

    [HarmonyPatch(typeof(CatalogView))]
    internal static class CatalogPatches
    {
        private static readonly AccessTools.FieldRef<CatalogView, ThreadSafeList<AssetDescription>> Active =
            AccessTools.FieldRefAccess<CatalogView, ThreadSafeList<AssetDescription>>("_activeAssetsArray");
        private static readonly AccessTools.FieldRef<CatalogView, List<CatalogEntry>> Entries =
            AccessTools.FieldRefAccess<CatalogView, List<CatalogEntry>>("_entries");

        internal static string Key(AssetDescription a) => a?.AssetFullName ?? a?.AssetName;

        [HarmonyPatch("UpdateActiveAssetsArray"), HarmonyPostfix]
        private static void Reorder(CatalogView __instance)
        {
            var favs = QolData.Get.favAssets;
            ThreadSafeList<AssetDescription> list = Active(__instance);
            if (favs.Count == 0 || list == null)
                return;
            var sorted = list.OrderBy(a => favs.Contains(Key(a)) ? 0 : 1).ToList();
            var fresh = new ThreadSafeList<AssetDescription>(sorted);
            Active(__instance) = fresh;
            var bar = Traverse.Create(__instance).Field("searchBar").GetValue();
            AccessTools.Method(bar.GetType(), "SetSearchableCollection")?.Invoke(bar, new object[] { fresh, (Func<AssetDescription, string>)(a => a.AssetName) });
        }

        [HarmonyPatch("EnsurePlaceholders"), HarmonyPostfix]
        private static void Stars(CatalogView __instance)
        {
            foreach (CatalogEntry e in Entries(__instance))
                if (e != null && e.GetComponent<CatalogStar>() == null)
                    e.gameObject.AddComponent<CatalogStar>().View = __instance;
        }

        [HarmonyPatch("Awake"), HarmonyPostfix]
        private static void HeightFields(CatalogView __instance)
        {
            var t = Traverse.Create(__instance);
            AddHeightField(t.Field("microOptionsPanel").GetValue<GameObject>(), false);
            AddHeightField(t.Field("giantessOptionsPanel").GetValue<GameObject>(), true);
        }

        internal static void Refresh(CatalogView view)
        {
            AccessTools.Method(typeof(CatalogView), "UpdateActiveAssetsArray").Invoke(view, new object[] { Traverse.Create(view).Field("_currentCategory").GetValue() });
            AccessTools.Method(typeof(CatalogView), "LoadPage").Invoke(view, new object[] { Traverse.Create(view).Field("page").GetValue<int>() });
        }

        // "Height [ 1.70 ] m" at the right of the "Play as spawned" row; empty = normal size
        private static void AddHeightField(GameObject panel, bool giantess)
        {
            if (panel == null || !Plugin.UiEnabled.Value)
                return;
            var row = new GameObject("Bucky Spawn Height", typeof(RectTransform));
            row.transform.SetParent(panel.transform, false);
            var rt = (RectTransform)row.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-8f, -2f);
            rt.sizeDelta = new Vector2(112f, 22f);

            var label = new GameObject("Label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(row.transform, false);
            label.text = "Height";
            Skin.Text(label, Skin.Semibold, 11, Skin.TextDim, TextAnchor.MiddleLeft);
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(0f, 1f);
            lrt.pivot = new Vector2(0f, 0.5f);
            lrt.sizeDelta = new Vector2(44f, 0f);

            var bg = new GameObject("Field", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            bg.transform.SetParent(row.transform, false);
            var brt = bg.rectTransform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 1f);
            brt.offsetMin = new Vector2(44f, 0f);
            brt.offsetMax = Vector2.zero;
            var input = bg.gameObject.AddComponent<InputField>();
            var text = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            var hint = new GameObject("Placeholder", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            foreach (Text x in new[] { text, hint })
            {
                x.transform.SetParent(bg.transform, false);
                var xr = x.rectTransform;
                xr.anchorMin = Vector2.zero;
                xr.anchorMax = Vector2.one;
                xr.offsetMin = new Vector2(6f, 0f);
                xr.offsetMax = new Vector2(-6f, 0f);
            }
            hint.text = "normal";
            input.textComponent = text;
            input.placeholder = hint;
            input.targetGraphic = bg;
            input.contentType = InputField.ContentType.DecimalNumber;
            Skin.Input(input);
            text.fontSize = hint.fontSize = 11;
            text.alignment = hint.alignment = TextAnchor.MiddleLeft;
            var keeper = row.AddComponent<SpawnHeightField>();
            keeper.Input = input;
            keeper.Giantess = giantess;
            input.onEndEdit.AddListener(keeper.Commit);
        }
    }

    // Star on catalog tiles; right-click toggles the favourite
    internal class CatalogStar : MonoBehaviour, IPointerClickHandler
    {
        internal CatalogView View;
        private CatalogEntry _entry;
        private GameObject _star;

        private void Awake()
        {
            _entry = GetComponent<CatalogEntry>();
            var img = new GameObject("Bucky Star", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            img.transform.SetParent(transform, false);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-2f, -2f);
            rt.sizeDelta = new Vector2(12f, 12f);
            img.sprite = Skin.StarIcon;
            img.color = new Color32(255, 200, 70, 255);
            img.raycastTarget = false;
            _star = img.gameObject;
            _star.SetActive(false);
        }

        private void LateUpdate()
        {
            bool fav = _entry != null && _entry.Active && QolData.Get.favAssets.Contains(CatalogPatches.Key(_entry.Asset));
            if (_star.activeSelf != fav)
                _star.SetActive(fav);
        }

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Right || _entry == null || !_entry.Active)
                return;
            bool on = QolData.Toggle(QolData.Get.favAssets, CatalogPatches.Key(_entry.Asset));
            QolHost.Say(on ? "Added to favourites" : "Removed from favourites");
            if (View != null)
                CatalogPatches.Refresh(View);
        }
    }

    internal class SpawnHeightField : MonoBehaviour
    {
        internal InputField Input;
        internal bool Giantess;
        private string _stage;

        private void OnEnable() => Load();

        private void Update()
        {
            string stage = LastStagePatch.Last?.Scene;
            if (stage != _stage && !Input.isFocused)
                Load();
        }

        private void Load()
        {
            _stage = LastStagePatch.Last?.Scene;
            var h = _stage != null ? QolData.Get.HeightsFor(_stage, false) : null;
            float m = h == null ? 0f : Giantess ? h.giantess : h.micro;
            Input.SetTextWithoutNotify(m > 0f ? m.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "");
        }

        internal void Commit(string text)
        {
            if (_stage == null)
                return;
            float.TryParse((text ?? "").Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float m);
            var h = QolData.Get.HeightsFor(_stage, true);
            if (Giantess)
                h.giantess = Mathf.Max(0f, m);
            else
                h.micro = Mathf.Max(0f, m);
            QolData.Save();
            QolHost.Say(m > 0f ? $"New {(Giantess ? "giantesses" : "micros")} here spawn {m:0.##} m tall" : "Spawn at normal size");
        }
    }

    // Catalog spawns: set the height chosen for this stage once the model is in
    [HarmonyPatch(typeof(SizeboxUI.EditPlacement))]
    internal static class SpawnHeightPatches
    {
        [HarmonyPatch("OnMicroSpawned"), HarmonyPostfix]
        private static void Micro(Micro micro) => Apply(micro, false);

        [HarmonyPatch("OnGiantessSpawned"), HarmonyPostfix]
        private static void Giantess(Giantess gts) => Apply(gts, true);

        private static void Apply(EntityBase e, bool giantess)
        {
            string stage = LastStagePatch.Last?.Scene;
            var h = stage != null ? QolData.Get.HeightsFor(stage, false) : null;
            float metres = h == null ? 0f : giantess ? h.giantess : h.micro;
            if (e == null || metres <= 0f)
                return;
            e.StartCoroutine(SetHeight(e, metres));
        }

        private static IEnumerator SetHeight(EntityBase e, float metres)
        {
            float t = 0f;
            while (e != null && e.meshHeight <= 0f && t < 20f)
            {
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return null;
            if (e != null && e.meshHeight > 0f)
                e.MeshHeight = Sbox.MeterToScale(metres);
        }
    }
}
