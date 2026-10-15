using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using SizeboxUI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SizeboxTweaks.UI
{
    // Pause menu: blurred game behind it, a short list (Resume, Save, Settings, Change Stage,
    // Restart, Main Menu, Quit), Video/Lighting/Audio/Controls moved into Settings as tabs, and a
    // Change Stage window with search and a confirmation prompt.
    internal class PauseUi : MonoBehaviour
    {
        private static readonly (string Tab, string Method)[] Tabs =
        {
            ("Game", "OnSettingsClick"), ("Video", "OnVideoClick"), ("Lighting", "OnLightingClick"),
            ("Audio", "OnAudioClick"), ("Controls", "OnControlsClick")
        };

        private PauseView _pause;
        private Traverse _t;
        private GameObject _buttonPanel;
        private RawImage _backdrop;
        private Texture2D _blurred;
        private CanvasGroup _group;

        internal static void Install(PauseView pause)
        {
            if (!Plugin.UiEnabled.Value || pause.GetComponent<PauseUi>() != null)
                return;
            pause.gameObject.AddComponent<PauseUi>().Build(pause);
        }

        private void Build(PauseView pause)
        {
            _pause = pause;
            _t = Traverse.Create(pause);
            _group = gameObject.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();

            // Backdrop: blurred copy of the screen, behind everything in the pause menu
            var bd = new GameObject("Bucky Backdrop", typeof(RectTransform), typeof(RawImage));
            bd.transform.SetParent(transform, false);
            bd.transform.SetAsFirstSibling();
            var bdr = (RectTransform)bd.transform;
            bdr.anchorMin = Vector2.zero;
            bdr.anchorMax = Vector2.one;
            bdr.offsetMin = bdr.offsetMax = Vector2.zero;
            _backdrop = bd.GetComponent<RawImage>();
            _backdrop.color = new Color(0.55f, 0.57f, 0.62f, 1f);
            _backdrop.raycastTarget = true;

            foreach (Image img in GetComponentsInChildren<Image>(true).Where(i => i.name == "PausePanel"))
                img.color = new Color(0f, 0f, 0f, 0.25f);

            var grid = _t.Field("_buttonLayout").GetValue<GridLayoutGroup>();
            _buttonPanel = grid.gameObject;
            var buttons = grid.GetComponentsInChildren<Button>(true).ToDictionary(b => b.GetComponent<Text>().text, b => b);
            foreach (string gone in new[] { "Video", "Lighting", "Audio", "Controls" })
                if (buttons.TryGetValue(gone, out Button b))
                    DestroyImmediate(b.gameObject);

            if (buttons.TryGetValue("Settings", out Button settings))
            {
                settings.onClick.RemoveAllListeners();
                settings.onClick.AddListener(() => OpenTab(0));
            }
            // Change Stage: a copy of a pause button (runtime listeners don't get copied)
            if (buttons.TryGetValue("Save", out Button save))
            {
                Button change = Instantiate(save, grid.transform, false);
                change.GetComponent<Text>().text = "Change Stage";
                change.onClick.AddListener(OpenStagePicker);
                change.transform.SetSiblingIndex(settings != null ? settings.transform.GetSiblingIndex() + 1 : 3);
            }
            if (buttons.TryGetValue("Restart", out Button restart) && settings != null)
                restart.transform.SetSiblingIndex(grid.transform.childCount - 3);

            StyleList(grid, "Restart", "Main Menu", "Quit Game");
        }

        // A vertical list of the game's text buttons as one dark card. Quiet ones are greyed.
        internal static void StyleList(GridLayoutGroup grid, params string[] quiet)
        {
            Skin.Panel(grid.GetComponent<Image>(), Skin.Card);
            grid.cellSize = new Vector2(240f, 38f);
            grid.spacing = new Vector2(0f, 2f);
            grid.padding = new RectOffset(18, 18, 16, 16);
            foreach (Button b in grid.GetComponentsInChildren<Button>(true))
            {
                Text label = b.GetComponent<Text>() ?? b.GetComponentInChildren<Text>();
                if (label == null)
                    continue;
                bool dim = quiet.Contains(label.text);
                Skin.Text(label, Skin.Semibold, 17, dim ? Skin.TextDim : Color.white, TextAnchor.MiddleCenter);
                // the label is the button's target graphic, so the tint colours the text
                var colors = b.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = Skin.Accent;
                colors.selectedColor = Skin.Accent;
                colors.pressedColor = new Color(0.7f, 0.75f, 1f);
                colors.colorMultiplier = 1f;
                b.colors = colors;
            }
            int count = grid.transform.Cast<Transform>().Count(t => t.gameObject.activeSelf);
            ((RectTransform)grid.transform).sizeDelta = new Vector2(276f, count * 40f + 30f);
        }

        private void OnEnable()
        {
            if (_pause != null)
                StartCoroutine(CaptureBackdrop());
        }

        private void OnDisable()
        {
            ReleaseBlur();
        }

        private void Update()
        {
            // The button list hides while a settings window, the save menu or the stage picker is open
            if (_buttonPanel == null)
                return;
            bool sub = _t.Field("_activeGameObject").GetValue<GameObject>() != null
                       || (_t.Field("_saveMenu").GetValue<GameObject>()?.activeSelf ?? false);
            if (_buttonPanel.activeSelf == sub)
                _buttonPanel.SetActive(!sub);
        }

        // One frame invisible, grab the screen, blur it by bouncing it through smaller render
        // textures (bilinear filtering does the smoothing), then show the menu on top. The result
        // is read back into an RGB texture: the screen's alpha is 0, which the UI would draw as
        // fully transparent.
        private IEnumerator CaptureBackdrop()
        {
            if (!Plugin.UiBlur.Value)
            {
                ShowDim();
                yield break;
            }
            _group.alpha = 0f;
            yield return new WaitForEndOfFrame();
            var temps = new List<RenderTexture>();
            try
            {
                ReleaseBlur();
                int w = Screen.width, h = Screen.height;
                var full = RenderTexture.GetTemporary(w, h, 0);
                temps.Add(full);
                ScreenCapture.CaptureScreenshotIntoRenderTexture(full);
                RenderTexture cur = full;
                for (int i = 0; i < 7; i++)
                {
                    // down 5 times, then back up 2 so the last stretch is smoother
                    if (i < 5) { w = Mathf.Max(8, w / 2); h = Mathf.Max(8, h / 2); }
                    else { w *= 2; h *= 2; }
                    var next = RenderTexture.GetTemporary(w, h, 0);
                    next.filterMode = FilterMode.Bilinear;
                    temps.Add(next);
                    Graphics.Blit(cur, next);
                    cur = next;
                }
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = cur;
                _blurred = new Texture2D(w, h, TextureFormat.RGB24, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                _blurred.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                _blurred.Apply(false);
                RenderTexture.active = prev;
                _backdrop.texture = _blurred;
                // The capture comes out upside down on some graphics APIs
                _backdrop.uvRect = SystemInfo.graphicsUVStartsAtTop ? new Rect(0f, 1f, 1f, -1f) : new Rect(0f, 0f, 1f, 1f);
                _backdrop.color = new Color(0.62f, 0.64f, 0.7f, 1f);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Pause blur: " + e.Message);
                ShowDim();
            }
            finally
            {
                foreach (var rt in temps)
                    RenderTexture.ReleaseTemporary(rt);
            }
            _group.alpha = 1f;
        }

        private void ShowDim()
        {
            _backdrop.texture = null;
            _backdrop.color = new Color(0.05f, 0.06f, 0.08f, 0.85f);
        }

        private void ReleaseBlur()
        {
            if (_blurred != null)
            {
                Destroy(_blurred);
                _blurred = null;
            }
        }

        // ---------- Settings with tabs ----------

        internal void OpenTab(int index)
        {
            _pause.CloseIfOpen();
            AccessTools.Method(typeof(PauseView), Tabs[index].Method).Invoke(_pause, null);
            var panel = _t.Field("_activeGameObject").GetValue<GameObject>();
            if (panel != null)
                AddTabBar(panel, index);
        }

        private void AddTabBar(GameObject panel, int active)
        {
            var bar = new GameObject("Bucky Tabs", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            bar.transform.SetParent(panel.transform, false);
            var rt = (RectTransform)bar.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(SettingsSkin.CardWidth, 34f);
            rt.anchoredPosition = new Vector2(0f, -8f + SettingsSkin.CardHeight / 2f + 8f);
            var layout = bar.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = true;

            // The tab row replaces the window title
            foreach (Text title in panel.GetComponentsInChildren<Text>(true).Where(t => t.transform.parent == panel.transform))
                title.enabled = false; // not SetActive: the game writes its title into the first active Text

            for (int i = 0; i < Tabs.Length; i++)
            {
                int idx = i;
                Button b = MakeButton(bar.transform, Tabs[i].Tab, i == active);
                var le = b.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = 108f;
                b.onClick.AddListener(() => { if (idx != active) OpenTab(idx); });
            }
        }

        internal static Button MakeButton(Transform parent, string text, bool accent)
        {
            var go = new GameObject(text, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var label = new GameObject("Text", typeof(RectTransform), typeof(Text));
            label.transform.SetParent(go.transform, false);
            var lrt = (RectTransform)label.transform;
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            label.GetComponent<Text>().text = text;
            var b = go.GetComponent<Button>();
            b.targetGraphic = go.GetComponent<Image>();
            Skin.Button(b, accent);
            return b;
        }

        // ---------- Change stage ----------

        private void OpenStagePicker()
        {
            _pause.CloseIfOpen();
            var picker = StagePicker.Create(transform, _pause);
            _t.Field("_activeGameObject").SetValue(picker);
        }
    }

    [HarmonyPatch(typeof(MainMenuSettingsView), "Start")]
    internal static class MainMenuSettingsPatch
    {
        private static void Postfix(MainMenuSettingsView __instance)
        {
            if (!Plugin.UiEnabled.Value)
                return;
            var grid = __instance.GetComponentInChildren<GridLayoutGroup>();
            if (grid != null)
                PauseUi.StyleList(grid, "Back");
        }
    }

    [HarmonyPatch(typeof(PauseView), "Awake")]
    internal static class PauseViewPatch
    {
        private static void Postfix(PauseView __instance)
        {
            try
            {
                PauseUi.Install(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Pause menu rework failed: " + e);
            }
        }
    }
}
