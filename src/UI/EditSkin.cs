using System;
using System.Collections.Generic;
using HarmonyLib;
using SizeboxUI;
using UnityEngine;
using UnityEngine.UI;

namespace SizeboxTweaks.UI
{
    // The in-game edit controls (bottom bar, catalog, transform, animation, pose, morphs, model,
    // scene tree) are a few hundred hand-placed objects in SF Atarian at many sizes. One pass
    // over all of them: OpenSans a bit smaller, the same sliders, ticks, fields and scrollbars as
    // the menus, and the dark panels in the game's gray. Views open later and lists refill, so it
    // rescans twice a second and only touches what it hasn't seen.
    internal class EditSkin : MonoBehaviour
    {
        private readonly HashSet<int> _done = new HashSet<int>();
        private float _next;

        private void Update()
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 0.5f;
            try
            {
                Scan();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Edit controls skin: " + e.Message);
                enabled = false;
            }
        }

        private bool First(UnityEngine.Object o) => _done.Add(o.GetInstanceID());

        private void Scan()
        {
            foreach (Text t in GetComponentsInChildren<Text>(true))
                if (First(t))
                    StyleText(t);
            foreach (Slider s in GetComponentsInChildren<Slider>(true))
                if (First(s))
                    Skin.Slider(s);
            foreach (Toggle t in GetComponentsInChildren<Toggle>(true))
                if (First(t) && t.GetComponentInParent<Dropdown>() == null)
                    Skin.Toggle(t);
            foreach (InputField f in GetComponentsInChildren<InputField>(true))
                if (First(f) && f.GetComponent<Image>() != null)
                    Skin.Input(f);
            foreach (Dropdown d in GetComponentsInChildren<Dropdown>(true))
                if (First(d))
                    Skin.Dropdown(d);
            foreach (Scrollbar sb in GetComponentsInChildren<Scrollbar>(true))
                if (First(sb))
                    Skin.Scrollbar(sb, 8f);
            foreach (Image img in GetComponentsInChildren<Image>(true))
                if (First(img))
                    StylePanel(img);
        }

        private static void StyleText(Text t)
        {
            Font f = t.font;
            if (f == null)
                return;
            string name = f.name;
            bool atarian = name.Contains("Atarian");
            if (!atarian && !name.StartsWith("OpenSans"))
                return; // icons, symbols, monospace: leave them
            int size = atarian ? Map(t.fontSize) : t.fontSize;
            // Thumbnail captions in the catalog sit on 64 px tiles
            if (t.transform.parent != null && t.transform.parent.name.StartsWith("Entry"))
                size = 9;
            bool title = atarian && t.fontSize >= 20 && name.Contains("Bold");
            t.font = title ? Skin.Bold : name.Contains("Regular") || name == "OpenSansLight" ? Skin.Regular : Skin.Semibold;
            t.fontStyle = FontStyle.Normal;
            t.fontSize = size;
            if (t.resizeTextForBestFit)
            {
                t.resizeTextMaxSize = atarian ? Map(t.resizeTextMaxSize) : t.resizeTextMaxSize;
                t.resizeTextMinSize = Mathf.Min(t.resizeTextMinSize, 8);
            }
            foreach (var fx in t.GetComponents<Shadow>())
                fx.enabled = false;
        }

        // SF Atarian is a narrow small-caps face; OpenSans needs about 60% of its point size
        private static int Map(int s) =>
            s >= 26 ? 15 : s >= 22 ? 15 : s >= 18 ? 13 : s >= 15 ? 12 : s >= 13 ? 11 : 10;

        // Dark backing panels: the game's gray with rounded corners. Buttons, thumbnails,
        // category headers (recoloured by the game on click) and light overlays are left alone.
        private static void StylePanel(Image img)
        {
            if (img.GetComponent<Selectable>() != null || img.GetComponent<Mask>() != null)
                return;
            Rect r = img.rectTransform.rect;
            if (r.width < 120f || r.height < 30f)
                return;
            Color c = img.color;
            float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
            if (lum > 0.2f || c.a < 0.2f)
                return;
            // Keep the panel's own see-through amount, so the game shows through like before
            Skin.Panel(img, new Color(Skin.Card.r, Skin.Card.g, Skin.Card.b, c.a));
        }
    }

    [HarmonyPatch(typeof(GuiManager), "Start")]
    internal static class EditSkinPatch
    {
        private static void Postfix(GuiManager __instance)
        {
            if (Plugin.UiEnabled.Value && __instance.EditMode != null && __instance.EditMode.GetComponent<EditSkin>() == null)
                __instance.EditMode.gameObject.AddComponent<EditSkin>();
        }
    }
}
