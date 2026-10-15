using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SizeboxTweaks.UI
{
    // Main menu stage select: a small gallery button next to the arrows opens the stage grid
    // (the pause menu's Change Stage window). Picking a tile selects that stage; OK still loads.
    [HarmonyPatch(typeof(UIStageSelect), "Start")]
    internal static class StageSelectGallery
    {
        private static readonly AccessTools.FieldRef<UIStageSelect, Button> NextButton =
            AccessTools.FieldRefAccess<UIStageSelect, Button>("nextStageButton");
        private static readonly AccessTools.FieldRef<UIStageSelect, MenuController> Menu =
            AccessTools.FieldRefAccess<UIStageSelect, MenuController>("menu");

        internal static GameObject Open;

        private static void Postfix(UIStageSelect __instance)
        {
            if (!Plugin.UiEnabled.Value)
                return;
            Button next = NextButton(__instance);
            if (next == null)
                return;
            var nrt = (RectTransform)next.transform;

            Button b = PauseUi.MakeButton(nrt.parent, "", false);
            b.name = "Bucky Stage Gallery";
            var rt = (RectTransform)b.transform;
            rt.anchorMin = nrt.anchorMin;
            rt.anchorMax = nrt.anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(40f, 40f);
            // under the "next" arrow
            Vector2 centre = nrt.anchoredPosition + new Vector2((0.5f - nrt.pivot.x) * nrt.rect.width, (0.5f - nrt.pivot.y) * nrt.rect.height);
            rt.anchoredPosition = centre + new Vector2(0f, -(nrt.rect.height / 2f + 30f));
            var icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            icon.transform.SetParent(rt, false);
            icon.rectTransform.sizeDelta = new Vector2(18f, 18f);
            icon.sprite = Skin.GridIcon;
            icon.color = Skin.TextMain;
            icon.raycastTarget = false;
            b.onClick.AddListener(() => Show(__instance));
        }

        private static void Show(UIStageSelect select)
        {
            if (Open != null)
                return;
            MenuController menu = Menu(select);
            var t = Traverse.Create(menu);
            var stages = t.Field("_listStages").GetValue<List<SceneLoader.SceneInfo>>();
            SceneLoader.SceneInfo shown = menu.GetActiveSceneData();
            Canvas canvas = select.GetComponentInParent<Canvas>();
            Open = StagePicker.Create(canvas.transform, "All Stages", i => i == shown, (info, name) =>
            {
                int index = stages.IndexOf(info);
                if (index >= 0)
                {
                    t.Field("_activeScene").SetValue(index);
                    AccessTools.Method(typeof(UIStageSelect), "ShowStage").Invoke(select, null);
                }
                Close();
            }, Close, true);
            Open.transform.SetAsLastSibling();
        }

        internal static void Close()
        {
            if (Open != null)
                Object.Destroy(Open);
            Open = null;
        }
    }

    // While the grid is open, the stage select must not react to keys (space or enter in the
    // search box would start the game); Esc closes the grid instead.
    [HarmonyPatch(typeof(UIStageSelect))]
    internal static class StageSelectKeysPatch
    {
        [HarmonyPatch("OnSelectKey"), HarmonyPrefix]
        private static bool Select() => StageSelectGallery.Open == null;

        [HarmonyPatch("OnArrowKeys"), HarmonyPrefix]
        private static bool Arrows() => StageSelectGallery.Open == null;

        [HarmonyPatch("OnBackKey"), HarmonyPrefix]
        private static bool Back()
        {
            if (StageSelectGallery.Open == null)
                return true;
            StageSelectGallery.Close();
            return false;
        }
    }
}
