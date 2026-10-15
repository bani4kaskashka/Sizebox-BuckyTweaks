using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SizeboxTweaks
{
    // A giantess walks by pushing a big physics capsule (GTSMovement) around, and that capsule
    // collides with Sizebox's "Map" layer. On Sizebox's own stages that layer is just the ground,
    // but on a Koikatsu map everything is on it: trees, hedges, lamps, benches, walls, desks. The
    // capsule gets wedged between them, physics pushes it out sideways, and the giantess gets
    // dragged to one side and can't walk anywhere (measured: 0.2 m in 3 s of walking, any
    // direction). Micros still collide with everything; giantesses only with floors.
    internal static class GiantessMapCollision
    {
        private static readonly List<Collider> Props = new List<Collider>();

        private static readonly AccessTools.FieldRef<GTSMovement, CapsuleCollider> Capsule =
            AccessTools.FieldRefAccess<GTSMovement, CapsuleCollider>("capsuleCollider");

        // Flat things are floors (ground, roads, room floors, sand); anything with real height
        // compared to its footprint is a prop the giantess should walk through.
        internal static bool IsFloor(Collider c)
        {
            Vector3 s = c.bounds.size;
            float footprint = Mathf.Max(s.x, s.z);
            return s.y <= 0.6f || s.y <= 0.1f * footprint;
        }

        internal static void OnMapLoaded(Scene scene)
        {
            Props.Clear();
            GameObject[] roots = scene.GetRootGameObjects();
            if (!roots.Any(r => r.name == "KK Spawn"))
                return; // not a Koikatsu map

            int floors = 0;
            foreach (GameObject root in roots)
                foreach (Collider c in root.GetComponentsInChildren<Collider>(true))
                {
                    if (c.isTrigger)
                        continue;
                    if (IsFloor(c))
                        floors++;
                    else
                        Props.Add(c);
                }
            Plugin.Log.LogInfo($"Koikatsu map: giantesses walk on {floors} floor colliders and through {Props.Count} props");

            foreach (GTSMovement m in Object.FindObjectsOfType<GTSMovement>())
                Apply(m);
        }

        internal static void Apply(GTSMovement m)
        {
            if (Props.Count == 0 || m == null || !Plugin.KoikatsuGiantessThroughProps.Value)
                return;
            CapsuleCollider cap = Capsule(m);
            if (cap == null)
                return;
            foreach (Collider p in Props)
                if (p != null)
                    Physics.IgnoreCollision(cap, p, true);
        }
    }

    [HarmonyPatch(typeof(GTSMovement), "Awake")]
    internal static class GtsMovementAwakePatch
    {
        private static void Postfix(GTSMovement __instance) => GiantessMapCollision.Apply(__instance);
    }

    [HarmonyPatch(typeof(SceneLoader), "OnSceneLoaded")]
    internal static class GiantessMapCollisionScenePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Scene scene) => GiantessMapCollision.OnMapLoaded(scene);
    }
}
