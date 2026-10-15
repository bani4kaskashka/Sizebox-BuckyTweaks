using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SizeboxTweaks.UI
{
    // Message boxes and prompts (restart, quit, change stage...) in the same dark card style.
    [HarmonyPatch(typeof(UiPopup), nameof(UiPopup.Popup))]
    internal static class PopupSkin
    {
        private static void Postfix(UiPopup __instance)
        {
            if (!Plugin.UiEnabled.Value || __instance == null || __instance.GetComponent<SettingsSkin.Styled>() != null)
                return;
            try
            {
                Style(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Popup skin: " + e.Message);
            }
        }

        // The game's box is a 400x400 window under a separate grey title bar, mostly empty. Size
        // it to the message and give the title bar the window's colour so they read as one card.
        private static void Compact(UiPopup popup, Image panel, Text message)
        {
            RectTransform bg = panel.rectTransform;
            Image header = popup.title != null ? popup.title.transform.parent.GetComponent<Image>() : null;
            if (header == null || header == panel || header.transform.parent != bg.parent)
                return;
            float width = bg.rect.width;
            var settings = message.GetGenerationSettings(new Vector2(width - 40f, 0f));
            float textH = message.cachedTextGeneratorForLayout.GetPreferredHeight(message.text, settings) / message.pixelsPerUnit;
            const float headerH = 52f, buttonsH = 56f;
            // One card: the title bar's own image is switched off and the bar sits inside the top of the window
            float height = Mathf.Clamp(textH + 24f + buttonsH + headerH, 170f, 460f);
            Vector2 centre = bg.anchoredPosition;
            bg.sizeDelta = new Vector2(bg.sizeDelta.x, height);

            var hr = header.rectTransform;
            hr.anchorMin = hr.anchorMax = bg.anchorMin;
            hr.pivot = new Vector2(0.5f, 1f);
            hr.sizeDelta = new Vector2(width, headerH);
            hr.anchoredPosition = centre + new Vector2(0f, height * (1f - bg.pivot.y));
            header.enabled = false;
            // drawn after the window so the title shows on top of it
            header.transform.SetSiblingIndex(bg.GetSiblingIndex() + 1);
            Skin.Text(popup.title, Skin.Bold, 18, Skin.TextMain, TextAnchor.MiddleLeft);
            var tr = popup.title.rectTransform;
            tr.anchorMin = Vector2.zero;
            tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(20f, 0f);
            tr.offsetMax = new Vector2(-20f, 0f);

            if (popup.messageArea is RectTransform ma)
            {
                ma.anchorMin = new Vector2(0f, 0f);
                ma.anchorMax = new Vector2(1f, 1f);
                ma.offsetMin = new Vector2(20f, buttonsH);
                ma.offsetMax = new Vector2(-20f, -headerH + 4f);
            }
            message.alignment = TextAnchor.UpperLeft;
            if (popup.buttonGrid is RectTransform bgr)
            {
                bgr.anchorMin = new Vector2(0f, 0f);
                bgr.anchorMax = new Vector2(1f, 0f);
                bgr.pivot = new Vector2(0.5f, 0f);
                bgr.offsetMin = new Vector2(20f, 16f);
                bgr.offsetMax = new Vector2(-20f, 16f + 30f);
                if (bgr.GetComponent<GridLayoutGroup>() is GridLayoutGroup g)
                {
                    g.childAlignment = TextAnchor.LowerRight;
                    g.cellSize = new Vector2(100f, 30f);
                    g.spacing = new Vector2(8f, 0f);
                }
            }
        }

        private static void Style(UiPopup popup)
        {
            popup.gameObject.AddComponent<SettingsSkin.Styled>();
            Transform buttons = popup.buttonGrid;

            // The window itself: the biggest image that isn't a button
            Image panel = popup.GetComponentsInChildren<Image>(true)
                .Where(i => buttons == null || !i.transform.IsChildOf(buttons) || i.transform == buttons)
                .Where(i => i.GetComponent<Selectable>() == null)
                .OrderByDescending(i => i.rectTransform.rect.width * i.rectTransform.rect.height)
                .FirstOrDefault();
            // A full-screen image is the dim layer behind the window, not the window
            var canvasRect = ((RectTransform)popup.transform.root).rect;
            foreach (Image img in popup.GetComponentsInChildren<Image>(true).Where(i => i.GetComponent<Selectable>() == null))
            {
                Rect r = img.rectTransform.rect;
                if (r.width >= canvasRect.width - 1f && r.height >= canvasRect.height - 1f)
                {
                    img.sprite = null;
                    img.color = new Color(0f, 0f, 0f, 0.45f);
                    if (img == panel)
                        panel = null;
                }
            }
            if (panel == null)
                panel = popup.GetComponentsInChildren<Image>(true)
                    .Where(i => i.GetComponent<Selectable>() == null && i.color.a > 0.3f && i.sprite != null)
                    .OrderByDescending(i => i.rectTransform.rect.width * i.rectTransform.rect.height)
                    .FirstOrDefault();
            if (panel != null)
                Skin.Panel(panel, Skin.Card);
            if (buttons != null && buttons.GetComponent<Image>() is Image gridBg && gridBg != panel)
                gridBg.color = new Color(0f, 0f, 0f, 0f);

            if (popup.title != null)
                Skin.Text(popup.title, Skin.Bold, 20, Skin.TextMain);
            if (popup is UiMessageBox box && box.message != null)
            {
                Skin.Text(box.message, Skin.Regular, 15, Skin.TextMain);
                box.message.lineSpacing = 1.1f;
                if (panel != null)
                    Compact(popup, panel, box.message);
            }
            if (buttons != null)
            {
                bool first = true;
                foreach (Button b in buttons.GetComponentsInChildren<Button>(true))
                {
                    Skin.Button(b, first);
                    first = false;
                }
            }
        }
    }
}
