using HarmonyLib;
using UnityEngine;

namespace SizeboxTweaks
{
    // New micros and objects from the catalog spawn at the size of the last selected micro.
    // Shrink one with the size gun, click it, and every spawn after that is millimetres tall.
    // Spawn at the map's normal size instead.
    [HarmonyPatch(typeof(InterfaceControl), nameof(InterfaceControl.lastMicroScale), MethodType.Getter)]
    internal static class SpawnSizePatch
    {
        private static void Postfix(ref float __result)
        {
            if (Plugin.SpawnNormalSize.Value)
                __result = MapSettingInternal.startingSize;
        }
    }

    // Restart reloads the active scene by its Unity scene name. Custom and Koikatsu stages are
    // listed under their own names, so the lookup failed and nothing happened after "Yes".
    // Reload the stage that was actually loaded.
    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.LoadScene), typeof(SceneLoader.SceneInfo))]
    internal static class LastStagePatch
    {
        // The game forgets which stage entry it loaded once loading is done
        internal static SceneLoader.SceneInfo Last;

        private static void Prefix(SceneLoader.SceneInfo scene) => Last = scene;
    }

    [HarmonyPatch(typeof(SavedScenesManager), nameof(SavedScenesManager.RestartScene))]
    internal static class RestartPatch
    {
        private static bool Prefix()
        {
            var loader = Object.FindObjectOfType<SceneLoader>();
            var last = LastStagePatch.Last;
            if (loader == null || last == null || last.AssetData == null)
                return true;
            if (GameController.Instance != null)
                GameController.Instance.SetPausedState(false);
            Plugin.Log.LogInfo("Restarting stage " + last.Scene);
            if (loader.LoadScene(last) == null)
                UiMessageBox.Create($"Unable to reload '{last.Scene}'.", "Restart").Popup();
            return false;
        }
    }

    // Mouse wheel: zoom (and Shift + wheel: camera height) in bigger steps.
    [HarmonyPatch(typeof(BaseCameraController), "OnZoomPerformed")]
    internal static class CameraScrollPatch
    {
        private static readonly AccessTools.FieldRef<BaseCameraController, float> ZoomInput =
            AccessTools.FieldRefAccess<BaseCameraController, float>("zoomInput");

        private static void Prefix(BaseCameraController __instance, out (float zoom, float height) __state)
        {
            __state = (ZoomInput(__instance), __instance.heightModifier);
        }

        private static void Postfix(BaseCameraController __instance, (float zoom, float height) __state)
        {
            float dz = ZoomInput(__instance) - __state.zoom;
            if (dz != 0f)
                ZoomInput(__instance) = __state.zoom + dz * Plugin.ZoomSpeed.Value;
            float dh = __instance.heightModifier - __state.height;
            if (dh != 0f)
                __instance.heightModifier = Mathf.Clamp(__state.height + dh * Plugin.HeightScrollSpeed.Value, 0.1f, 1.9f);
        }
    }

    // Edit mode's free camera (SimCam) zooms with its own speed, separate from the player cameras
    [HarmonyPatch(typeof(SimCam), "Zoom")]
    internal static class SimCamZoomPatch
    {
        private static readonly AccessTools.FieldRef<SimCam, float> Speed = AccessTools.FieldRefAccess<SimCam, float>("_zoomSpeed");

        private static void Prefix(SimCam __instance, out float __state)
        {
            __state = Speed(__instance);
            Speed(__instance) = __state * Plugin.ZoomSpeed.Value;
        }

        private static void Postfix(SimCam __instance, float __state) => Speed(__instance) = __state;
    }

    // Some maps have holes or missing floors. If the player keeps falling with nothing at all
    // below, put them back where they last stood on solid ground.
    internal class VoidRescue : MonoBehaviour
    {
        private EntityBase _who;
        private Vector3 _safe;
        private Quaternion _safeRot;
        private bool _hasSafe;
        private float _fallingInVoid;
        private float _nextSafeCheck;
        private float _standing;
        private int _rescues;
        private float _rescueWindow;

        private void FixedUpdate()
        {
            if (!Plugin.VoidRescue.Value || GameController.LocalClient?.Player == null)
                return;
            EntityBase e = GameController.LocalClient.Player.Entity;
            if (e == null || e.Rigidbody == null || e.Rigidbody.isKinematic || (e is Humanoid hm && hm.IsDead) || (e is Micro mc && mc.isCrushed))
            {
                _fallingInVoid = 0f;
                return;
            }
            if (e != _who)
            {
                _who = e;
                _hasSafe = false;
                _fallingInVoid = 0f;
            }
            float h = Mathf.Max(e.MeshHeight, 0.01f);
            Vector3 p = e.transform.position;
            int mask = Layers.walkableMask | LayerMask.GetMask("Map");

            if (Time.time >= _nextSafeCheck)
            {
                _nextSafeCheck = Time.time + 0.25f;
                // standing for a moment on something solid (ground right under the feet, not
                // moving up or down much); triggers don't count
                bool onGround = Mathf.Abs(e.Rigidbody.velocity.y) < h * 0.3f
                                && Physics.Raycast(p + Vector3.up * (h * 0.3f), Vector3.down, h * 0.5f, mask, QueryTriggerInteraction.Ignore);
                _standing = onGround ? _standing + 0.25f : 0f;
                if (_standing >= 1f)
                {
                    _safe = p;
                    _safeRot = e.transform.rotation;
                    _hasSafe = true;
                }
            }

            bool falling = e.Rigidbody.velocity.y < -h * 0.5f;
            bool nothingBelow = falling && !Physics.Raycast(p, Vector3.down, h * 2000f, mask, QueryTriggerInteraction.Ignore);
            _fallingInVoid = nothingBelow ? _fallingInVoid + Time.fixedDeltaTime : 0f;
            if (_fallingInVoid > 1.2f && _hasSafe)
            {
                // A spot that keeps dropping you again isn't safe; give up instead of looping
                if (Time.time > _rescueWindow)
                {
                    _rescues = 0;
                    _rescueWindow = Time.time + 10f;
                }
                if (++_rescues > 3)
                {
                    if (_rescues == 4)
                        Plugin.Log.LogWarning("Void rescue: the safe spot keeps dropping through, stopping");
                    _hasSafe = false;
                    return;
                }
                string below = Physics.Raycast(_safe + Vector3.up * h, Vector3.down, out RaycastHit floor, h * 3f, mask, QueryTriggerInteraction.Ignore)
                    ? $"{floor.collider.name} ({LayerMask.LayerToName(floor.collider.gameObject.layer)}, {floor.collider.GetType().Name})" : "nothing";
                Plugin.Log.LogInfo($"Fell into the void at {p}, back to {_safe} (floor there: {below})");
                Vector3 to = _safe + Vector3.up * (h * 0.1f);
                e.Rigidbody.velocity = Vector3.zero;
                e.Rigidbody.position = to;
                e.transform.SetPositionAndRotation(to, _safeRot);
                _fallingInVoid = 0f;
            }
        }
    }
}
