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
        internal static readonly Color Card = new Color32(66, 66, 66, 255); // the game's own menu gray
        internal static readonly Color CardSoft = new Color32(74, 74, 74, 255);
        internal static readonly Color Field = new Color32(86, 86, 86, 255);
        internal static readonly Color FieldHover = new Color32(102, 102, 102, 255);
        internal static readonly Color Track = new Color32(104, 104, 104, 255);
        internal static readonly Color Accent = new Color32(110, 150, 255, 255);
        internal static readonly Color AccentDim = new Color32(110, 150, 255, 70);
        internal static readonly Color TextMain = new Color32(232, 235, 242, 255);
        internal static readonly Color TextDim = new Color32(184, 184, 184, 255);
        internal static readonly Color TextHeader = new Color32(140, 172, 255, 255);
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

        // Accent square with a white tick, shown over the toggle box when it is on
        internal static Sprite CheckOn => _checkOn ?? (_checkOn = MakeCheck());
        private static Sprite _checkOn;

        private static Sprite MakeCheck()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            Color32 accent = Accent;
            Vector2 a = new Vector2(8.5f, 16.5f), b = new Vector2(13.5f, 11f), c = new Vector2(23.5f, 21.5f);
            float Seg(Vector2 p, Vector2 s0, Vector2 s1)
            {
                Vector2 d = s1 - s0;
                float t = Mathf.Clamp01(Vector2.Dot(p - s0, d) / d.sqrMagnitude);
                return Vector2.Distance(p, s0 + d * t);
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float cx = Mathf.Clamp(p.x, 7f, n - 7f), cy = Mathf.Clamp(p.y, 7f, n - 7f);
                    float box = Mathf.Clamp01(0.5f - (Vector2.Distance(p, new Vector2(cx, cy)) - 7f));
                    float tick = Mathf.Clamp01(0.5f - (Mathf.Min(Seg(p, a, b), Seg(p, b, c)) - 1.9f));
                    Color col = Color.Lerp(accent, Color.white, tick);
                    col.a = box;
                    px[y * n + x] = col;
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        // Four small rounded squares: "show all as a grid"
        internal static Sprite GridIcon => _gridIcon ?? (_gridIcon = MakeGridIcon());
        private static Sprite _gridIcon;

        private static Sprite MakeGridIcon()
        {
            const int n = 32, cell = 13, gap = 6;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = 0f;
                    foreach (int ox in new[] { 0, cell + gap })
                        foreach (int oy in new[] { 0, cell + gap })
                        {
                            var p = new Vector2(x + 0.5f - ox, y + 0.5f - oy);
                            float cx = Mathf.Clamp(p.x, 3f, cell - 3f), cy = Mathf.Clamp(p.y, 3f, cell - 3f);
                            a = Mathf.Max(a, Mathf.Clamp01(0.5f - (Vector2.Distance(p, new Vector2(cx, cy)) - 3f)));
                        }
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

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
                DropdownList(d);
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

        // Sizebox's dropdown template is just the item toggle inside a bare object: no background
        // and no scrolling, so long lists (resolutions) run over everything. Rebuild it the way
        // Unity's own template is laid out: card > viewport > content > item.
        private static void DropdownList(Dropdown d)
        {
            RectTransform template = d.template;
            if (template.GetComponent<ScrollRect>() != null)
                return;
            Toggle item = template.GetComponentInChildren<Toggle>(true);
            if (item == null)
                return;

            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.anchoredPosition = new Vector2(0f, -3f);
            template.sizeDelta = new Vector2(0f, 230f);
            var bg = template.GetComponent<Image>() ?? template.gameObject.AddComponent<Image>();
            Panel(bg, new Color32(78, 78, 78, 255)); // a shade lighter than the window it opens over
            bg.raycastTarget = true;

            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(template, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = new Vector2(4f, 4f);
            viewport.offsetMax = new Vector2(-4f, -4f);
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, 24f);

            var irt = (RectTransform)item.transform;
            irt.SetParent(content, false);
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(1f, 0.5f);
            irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = Vector2.zero;
            irt.sizeDelta = new Vector2(0f, 24f);

            var scroll = template.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            // Row: invisible until hovered, accent bar on the current value
            if (item.targetGraphic is Image row)
            {
                row.sprite = RoundedSmall;
                row.type = Image.Type.Sliced;
                row.color = Color.white;
            }
            var colors = item.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = FieldHover;
            colors.selectedColor = FieldHover;
            colors.pressedColor = Track;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.05f;
            item.colors = colors;
            if (!(item.graphic is Image mark))
            {
                mark = new GameObject("Current", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                mark.transform.SetParent(irt, false);
                item.graphic = mark;
            }
            mark.sprite = RoundedSmall;
            mark.type = Image.Type.Sliced;
            mark.color = Accent;
            mark.raycastTarget = false;
            var mrt = mark.rectTransform;
            mrt.anchorMin = mrt.anchorMax = new Vector2(0f, 0.5f);
            mrt.pivot = new Vector2(0f, 0.5f);
            mrt.anchoredPosition = new Vector2(3f, 0f);
            mrt.sizeDelta = new Vector2(3f, 14f);
            if (d.itemText != null)
            {
                Text(d.itemText, Regular, 13, TextMain, TextAnchor.MiddleLeft);
                var trt = d.itemText.rectTransform;
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.offsetMin = new Vector2(12f, 0f);
                trt.offsetMax = new Vector2(-8f, 0f);
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
                // the game's tick sprite is dark, so tinting it does nothing; use our own
                check.sprite = CheckOn;
                check.type = Image.Type.Simple;
                check.color = Color.white;
                var rt = check.rectTransform;
                if (t.targetGraphic != null && check.transform.parent != t.targetGraphic.transform)
                {
                    // sit exactly on the box wherever the prefab put it
                    rt.SetParent(t.targetGraphic.transform, false);
                }
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(16f, 16f);
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
                Panel(sb.handleRect.GetComponent<Image>(), new Color32(130, 130, 130, 220), true);
            var rt = (RectTransform)sb.transform;
            rt.sizeDelta = new Vector2(width, rt.sizeDelta.y);
        }
    }
}
