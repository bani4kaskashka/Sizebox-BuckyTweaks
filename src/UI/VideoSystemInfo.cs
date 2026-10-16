using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using HarmonyLib;
using Pause;
using UnityEngine;
using UnityEngine.UI;

namespace SizeboxTweaks.UI
{
    // The bottom of the Video settings: the game's three info sections (build, system, feature
    // support) become one short "System" list that says which graphics API is really in use,
    // with a picker that switches it and offers to restart right away.
    [HarmonyPatch(typeof(VideoSettingsView), "Start")]
    internal static class VideoSystemInfo
    {
        private static readonly string[] ApiNames = { "Game default (DirectX 12)", "Vulkan", "DirectX 11", "DirectX 12" };

        private static void Postfix(VideoSettingsView __instance)
        {
            if (!Plugin.UiEnabled.Value)
                return;
            try
            {
                Rebuild(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Video system info failed: " + e);
            }
        }

        private static void Rebuild(VideoSettingsView view)
        {
            var grid = Traverse.Create(view).Field("gridGroup").GetValue<GridLayoutGroup>();
            if (grid == null)
                return;

            QualityButtons(Traverse.Create(view).Field("_dropQuality").GetValue<Dropdown>());

            // Drop everything from the game's "Build Information" header down
            bool drop = false;
            foreach (Transform row in grid.transform.Cast<Transform>().ToList())
            {
                if (!drop && row.name.StartsWith("Header") && string.Equals(row.GetComponentInChildren<Text>()?.text, "Build Information", StringComparison.OrdinalIgnoreCase))
                    drop = true;
                if (drop)
                    UnityEngine.Object.DestroyImmediate(row.gameObject);
            }

            Call(view, "AddHeader", "System");

            var dropdown = (Dropdown)Call(view, "AddDropdown", "Graphics API", ApiNames.Select(n => new Dropdown.OptionData(n)).ToList());
            dropdown.value = (int)Plugin.Graphics.Value;
            dropdown.RefreshShownValue();
            Text running = (Text)Call(view, "AddReadOnly", "Running on", RunningText());
            dropdown.onValueChanged.AddListener(v =>
            {
                var want = (GraphicsChoice)v;
                Plugin.Graphics.Value = want;
                if (running != null)
                    running.text = RunningText();
                if (GraphicsApi.IsRunning(want))
                    return;
                UiMessageBox box = UiMessageBox.Create($"The game has to restart to switch to {ApiNames[v]}.\nAll unsaved progress will be lost. Restart now?", "Graphics API");
                box.AddButton("Restart", () => GraphicsApi.RestartWith(want));
                box.AddButton("Later", null);
                box.Popup();
            });

            int vram = SystemInfo.graphicsMemorySize;
            Call(view, "AddReadOnly", "Graphics card", SystemInfo.graphicsDeviceName + (vram > 0 ? $"  {Gb(vram)}" : ""));
            Call(view, "AddReadOnly", "Processor", $"{SystemInfo.processorType.Trim()}  ({SystemInfo.processorCount} threads)");
            Call(view, "AddReadOnly", "Memory", Gb(SystemInfo.systemMemorySize));
            Resolution res = Screen.currentResolution;
            Call(view, "AddReadOnly", "Display", $"{Screen.width} x {Screen.height}, {Mathf.RoundToInt((float)res.refreshRateRatio.value)} Hz");
            Call(view, "AddReadOnly", "System", SystemInfo.operatingSystem);

            var features = new List<string>();
            if (SystemInfo.supportsComputeShaders) features.Add("compute shaders");
            if (SystemInfo.supportsAsyncCompute) features.Add("async compute");
            if (SystemInfo.supportsRayTracing) features.Add("ray tracing");
            Call(view, "AddReadOnly", "GPU features", features.Count > 0 ? Capitalise(string.Join(", ", features)) : "None reported");

            Call(view, "AddHeader", "Versions");
            Call(view, "AddReadOnly", "Sizebox", $"{Application.version}  (Unity {Application.unityVersion}, Mono)");
            Call(view, "AddReadOnly", "Bucky Tweaks", Plugin.Version);
            var px = Chainloader.PluginInfos.Values.FirstOrDefault(p => p.Metadata.Name.Contains("PerspectiveX"));
            if (px != null)
                Call(view, "AddReadOnly", "PerspectiveX", px.Metadata.Version.ToString());
        }

        private static int _lastPreset = -1;

        // The quality preset is an action (it sets a dozen other options at once), not a value,
        // so it's three buttons instead of a dropdown with "-" in it. They still drive the
        // game's own dropdown, so the preset logic is the game's.
        private static void QualityButtons(Dropdown presets)
        {
            if (presets == null || presets.options.Count < 4)
                return;
            Transform row = presets.transform.parent;
            presets.gameObject.SetActive(false);
            var box = new GameObject("Bucky Presets", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            box.transform.SetParent(row, false);
            var rt = (RectTransform)box.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(1f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(250f, 24f);
            var layout = box.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = true;
            var buttons = new List<Button>();
            for (int i = 1; i < 4; i++)
            {
                int index = i;
                Button b = PauseUi.MakeButton(box.transform, presets.options[i].text, index == _lastPreset);
                foreach (Text t in b.GetComponentsInChildren<Text>(true))
                    t.fontSize = 12;
                buttons.Add(b);
                b.onClick.AddListener(() =>
                {
                    _lastPreset = index;
                    presets.value = index; // runs the game's SetQualityPreset
                    for (int k = 0; k < buttons.Count; k++)
                    {
                        Skin.Button(buttons[k], k + 1 == index);
                        foreach (Text t in buttons[k].GetComponentsInChildren<Text>(true))
                            t.fontSize = 12;
                    }
                });
            }
        }

        private static string RunningText()
        {
            string now = GraphicsApi.Describe();
            return GraphicsApi.IsRunning(Plugin.Graphics.Value) ? now : $"{now}, switches after a restart";
        }

        private static string Gb(int mb) => $"{mb / 1024f:0.#} GB";

        private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static object Call(SettingsView view, string method, params object[] args) =>
            AccessTools.Method(typeof(SettingsView), method, args.Select(a => a.GetType()).ToArray()).Invoke(view, args);
    }
}
