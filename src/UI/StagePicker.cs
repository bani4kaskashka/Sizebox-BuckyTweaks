using System.Collections.Generic;
using System.Linq;
using SizeboxUI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SizeboxTweaks.UI
{
    // "Change Stage" from the pause menu: every stage (built in, custom and Koikatsu) as a tile
    // grid with a search box. Picking one asks first, then loads it like Restart does.
    internal class StagePicker : MonoBehaviour
    {
        private const float Width = 720f, Height = 470f, Tile = 124f, Gap = 10f;

        private readonly List<(GameObject Tile, string Name)> _tiles = new List<(GameObject, string)>();
        private PauseView _pause;

        internal static GameObject Create(Transform parent, PauseView pause)
        {
            var go = new GameObject("Bucky Stage Picker", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(Width, Height + 90f);
            go.AddComponent<StagePicker>().Build(pause);
            return go;
        }

        private void Build(PauseView pause)
        {
            _pause = pause;

            var title = NewChild<Text>("Title", transform);
            title.text = "Change Stage";
            Skin.Text(title, Skin.Bold, 24, Skin.TextMain, TextAnchor.LowerLeft);
            Place(title.rectTransform, new Vector2(0f, Height / 2f + 6f), new Vector2(Width, 40f), new Vector2(0.5f, 0f));

            // Search box, top right, level with the title
            var searchBg = NewChild<Image>("Search", transform);
            Place(searchBg.rectTransform, new Vector2(Width / 2f, Height / 2f + 10f), new Vector2(220f, 30f), new Vector2(1f, 0f));
            var search = searchBg.gameObject.AddComponent<InputField>();
            var text = NewChild<Text>("Text", searchBg.transform);
            var hint = NewChild<Text>("Placeholder", searchBg.transform);
            FillX(text.rectTransform, 10f);
            FillX(hint.rectTransform, 10f);
            hint.text = "Search stages";
            text.supportRichText = false;
            search.textComponent = text;
            search.placeholder = hint;
            search.targetGraphic = searchBg;
            Skin.Input(search);
            text.alignment = hint.alignment = TextAnchor.MiddleLeft;
            search.onValueChanged.AddListener(Filter);

            // Card with a scrolling grid of tiles
            var card = NewChild<Image>("Card", transform);
            Place(card.rectTransform, Vector2.zero, new Vector2(Width, Height), new Vector2(0.5f, 0.5f));
            Skin.Panel(card, Skin.Card);
            var scroll = card.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;

            var viewport = NewChild<RectTransform>("Viewport", card.transform);
            Fill(viewport, 8f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = NewChild<RectTransform>("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var grid = content.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(Tile, Tile + 30f);
            grid.spacing = new Vector2(Gap, Gap);
            grid.padding = new RectOffset(12, 12, 10, 10);
            grid.childAlignment = TextAnchor.UpperCenter;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;

            // Slim scrollbar on the right edge of the card
            var sbImg = NewChild<Image>("Scrollbar", card.transform);
            Place(sbImg.rectTransform, new Vector2(Width / 2f - 6f, 0f), new Vector2(6f, Height - 24f), new Vector2(1f, 0.5f));
            var area = NewChild<RectTransform>("Sliding Area", sbImg.transform);
            Fill(area, 0f);
            var handle = NewChild<Image>("Handle", area);
            Fill(handle.rectTransform, 0f);
            var sb = sbImg.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = handle.rectTransform;
            sb.targetGraphic = handle;
            Skin.Scrollbar(sb);
            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            var loader = FindObjectOfType<SceneLoader>();
            string current = SceneManager.GetActiveScene().name;
            if (loader != null)
                foreach (SceneLoader.SceneInfo info in loader.GetSceneList())
                    AddTile(content, loader, info, info.Scene == current);

            // Back, under the card on the right
            Button back = PauseUi.MakeButton(transform, "Back", true);
            Place((RectTransform)back.transform, new Vector2(Width / 2f, -Height / 2f - 10f), new Vector2(120f, 32f), new Vector2(1f, 1f));
            back.onClick.AddListener(() => _pause.CloseIfOpen());
        }

        private void AddTile(Transform content, SceneLoader loader, SceneLoader.SceneInfo info, bool current)
        {
            string name = Pretty(info.Scene);
            var bg = NewChild<Image>(name, content);
            Skin.Panel(bg, current ? Skin.AccentDim : new Color(1f, 1f, 1f, 0f), true);
            var button = bg.gameObject.AddComponent<Button>();
            button.targetGraphic = bg;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 1f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            // hover: tint the frame with the accent
            var hover = bg.gameObject.AddComponent<HoverTint>();
            hover.Target = bg;
            hover.Normal = bg.color;
            hover.Hover = Skin.AccentDim;

            // Square picture, cropped from the middle of whatever shape the thumbnail is
            var pic = NewChild<RawImage>("Picture", bg.transform);
            var prt = pic.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.anchoredPosition = new Vector2(0f, -4f);
            prt.sizeDelta = new Vector2(Tile - 8f, Tile - 8f);
            Sprite s = info.Thumbnail;
            if (s != null && s.texture != null)
            {
                pic.texture = s.texture;
                Rect r = s.textureRect;
                float tw = s.texture.width, th = s.texture.height;
                float side = Mathf.Min(r.width, r.height);
                pic.uvRect = new Rect((r.x + (r.width - side) / 2f) / tw, (r.y + (r.height - side) / 2f) / th, side / tw, side / th);
                pic.color = Color.white;
            }
            else
                pic.color = Skin.Field;

            var label = NewChild<Text>("Name", bg.transform);
            label.text = name;
            Skin.Text(label, Skin.Semibold, 12, current ? Skin.TextHeader : Skin.TextMain, TextAnchor.MiddleCenter);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            var lrt = label.rectTransform;
            lrt.anchorMin = new Vector2(0f, 0f);
            lrt.anchorMax = new Vector2(1f, 0f);
            lrt.pivot = new Vector2(0.5f, 0f);
            lrt.anchoredPosition = new Vector2(0f, 2f);
            lrt.sizeDelta = new Vector2(-6f, 32f);
            if (name.Length > 18)
                label.fontSize = 11;

            button.onClick.AddListener(() => Confirm(loader, info, name));
            _tiles.Add((bg.gameObject, name.ToLowerInvariant()));
        }

        private static void Confirm(SceneLoader loader, SceneLoader.SceneInfo info, string name)
        {
            UiMessageBox box = UiMessageBox.Create($"Load {name}?\nAll unsaved progress will be lost.", "Change Stage");
            box.AddButtonsYesNo(() =>
            {
                if (GameController.Instance != null)
                    GameController.Instance.SetPausedState(false);
                AsyncOperation op = loader.LoadScene(info);
                if (op == null)
                    UiMessageBox.Create($"Unable to load '{name}'.", "Change Stage").Popup();
                box.Close();
            });
            box.Popup();
        }

        private void Filter(string query)
        {
            string q = (query ?? "").Trim().ToLowerInvariant();
            foreach (var (tile, name) in _tiles)
                tile.SetActive(q.Length == 0 || q.Split(' ').All(word => name.Contains(word)));
        }

        internal static string Pretty(string scene)
        {
            if (string.IsNullOrEmpty(scene))
                return "?";
            string s = scene.EndsWith("_SBE") ? scene.Substring(0, scene.Length - 4) : scene;
            return s.Replace('_', ' ').Trim();
        }

        private static T NewChild<T>(string name, Transform parent) where T : Component
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return typeof(T) == typeof(RectTransform) ? go.GetComponent<T>() : go.AddComponent<T>();
        }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size, Vector2 pivot)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static void FillX(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, 0f);
            rt.offsetMax = new Vector2(-inset, 0f);
        }

        private static void Fill(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }

    internal class HoverTint : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
    {
        internal Graphic Target;
        internal Color Normal, Hover;

        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { if (Target != null) Target.color = Hover; }
        public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { if (Target != null) Target.color = Normal; }
    }
}
