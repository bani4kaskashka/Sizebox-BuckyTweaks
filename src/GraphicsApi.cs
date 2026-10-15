using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SizeboxTweaks
{
    public enum GraphicsChoice
    {
        Default,
        Vulkan,
        DirectX11,
        DirectX12
    }

    // Sizebox starts on DirectX 12. The build also ships Vulkan, DX11 and OpenGL shaders, so
    // Unity can run it on those with a launch option (-force-vulkan etc.). The API is picked
    // before any plugin loads, so the only way to switch from inside is to restart once with
    // the option added.
    internal static class GraphicsApi
    {
        private const string RelaunchedFlag = "-bucky-relaunched";

        internal static void Check()
        {
            GraphicsChoice want = Plugin.Graphics.Value;
            GraphicsDeviceType now = SystemInfo.graphicsDeviceType;
            Plugin.Log.LogInfo($"Graphics API: {now} ({SystemInfo.graphicsDeviceName}), setting {want}");
            if (want == GraphicsChoice.Default || Matches(want, now))
                return;

            string[] args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            if (args.Any(a => a.Equals(RelaunchedFlag, StringComparison.OrdinalIgnoreCase)))
            {
                Plugin.Log.LogWarning($"Asked for {want} but the game came up on {now} after restarting; not trying again.");
                return;
            }
            if (args.Any(a => a.StartsWith("-force-", StringComparison.OrdinalIgnoreCase)))
                return; // the user picked an API on the command line themselves

            Relaunch(want, args);
        }

        // From the Video settings: restart now on the picked API (Default = no launch option).
        internal static void RestartWith(GraphicsChoice want)
        {
            string[] args = Environment.GetCommandLineArgs().Skip(1)
                .Where(a => !a.StartsWith("-force-", StringComparison.OrdinalIgnoreCase) && !a.Equals(RelaunchedFlag, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Relaunch(want, args);
        }

        private static void Relaunch(GraphicsChoice want, string[] args)
        {
            string flag = want == GraphicsChoice.Vulkan ? "-force-vulkan"
                : want == GraphicsChoice.DirectX11 ? "-force-d3d11"
                : want == GraphicsChoice.DirectX12 ? "-force-d3d12"
                : null;
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            var all = args.Select(Quote).ToList();
            if (flag != null)
                all.Add(flag);
            all.Add(RelaunchedFlag);
            string joined = string.Join(" ", all);
            Plugin.Log.LogInfo($"Restarting on {want}: {exe} {joined}");
            var psi = new ProcessStartInfo(exe, joined) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
            // Doorstop (the BepInEx loader) marks the process with DOORSTOP_* variables; a child
            // that inherits them thinks BepInEx is already running and starts without any mods.
            foreach (string key in psi.EnvironmentVariables.Keys.Cast<string>().ToList())
                if (key.StartsWith("DOORSTOP", StringComparison.OrdinalIgnoreCase))
                    psi.EnvironmentVariables.Remove(key);
            Process.Start(psi);
            Application.Quit();
        }

        internal static bool IsRunning(GraphicsChoice want) => want == GraphicsChoice.Default
            ? SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12
            : Matches(want, SystemInfo.graphicsDeviceType);

        // "Vulkan 1.3.280", "DirectX 12 (feature level 12.1)", "DirectX 11"
        internal static string Describe()
        {
            string v = SystemInfo.graphicsDeviceVersion ?? "";
            int bracket = v.IndexOf('[');
            string level = null;
            if (bracket >= 0)
            {
                string inside = v.Substring(bracket + 1).TrimEnd(']');
                if (inside.StartsWith("level "))
                    level = inside.Substring(6);
                v = v.Substring(0, bracket).Trim();
            }
            switch (SystemInfo.graphicsDeviceType)
            {
                case GraphicsDeviceType.Direct3D12: return "DirectX 12" + (level != null ? $" (feature level {level})" : "");
                case GraphicsDeviceType.Direct3D11: return "DirectX 11" + (level != null ? $" (feature level {level})" : "");
                case GraphicsDeviceType.Vulkan: return "Vulkan";
                case GraphicsDeviceType.OpenGLCore: return "OpenGL";
                default: return v.Length > 0 ? v : SystemInfo.graphicsDeviceType.ToString();
            }
        }

        private static bool Matches(GraphicsChoice want, GraphicsDeviceType now)
        {
            switch (want)
            {
                case GraphicsChoice.Vulkan: return now == GraphicsDeviceType.Vulkan;
                case GraphicsChoice.DirectX11: return now == GraphicsDeviceType.Direct3D11;
                case GraphicsChoice.DirectX12: return now == GraphicsDeviceType.Direct3D12;
                default: return true;
            }
        }

        private static string Quote(string a) => a.Contains(" ") ? "\"" + a + "\"" : a;
    }
}
