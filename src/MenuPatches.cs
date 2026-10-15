using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SizeboxTweaks
{
    // Long stage names (Koikatsu mod maps) wrap onto a second line in the stage select box and
    // the box's tight line spacing draws both lines on top of each other. Shrink to fit instead.
    [HarmonyPatch(typeof(UIStageSelect), "ShowStage")]
    internal static class StageNamePatch
    {
        private static readonly AccessTools.FieldRef<UIStageSelect, Text> StageText =
            AccessTools.FieldRefAccess<UIStageSelect, Text>("stageText");

        private static int _originalSize;

        private static void Postfix(UIStageSelect __instance)
        {
            Text t = StageText(__instance);
            if (t == null)
                return;
            if (_originalSize == 0)
                _originalSize = t.fontSize;
            // One line, no wrapping, and shrink the font until the name fits the box.
            // (Unity's own best-fit doesn't take on this text.)
            t.resizeTextForBestFit = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            float width = ((RectTransform)t.transform).rect.width * 0.94f;
            t.fontSize = _originalSize;
            var settings = t.GetGenerationSettings(new Vector2(float.MaxValue, float.MaxValue));
            while (t.fontSize > 8)
            {
                settings.fontSize = t.fontSize;
                float needed = t.cachedTextGeneratorForLayout.GetPreferredWidth(t.text, settings) / t.pixelsPerUnit;
                if (needed <= width)
                    break;
                t.fontSize--;
            }
        }
    }

    // "Bucky Tweaks Version: x" under Sizebox's own version line on the main menu.
    [HarmonyPatch(typeof(VersionText), "Start")]
    internal static class VersionLabelPatch
    {
        private static void Postfix(VersionText __instance)
        {
            Text original = __instance.GetComponent<Text>();
            if (original == null || __instance.transform.parent == null)
                return;

            var copy = Object.Instantiate(__instance.gameObject, __instance.transform.parent);
            copy.name = "Bucky Tweaks Version";
            // The copy must not run VersionText again (it would copy itself forever).
            Object.DestroyImmediate(copy.GetComponent<VersionText>());

            Text label = copy.GetComponent<Text>();
            label.text = "Bucky Tweaks Version: " + Plugin.Version;
            // Bottom-left corner, one line, smaller than Sizebox's own version text.
            label.fontSize = Mathf.Max(10, Mathf.RoundToInt(original.fontSize * 0.28f));
            label.resizeTextForBestFit = false;
            label.alignment = TextAnchor.LowerLeft;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var dst = (RectTransform)copy.transform;
            var canvas = dst.GetComponentInParent<Canvas>();
            if (canvas != null)
                dst.SetParent(canvas.transform, false);
            dst.anchorMin = dst.anchorMax = dst.pivot = Vector2.zero;
            dst.sizeDelta = new Vector2(600f, label.fontSize * 1.5f);
            dst.anchoredPosition = new Vector2(12f, 8f);
        }
    }
}
