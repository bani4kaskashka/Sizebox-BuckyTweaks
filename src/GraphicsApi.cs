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

            string flag = want == GraphicsChoice.Vulkan ? "-force-vulkan"
                : want == GraphicsChoice.DirectX11 ? "-force-d3d11"
                : "-force-d3d12";
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string joined = string.Join(" ", args.Select(Quote).Concat(new[] { flag, RelaunchedFlag }));
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
