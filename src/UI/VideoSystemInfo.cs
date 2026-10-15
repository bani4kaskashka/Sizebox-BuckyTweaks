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
