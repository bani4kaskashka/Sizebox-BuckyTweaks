using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace SizeboxTweaks.UI
{
    // One look for every menu: OpenSans only (the game ships it), dark rounded cards, one accent
    // colour, slim controls. Sprites are generated at runtime so nothing has to be shipped.
    internal static class Skin
    {
        // Opaque on purpose: the game blends UI in linear space, where 90% alpha still looks see-through
        internal static readonly Color Card = new Color32(22, 24, 30, 255);
        internal static readonly Color CardSoft = new Color32(30, 33, 41, 255);
        internal static readonly Color Field = new Color32(40, 44, 54, 255);
        internal static readonly Color FieldHover = new Color32(52, 57, 70, 255);
        internal static readonly Color Track = new Color32(58, 63, 76, 255);
        internal static readonly Color Accent = new Color32(110, 150, 255, 255);
        internal static readonly Color AccentDim = new Color32(110, 150, 255, 70);
        internal static readonly Color TextMain = new Color32(232, 235, 242, 255);
        internal static readonly Color TextDim = new Color32(160, 168, 184, 255);
        internal static readonly Color TextHeader = new Color32(120, 158, 255, 255);
        internal static readonly Color Danger = new Color32(240, 100, 100, 255);

        private static Font _regular, _semibold, _bold;
        private static Sprite _rounded, _roundedSmall, _circle;

        internal static Font Regular => _regular ?? (_regular = FindFont("OpenSansRegular", "OpenSans-Regular"));
        internal static Font Semibold => _semibold ?? (_semibold = FindFont("OpenSansSemibold", "OpenSans-Semibold"));
        internal static Font Bold => _bold ?? (_bold = FindFont("OpenSansBold", "OpenSans-Bold"));

        private static Font FindFont(params string[] names)
        {
            Font[] all = Resources.FindObjectsOfTypeAll<Font>();
            foreach (string n in names)
            {
                Font f = all.FirstOrDefault(x => x.name == n);
                if (f != null)
                    return f;
            }
            return all.FirstOrDefault(x => x.name.StartsWith("OpenSans")) ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // 9-sliced rounded rectangle, radius 8 px (or 4 for small fields)
        internal static Sprite Rounded => _rounded ?? (_rounded = MakeRounded(32, 8));
        internal static Sprite RoundedSmall => _roundedSmall ?? (_roundedSmall = MakeRounded(16, 4));
        internal static Sprite Circle => _circle ?? (_circle = MakeRounded(32, 16, false));

        // Small downward triangle for dropdowns
        internal static Sprite Chevron => _chevron ?? (_chevron = MakeChevron());
        private static Sprite _chevron;

        private static Sprite MakeChevron()
        {
            const int w = 20, h = 12;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // inside when |x - centre| < half width at this row (apex at the bottom)
                    float half = (y + 0.5f) / h * (w / 2f);
                    float d = Mathf.Abs(x + 0.5f - w / 2f) - half;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(0.5f - d) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
        }

        private static Sprite MakeRounded(int size, int radius, bool sliced = true)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            float r = radius;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // distance outside the rounded corner, anti-aliased over one pixel
                    float cx = Mathf.Clamp(x + 0.5f, r, size - r);
                    float cy = Mathf.Clamp(y + 0.5f, r, size - r);
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy)) - r;
                    byte a = (byte)(Mathf.Clamp01(0.5f - d) * 255f);
                    px[y * size + x] = new Color32(255, 255, 255, a);
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            Vector4 border = sliced ? new Vector4(radius, radius, radius, radius) : Vector4.zero;
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        }

        internal static void Panel(Image img, Color color, bool small = false)
        {
            if (img == null)
                return;
            img.sprite = small ? RoundedSmall : Rounded;
            img.type = Image.Type.Sliced;
            img.color = color;
        }

        internal static void Text(Text t, Font font, int size, Color color, TextAnchor? align = null)
        {
            if (t == null)
                return;
            t.font = font;
            t.fontStyle = FontStyle.Normal;
            t.fontSize = size;
            t.color = color;
            t.resizeTextForBestFit = false;
            if (align.HasValue)
                t.alignment = align.Value;
            foreach (var fx in t.GetComponents<Shadow>())
                fx.enabled = false;
        }

        internal static void Button(Button b, bool accent = false)
        {
            if (b == null)
                return;
            var img = b.GetComponent<Image>() ?? b.targetGraphic as Image;
            if (img != null)
                Panel(img, accent ? Accent : Field, true);
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.2f);
            colors.selectedColor = new Color(1.1f, 1.1f, 1.15f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.9f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            b.colors = colors;
            foreach (var t in b.GetComponentsInChildren<Text>(true))
                Text(t, Semibold, 14, accent ? Color.white : TextMain, TextAnchor.MiddleCenter);
        }

        // Thin track, accent fill, round handle
        internal static void Slider(Slider s)
        {
            if (s == null)
                return;
            Transform bg = s.transform.Find("Background");
            if (bg != null)
            {
                Panel(bg.GetComponent<Image>(), Track, true);
                var rt = (RectTransform)bg;
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(0f, 4f);
            }
            if (s.fillRect != null)
            {
                Panel(s.fillRect.GetComponent<Image>(), Accent, true);
                var area = s.fillRect.parent as RectTransform;
                if (area != null)
                {
                    area.anchorMin = new Vector2(0f, 0.5f);
                    area.anchorMax = new Vector2(1f, 0.5f);
                    area.sizeDelta = new Vector2(area.sizeDelta.x, 4f);
                }
            }
            if (s.handleRect != null)
            {
                var img = s.handleRect.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = Circle;
                    img.type = Image.Type.Simple;
                    img.color = TextMain;
                }
                s.handleRect.sizeDelta = new Vector2(12f, 0f);
                var area = s.handleRect.parent as RectTransform;
                if (area != null)
                {
                    area.anchorMin = new Vector2(0f, 0.5f);
                    area.anchorMax = new Vector2(1f, 0.5f);
                    area.sizeDelta = new Vector2(area.sizeDelta.x, 12f);
                }
            }
        }

        internal static void Input(InputField f)
        {
            if (f == null)
                return;
            Panel(f.GetComponent<Image>(), Field, true);
            if (f.textComponent != null)
                Text(f.textComponent, Regular, 13, TextMain);
            if (f.placeholder is Text ph)
                Text(ph, Regular, 13, TextDim);
            f.selectionColor = AccentDim;
            f.caretColor = TextMain;
        }

        internal static void Dropdown(Dropdown d)
        {
            if (d == null)
                return;
            var img = d.GetComponent<Image>() ?? d.transform.Find("Background")?.GetComponent<Image>();
            Panel(img, Field, true);
            foreach (var bg in d.GetComponentsInChildren<Image>(true).Where(i => i.name == "Background" && i.transform.parent == d.transform))
                Panel(bg, Field, true);
            if (d.captionText != null)
            {
                Text(d.captionText, Regular, 13, TextMain, TextAnchor.MiddleLeft);
                var crt = d.captionText.rectTransform;
                crt.anchorMin = Vector2.zero;
                crt.anchorMax = Vector2.one;
                crt.offsetMin = new Vector2(10f, 0f);
                crt.offsetMax = new Vector2(-26f, 0f);
            }
            if (d.itemText != null)
                Text(d.itemText, Regular, 13, TextMain);
            if (d.template != null)
            {
                Panel(d.template.GetComponent<Image>(), Card);
                foreach (var t in d.template.GetComponentsInChildren<Toggle>(true))
                {
                    var bg = t.targetGraphic as Image;
                    if (bg != null)
                        bg.color = new Color(0, 0, 0, 0);
                    var colors = t.colors;
                    colors.highlightedColor = FieldHover;
                    colors.selectedColor = FieldHover;
                    colors.normalColor = new Color(1, 1, 1, 0);
                    t.colors = colors;
                    if (t.graphic is Image check)
                        check.color = Accent;
                }
            }
            // The game's prefab draws a white underline (a slider fill) and has no arrow
            foreach (var line in d.GetComponentsInChildren<Image>(true).Where(i => i.transform.parent != null && i.transform.parent.name == "Fill Area"))
                line.enabled = false;
            var arrows = d.GetComponentsInChildren<Image>(true).Where(i => i.name == "Arrow").ToList();
            if (arrows.Count == 0)
            {
                var go = new GameObject("Arrow", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(d.transform, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-10f, 0f);
                rt.sizeDelta = new Vector2(10f, 6f);
                var arrowImg = go.GetComponent<Image>();
                arrowImg.raycastTarget = false;
                arrows.Add(arrowImg);
            }
            foreach (var arrow in arrows)
            {
                arrow.sprite = Chevron;
                arrow.color = TextDim;
            }
        }

        internal static void Toggle(Toggle t)
        {
            if (t == null)
                return;
            if (t.targetGraphic is Image box)
            {
                Panel(box, Track, true);
                box.rectTransform.sizeDelta = new Vector2(16f, 16f);
            }
            if (t.graphic is Image check)
            {
                check.color = Accent;
                check.rectTransform.sizeDelta = new Vector2(14f, 14f);
            }
        }

        internal static void Scrollbar(Scrollbar sb, float width = 6f)
        {
            if (sb == null)
                return;
            var img = sb.GetComponent<Image>();
            if (img != null)
                img.color = new Color(0, 0, 0, 0);
            if (sb.handleRect != null)
                Panel(sb.handleRect.GetComponent<Image>(), new Color32(90, 96, 112, 200), true);
            var rt = (RectTransform)sb.transform;
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        }
    }
}
