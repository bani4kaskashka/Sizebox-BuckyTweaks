using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SizeboxTweaks.UI
{
    // Every Sizebox settings window (Game, Video, Lighting, Audio, Controls, the gun settings,
    // main menu settings) is the same "SettingsMenu" prefab: a title, a 500x450 scroll box and a
    // grid of 400x27 rows built from a handful of row prefabs, in three different fonts. This
    // re-lays it out as one wider card with a label column on the left and every control lined
    // up on the right, in one typeface.
    internal class SettingsSkin : MonoBehaviour
    {
        internal const float CardWidth = 640f, CardHeight = 470f;
        private const float RowHeight = 30f, Pad = 18f;
        private const float ControlWidth = 250f, Gap = 8f;

        private GridLayoutGroup _grid;
        private int _styledCount = -1;
        private bool _frameDone;

        private void Start()
        {
            _grid = GetComponentInChildren<GridLayoutGroup>(true);
            if (!Plugin.UiEnabled.Value || _grid == null || GetComponentInChildren<ScrollRect>(true) == null)
            {
                enabled = false;
                return;
            }
            StyleFrame();
        }

        private void LateUpdate()
        {
            // Rows are added in the view's Start and sometimes later (rebinding, sub-menus)
            if (_grid != null && _grid.transform.childCount != _styledCount)
            {
                foreach (Transform row in _grid.transform)
                    if (row.GetComponent<Styled>() == null)
                        StyleRow(row);
                _styledCount = _grid.transform.childCount;
            }
        }

        private void StyleFrame()
        {
            if (_frameDone)
                return;
            _frameDone = true;
            var scroll = GetComponentInChildren<ScrollRect>(true);
            var card = (RectTransform)scroll.transform;
            card.sizeDelta = new Vector2(CardWidth, CardHeight);
            card.anchoredPosition = new Vector2(0f, -8f);
            Skin.Panel(card.GetComponent<Image>(), Skin.Card);
            scroll.scrollSensitivity = 30f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            // Title: top-left of the card
            Text title = GetComponentsInChildren<Text>(true).FirstOrDefault(t => t.transform.parent == transform);
            if (title != null)
            {
                Skin.Text(title, Skin.Bold, 24, Skin.TextMain, TextAnchor.LowerLeft);
                var rt = title.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(CardWidth, 40f);
                rt.anchoredPosition = new Vector2(0f, card.anchoredPosition.y + CardHeight / 2f + 6f);
            }

            // Scrollbar: slim, hugging the card's right edge
            var sb = GetComponentsInChildren<Scrollbar>(true).FirstOrDefault(s => s.transform.parent == transform);
            if (sb != null)
            {
                Skin.Scrollbar(sb);
                var rt = (RectTransform)sb.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(6f, CardHeight - 24f);
                rt.anchoredPosition = new Vector2(CardWidth / 2f - 9f, card.anchoredPosition.y);
            }

            // Close ("OK") button: under the card on the right, accent
            foreach (Button b in GetComponentsInChildren<Button>(true).Where(b => !b.transform.IsChildOf(card)))
            {
                Skin.Button(b, accent: true);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(120f, 32f);
                rt.anchoredPosition = new Vector2(CardWidth / 2f, card.anchoredPosition.y - CardHeight / 2f - 10f);
                break;
            }

            _grid.cellSize = new Vector2(CardWidth - Pad * 2f, RowHeight);
            _grid.spacing = new Vector2(0f, 2f);
            _grid.padding = new RectOffset((int)Pad, (int)Pad, 14, 14);
            var content = (RectTransform)_grid.transform;
            content.sizeDelta = new Vector2(CardWidth, content.sizeDelta.y);
        }

        internal static void StyleRow(Transform row)
        {
            row.gameObject.AddComponent<Styled>();
            string kind = row.name.Replace("(Clone)", "");
            Text[] texts = row.GetComponentsInChildren<Text>(true);

            if (kind == "Header")
            {
                foreach (Text t in texts)
                {
                    Skin.Text(t, Skin.Bold, 12, Skin.TextHeader, TextAnchor.LowerLeft);
                    t.text = t.text.ToUpperInvariant();
                    Stretch(t.rectTransform, 0f, 4f);
                }
                return;
            }

            // Every control on the row, laid out right to left
            var controls = new List<RectTransform>();
            Toggle rowToggle = row.GetComponent<Toggle>();
            foreach (Transform child in row)
            {
                var sel = child.GetComponent<Selectable>();
                if (sel != null)
                    controls.Add((RectTransform)child);
            }

            // The row label: the first text that isn't inside a control
            Text label = texts.FirstOrDefault(t => t.name == "Label" && t.GetComponentInParent<Selectable>() == (rowToggle as Selectable))
                         ?? texts.FirstOrDefault(t => t.GetComponentInParent<Selectable>() == null || t.GetComponentInParent<Selectable>() == rowToggle);

            if (kind.Contains("Path"))
            {
                StylePath(row, label);
                return;
            }

            if (kind == "ReadOnly")
            {
                // Title on the left, value on the right
                Text value = texts.FirstOrDefault(t => t != label);
                if (label != null)
                {
                    Skin.Text(label, Skin.Semibold, 14, Skin.TextMain, TextAnchor.MiddleLeft);
                    Left(label.rectTransform, 0f, 230f);
                }
                if (value != null)
                {
                    Skin.Text(value, Skin.Regular, 13, Skin.TextDim, TextAnchor.MiddleRight);
                    value.horizontalOverflow = HorizontalWrapMode.Wrap;
                    Right(value.rectTransform, 0f, CardWidth - Pad * 2f - 240f, 24f);
                }
                return;
            }

            if (label != null)
            {
                Skin.Text(label, Skin.Semibold, 14, Skin.TextMain, TextAnchor.MiddleLeft);
                // Toggle labels live inside the checkbox; move them out to the row
                if (rowToggle != null && label.transform.parent != row)
                    label.transform.SetParent(row, false);
                Left(label.rectTransform, 0f, 300f);
            }

            if (rowToggle != null)
            {
                Skin.Toggle(rowToggle);
                if (rowToggle.targetGraphic != null)
                    Right(rowToggle.targetGraphic.rectTransform, 2f, 18f, 18f);
            }

            // Toggle + slider rows: the row itself is the toggle, its box sits at the right edge,
            // so the slider starts left of it
            bool boxed = rowToggle != null && controls.Count > 0;
            float x = boxed ? 18f + Gap + 4f : 0f;
            bool pair = controls.Count >= 2;
            // Placed from the right edge leftwards; a value box goes right of its slider
            controls = controls.OrderBy(c => c.GetComponent<InputField>() != null ? 1 : 0).ToList();
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                RectTransform c = controls[i];
                float w = ControlWidth;
                var input = c.GetComponent<InputField>();
                var slider = c.GetComponent<Slider>();
                var dropdown = c.GetComponent<Dropdown>();
                var button = c.GetComponent<Button>();
                var toggle = c.GetComponent<Toggle>();
                if (pair)
                {
                    if (input != null && controls.Any(o => o.GetComponent<Slider>() != null))
                        w = 64f; // value box next to a slider
                    else if (slider != null)
                        w = ControlWidth - 64f - Gap;
                    else
                        w = (ControlWidth - Gap) / 2f + 40f; // two key binding buttons
                }
                if (boxed && slider != null)
                    w = ControlWidth - x;
                if (toggle != null && c != (RectTransform)row)
                {
                    Skin.Toggle(toggle);
                    w = 18f;
                }
                Right(c, x, w, slider != null ? 16f : 24f);
                x += w + Gap;

                if (input != null) Skin.Input(input);
                if (slider != null) Skin.Slider(slider);
                if (dropdown != null) Skin.Dropdown(dropdown);
                if (button != null)
                {
                    Skin.Button(button);
                    foreach (Text t in c.GetComponentsInChildren<Text>(true))
                        Skin.Text(t, Skin.Regular, 12, Skin.TextMain, TextAnchor.MiddleCenter);
                }
            }

            // A row that is just a button (e.g. "Size Gun Settings"): full width
            Button own = row.GetComponent<Button>();
            if (own != null && controls.Count == 0)
            {
                Skin.Button(own);
                foreach (Text t in texts)
                    Skin.Text(t, Skin.Semibold, 13, Skin.TextMain, TextAnchor.MiddleCenter);
            }
            else if (controls.Count == 0 && rowToggle == null)
            {
                foreach (Text t in texts.Where(t => t != label))
                    Skin.Text(t, Skin.Regular, 13, Skin.TextDim);
            }
        }

        // Path rows: title, a field box with the path in it, and View/Browse buttons on the right.
        // The game keeps the text as a separate child sticking out of the box; put it inside.
        private static void StylePath(Transform row, Text label)
        {
            if (label != null)
            {
                Skin.Text(label, Skin.Semibold, 14, Skin.TextMain, TextAnchor.MiddleLeft);
                Left(label.rectTransform, 0f, 150f);
            }
            float right = 0f;
            foreach (Button b in row.GetComponentsInChildren<Button>(true).Reverse())
            {
                if (b.transform.parent != row)
                    continue;
                Skin.Button(b);
                foreach (Text t in b.GetComponentsInChildren<Text>(true))
                    Skin.Text(t, Skin.Semibold, 12, Skin.TextMain, TextAnchor.MiddleCenter);
                Right((RectTransform)b.transform, right, 60f, 24f);
                right += 60f + Gap;
            }
            InputField input = row.GetComponentInChildren<InputField>(true);
            Transform box = input != null ? (input.transform.parent != row ? input.transform.parent : input.transform) : null;
            if (box == null)
                return;
            var boxRt = (RectTransform)box;
            boxRt.anchorMin = boxRt.anchorMax = new Vector2(0f, 0.5f);
            boxRt.pivot = new Vector2(0f, 0.5f);
            boxRt.anchoredPosition = new Vector2(160f, 0f);
            boxRt.sizeDelta = new Vector2(CardWidth - Pad * 2f - 160f - right, 24f);
            Skin.Panel(box.GetComponent<Image>(), Skin.Field, true);
            if (box.GetComponent<RectMask2D>() == null)
                box.gameObject.AddComponent<RectMask2D>();
            foreach (RectTransform child in box)
            {
                child.anchorMin = Vector2.zero;
                child.anchorMax = Vector2.one;
                child.pivot = new Vector2(0.5f, 0.5f);
                child.offsetMin = new Vector2(8f, 0f);
                child.offsetMax = new Vector2(-8f, 0f);
            }
            if (input.textComponent != null)
            {
                Text value = input.textComponent;
                Skin.Text(value, Skin.Regular, 12, Skin.TextDim);
                value.horizontalOverflow = HorizontalWrapMode.Overflow;
                // long paths: show the end (the folder name), short ones from the left
                float room = boxRt.sizeDelta.x - 16f;
                value.alignment = value.preferredWidth > room ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            }
            if (input.placeholder is Text ph)
            {
                Skin.Text(ph, Skin.Regular, 12, Skin.TextDim, TextAnchor.MiddleLeft);
                ph.fontStyle = FontStyle.Italic;
            }
            input.selectionColor = Skin.AccentDim;
        }

        private static void Left(RectTransform rt, float x, float width)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0f);
            rt.sizeDelta = new Vector2(width, 24f);
        }

        private static void Right(RectTransform rt, float fromRight, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = new Vector2(-fromRight, 0f);
            rt.sizeDelta = new Vector2(width, height);
        }

        private static void Stretch(RectTransform rt, float left, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = Vector2.zero;
        }

        internal class Styled : MonoBehaviour { }
    }

    [HarmonyPatch(typeof(Pause.SettingsView), "Awake")]
    internal static class SettingsSkinPatch
    {
        private static void Postfix(Pause.SettingsView __instance)
        {
            if (__instance.GetComponent<SettingsSkin>() == null)
                __instance.gameObject.AddComponent<SettingsSkin>();
        }
    }
}
