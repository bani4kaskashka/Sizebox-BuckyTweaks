using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SizeboxTweaks
{
    public enum HeightMode
    {
        // Top of the skin/head mesh that is skinned to the humanoid skeleton (ignores hair bones, ribbons, hats)
        Body,
        // Highest visible vertex of the whole character (includes hair, ears, hats)
        AllMesh,
        // The game's original behaviour (renderer bounding box, often too tall)
        Vanilla
    }

    public enum DevTest
    {
        Off,
        Heights,
        Scene,
        Movement,
        Drift,
        Menu,
        Stairs,
        UiTour,
        Push
    }

    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "sizebox.tweaks";
        public const string Name = "Bucky Tweaks";
        public const string Version = "1.3.0";

        internal static ManualLogSource Log;

        // Movement (player micro)
        internal static ConfigEntry<bool> MovementEnabled;
        internal static ConfigEntry<float> Acceleration;
        internal static ConfigEntry<float> Deceleration;
        internal static ConfigEntry<float> TurnSpeed;
        internal static ConfigEntry<float> RunSpeed;
        internal static ConfigEntry<float> WalkSpeed;
        internal static ConfigEntry<float> SprintSpeed;
        internal static ConfigEntry<float> AnimationDamp;
        internal static ConfigEntry<float> JumpHeight;
        internal static ConfigEntry<float> StepHeight;
        internal static ConfigEntry<bool> SolidCharacters;
        internal static ConfigEntry<GraphicsChoice> Graphics;
        // Movement (player giantess)
        internal static ConfigEntry<float> GtsSpeedChange;
        internal static ConfigEntry<float> GtsTurnSpeed;
        internal static ConfigEntry<float> GtsSpeedMultiplier;

        // UI
        internal static ConfigEntry<bool> UiEnabled;
        internal static ConfigEntry<bool> UiBlur;

        // Height
        internal static ConfigEntry<HeightMode> Height;
        internal static ConfigEntry<bool> HeightLog;

        // Koikatsu maps
        internal static ConfigEntry<bool> KoikatsuEnabled;
        internal static ConfigEntry<string> KoikatsuDir;
        internal static ConfigEntry<string> KoikatsuSunshineDir;
        internal static ConfigEntry<string> KoikatsuSkip;
        internal static ConfigEntry<bool> KoikatsuDebug;
        internal static ConfigEntry<bool> KoikatsuBakedLighting;
        internal static ConfigEntry<bool> KoikatsuAddColliders;
        internal static ConfigEntry<bool> KoikatsuGiantessThroughProps;
        internal static ConfigEntry<bool> KoikatsuModMaps;

        // Dev self test
        internal static ConfigEntry<DevTest> Test;
        internal static ConfigEntry<string> TestScene;
        internal static ConfigEntry<float> TestHeightMeters;
        internal static ConfigEntry<string> TestModels;

        private void Awake()
        {
            Log = Logger;

            MovementEnabled = Config.Bind("Movement", "Enabled", true,
                "Replace the floaty player movement with snappier values. Off = the game's own values.");
            Acceleration = Config.Bind("Movement", "Acceleration", 10f,
                new ConfigDescription("How fast you reach running speed. Game default 4 (about half a second to get going).", new AcceptableValueRange<float>(1f, 40f)));
            Deceleration = Config.Bind("Movement", "Deceleration", 13f,
                new ConfigDescription("How fast you stop when you let go of the keys. Game default 4 (about half a second of sliding).", new AcceptableValueRange<float>(1f, 40f)));
            TurnSpeed = Config.Bind("Movement", "TurnSpeed", 12f,
                new ConfigDescription("How fast the body turns to face the move direction. Game default 3, which makes you slide sideways while turning.", new AcceptableValueRange<float>(1f, 40f)));
            WalkSpeed = Config.Bind("Movement", "WalkSpeedMultiplier", 0.5f,
                new ConfigDescription("Walk (toggle) speed multiplier. Game default 0.5.", new AcceptableValueRange<float>(0.05f, 3f)));
            RunSpeed = Config.Bind("Movement", "RunSpeedMultiplier", 1f,
                new ConfigDescription("Normal run speed multiplier. Game default 1.", new AcceptableValueRange<float>(0.05f, 5f)));
            SprintSpeed = Config.Bind("Movement", "SprintSpeedMultiplier", 1.38f,
                new ConfigDescription("Sprint speed multiplier. Game default 1.38.", new AcceptableValueRange<float>(0.05f, 10f)));
            AnimationDamp = Config.Bind("Movement", "AnimationBlendTime", 0.15f,
                new ConfigDescription("Seconds the walk/run animation takes to catch up with your speed. Game default 0.65, which looks like ice skating.", new AcceptableValueRange<float>(0.01f, 1f)));
            JumpHeight = Config.Bind("Movement", "JumpHeight", 0.3f,
                new ConfigDescription("How high a micro jumps, as a fraction of their own height (0.3 = about 50 cm for a 1.65 m person). " +
                    "The game's jump goes about 1.25 body heights up.", new AcceptableValueRange<float>(0.05f, 2f)));
            StepHeight = Config.Bind("Movement", "StepHeight", 0.15f,
                new ConfigDescription("Highest ledge a micro steps up onto by just walking, as a fraction of their height (0.15 = about 25 cm, a big stair step). 0 = off.", new AcceptableValueRange<float>(0f, 0.5f)));
            GtsSpeedChange = Config.Bind("Movement (Giantess)", "SpeedChange", 8f,
                new ConfigDescription("How fast a player-controlled giantess speeds up and slows down. Game default 5.", new AcceptableValueRange<float>(1f, 30f)));
            GtsTurnSpeed = Config.Bind("Movement (Giantess)", "TurnSpeed", 10f,
                new ConfigDescription("How fast a player-controlled giantess turns. Game default 10.", new AcceptableValueRange<float>(1f, 40f)));
            // 1.3.0: a touch smoother turning and starting/stopping. Move people still on the
            // old defaults to the new ones; values someone picked themselves stay.
            if (Mathf.Approximately(Acceleration.Value, 12f)) Acceleration.Value = 10f;
            if (Mathf.Approximately(Deceleration.Value, 16f)) Deceleration.Value = 13f;
            if (Mathf.Approximately(TurnSpeed.Value, 20f)) TurnSpeed.Value = 12f;
            SolidCharacters = Config.Bind("Movement", "SolidCharacters", true,
                "Other characters don't get shoved around when you walk into them; you bump into them instead. Only affects you vs. them.");
            GtsSpeedMultiplier = Config.Bind("Movement (Giantess)", "SpeedMultiplier", 2f,
                new ConfigDescription("Walk/run/animation speed of every giantess (player and AI). 1 = game speed. The game also slows big giantesses down " +
                    "(Settings > 'Giantess Speed Affected by Scale': a 100 m giantess moves at 60%, 1 km at 40%); this multiplies on top.", new AcceptableValueRange<float>(0.25f, 6f)));

            Graphics = Config.Bind("Graphics", "GraphicsApi", GraphicsChoice.Default,
                "Which graphics API the game runs on. Default = the game's own (DirectX 12). Vulkan/DirectX11 can help with driver timeouts " +
                "or speed on some cards. Takes effect by restarting the game once, automatically.");
            UiEnabled = Config.Bind("UI", "ModernMenus", true,
                "One font, compact settings windows, a shorter pause menu with Settings tabs and a Change Stage button. Off = the game's own menus.");
            UiBlur = Config.Bind("UI", "BlurPauseBackground", true, "Blur the game behind the pause menu.");
            Height = Config.Bind("Height", "Mode", HeightMode.Body,
                "How a character's real height is measured when it spawns. Body = top of the head skin, ignores hair/ears/hats " +
                "(matches the height you type in). AllMesh = top of anything (hair, hats). Vanilla = the game's bounding box (wrong for most models). " +
                "Only affects characters spawned after the change.");
            HeightLog = Config.Bind("Height", "LogMeasurements", true,
                "Write each character's measured heights to BepInEx/LogOutput.log.");

            KoikatsuEnabled = Config.Bind("Koikatsu Maps", "Enabled", true,
                "Add the maps of your Koikatsu / Koikatsu Sunshine install to the stage select (loaded straight from the game folder, nothing is copied).");
            KoikatsuDir = Config.Bind("Koikatsu Maps", "KoikatsuFolder", "",
                "Koikatsu install folder (the one that contains abdata). Empty = look it up in the registry.");
            KoikatsuSunshineDir = Config.Bind("Koikatsu Maps", "KoikatsuSunshineFolder", "",
                "Koikatsu Sunshine install folder (the one that contains abdata). Empty = look it up in the registry.");
            KoikatsuSkip = Config.Bind("Koikatsu Maps", "SkipMaps", "",
                "Comma separated map file names (without .unity3d) to leave out of the stage list.");
            KoikatsuBakedLighting = Config.Bind("Koikatsu Maps", "KeepBakedLighting", false,
                "Keep Koikatsu's baked lightmaps. Off = light the map in real time with Sizebox's sun and sky (recommended: KK's day/night " +
                "lightmap switching does not exist in Sizebox, so baked maps often load black or at the wrong time of day).");
            KoikatsuGiantessThroughProps = Config.Bind("Koikatsu Maps", "GiantessWalksThroughProps", true,
                "Giantesses only collide with floors on Koikatsu maps and walk through trees, walls and furniture. Off = they get wedged between props and can't move.");
            KoikatsuAddColliders = Config.Bind("Koikatsu Maps", "AddMissingColliders", true,
                "Give solid map meshes without collision a collider (Koikatsu Sunshine floors have none, so characters fall through; also lets you stand on desks, beds, benches).");
            KoikatsuModMaps = Config.Bind("Koikatsu Maps", "IncludeModMaps", true,
                "Also list maps from sideloader zipmods in each game's mods folder (shown as 'KK mod - ...' / 'KKS mod - ...'). " +
                "Only the newest version of each mod is listed. The first start after installing mods reads every zipmod once (results are cached).");
            KoikatsuDebug = Config.Bind("Koikatsu Maps", "DebugLog", false, "Log every material of a Koikatsu map as it loads.");
            if (string.IsNullOrWhiteSpace(KoikatsuDir.Value))
                KoikatsuDir.Value = FindInstall(@"Software\Illusion\Koikatu\koikatu");
            if (string.IsNullOrWhiteSpace(KoikatsuSunshineDir.Value))
                KoikatsuSunshineDir.Value = FindInstall(@"Software\Illusion\KoikatsuSunshine\KoikatsuSunshine");

            Test = Config.Bind("Dev", "SelfTest", DevTest.Off,
                "Developer self test. Heights = load a stage, spawn every model at the same height, screenshot. Scene = load TestScene and screenshot. Leave Off.");
            TestScene = Config.Bind("Dev", "TestScene", "",
                "Stage name for the self test (empty = first stage in the list).");
            TestHeightMeters = Config.Bind("Dev", "TestHeightMeters", 1.8f, "Height every model is set to in the Heights self test.");
            TestModels = Config.Bind("Dev", "TestModels", "", "Comma separated parts of model names to include in the Heights self test (empty = all).");

            FixBepInExConfig();
            GraphicsApi.Check();
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            if (Test.Value != DevTest.Off)
            {
                // The game tears down the BepInEx manager object during startup, so the test
                // lives on its own persistent object.
                var host = new GameObject("SizeboxTweaks SelfTest") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(host);
                host.AddComponent<SelfTest>();
            }
            var keep = new GameObject("BuckyTweaks") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(keep);
            global::SizeboxTweaks.SolidCharacters.Install(keep);

            Log.LogInfo($"Koikatsu: '{KoikatsuDir.Value}', Sunshine: '{KoikatsuSunshineDir.Value}'");
            Log.LogInfo($"{Name} {Version} loaded (height mode {Height.Value}, movement {(MovementEnabled.Value ? "tuned" : "vanilla")})");
        }

        private float _heartbeat;
        private void Update()
        {
            if (Test.Value == DevTest.Off || Time.realtimeSinceStartup < _heartbeat)
                return;
            _heartbeat = Time.realtimeSinceStartup + 10f;
            Log.LogInfo($"[SelfTest] manager object alive at {Time.realtimeSinceStartup:0}s");
        }

        // Sizebox destroys BepInEx's manager object at startup, which kills ConfigurationManager
        // (the F1 menu) and anything else living on it. BepInEx keeps it alive when
        // HideManagerGameObject is on; switch it on once so nobody has to edit the file by hand.
        private static void FixBepInExConfig()
        {
            try
            {
                string path = System.IO.Path.Combine(Paths.ConfigPath, "BepInEx.cfg");
                if (!System.IO.File.Exists(path))
                    return;
                string text = System.IO.File.ReadAllText(path);
                if (text.Contains("HideManagerGameObject = false"))
                {
                    System.IO.File.WriteAllText(path, text.Replace("HideManagerGameObject = false", "HideManagerGameObject = true"));
                    Log.LogWarning("Turned on HideManagerGameObject in BepInEx.cfg. Restart the game once so the F1 settings menu works.");
                }
            }
            catch (System.Exception e)
            {
                Log.LogWarning("Could not update BepInEx.cfg: " + e.Message);
            }
        }

        private static string FindInstall(string key)
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key))
                {
                    string dir = k?.GetValue("INSTALLDIR") as string;
                    if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(System.IO.Path.Combine(dir, "abdata")))
                        return dir.TrimEnd('\\', '/');
                }
            }
            catch (System.Exception e)
            {
                Log.LogWarning($"Registry lookup {key}: {e.Message}");
            }
            return "";
        }
    }
}
