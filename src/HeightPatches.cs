using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SizeboxTweaks
{
    // Sizebox decides how tall a character "is" from the combined renderer bounding box
    // (EntityBase.GetCombinedRendererHeight -> bounds.max.y) and then rescales the model so that
    // box is exactly 2 units (micro) or 2000 units (giantess) tall. Every height you type in or
    // see in the UI is that box height. The box is not the body: skinned mesh bounds are padded,
    // and hair, ears, ribbons, hats and stray accessory meshes all push it up by different amounts
    // per model. So two characters set to 180 cm and 200 cm can stand head to head.
    //
    // Fix: right after Humanoid.InitializeModel measures the box (and before Micro/Giantess
    // normalise the model with it), measure the real mesh by skinning it in its rest pose and
    // replace meshHeight with that.
    [HarmonyPatch(typeof(Humanoid), "InitializeModel")]
    internal static class HumanoidHeightPatch
    {
        private static void Postfix(Humanoid __instance)
        {
            if (Plugin.Height.Value == HeightMode.Vanilla && !Plugin.HeightLog.Value)
                return;

            HeightMeasure.Result r;
            try
            {
                r = HeightMeasure.Measure(__instance);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Height measure failed for {Describe(__instance)}: {e}");
                return;
            }

            float vanilla = __instance.meshHeight;
            float chosen = vanilla;
            string used = "vanilla";
            if (Plugin.Height.Value == HeightMode.Body && r.Body > 0f)
            {
                chosen = r.Body;
                used = "body";
            }
            else if (Plugin.Height.Value != HeightMode.Vanilla && r.AllMesh > 0f)
            {
                chosen = r.AllMesh;
                used = "allmesh";
            }

            // Never trust a result that is wildly off from the box; that means the measure broke.
            if (chosen < vanilla * 0.4f || chosen > vanilla * 1.05f)
            {
                Plugin.Log.LogWarning($"{Describe(__instance)}: measured {chosen:0.###} vs box {vanilla:0.###}, keeping box height");
                chosen = vanilla;
                used = "vanilla (fallback)";
            }

            if (Plugin.HeightLog.Value)
            {
                Plugin.Log.LogInfo(
                    $"Height {Describe(__instance)}: box {vanilla:0.####}  allMesh {r.AllMesh:0.####}  body {r.Body:0.####}  " +
                    $"headBone {r.HeadBoneY:0.####}  eyes {r.EyeY:0.####}  -> using {used} " +
                    $"(box is {(vanilla / chosen - 1f) * 100f:+0.0;-0.0}% vs used; skinned {r.SkinnedRenderers}, static {r.StaticRenderers}, body verts {r.BodyVerts})");
            }

            __instance.meshHeight = chosen;
        }

        internal static string Describe(EntityBase e)
        {
            string asset = e.asset != null ? e.asset.AssetName : "?";
            string kind = e is Giantess ? "GTS" : e is Micro ? "Micro" : e.GetType().Name;
            return $"[{kind}] {asset}";
        }
    }

    internal static class HeightMeasure
    {
        public struct Result
        {
            public float AllMesh;
            public float Body;
            public float HeadBoneY;
            public float EyeY;
            public int SkinnedRenderers;
            public int StaticRenderers;
            public int BodyVerts;
        }

        private static Mesh _bakeTarget;

        public static Result Measure(Humanoid h)
        {
            var result = new Result { AllMesh = -1f, Body = -1f, HeadBoneY = -1f, EyeY = -1f };
            GameObject model = h.model;
            if (model == null)
                return result;

            // Same trick the game uses: measure with the root at the origin, unrotated, scale 1,
            // so the numbers are in the same space as meshHeight.
            Transform root = h.transform;
            Transform parent = root.parent;
            Vector3 localScale = root.localScale;
            Vector3 position = root.position;
            Quaternion rotation = root.rotation;
            root.SetParent(null);
            root.localScale = Vector3.one;
            root.rotation = Quaternion.identity;
            root.position = Vector3.zero;
            try
            {
                MeasureInPlace(h, model, ref result);
            }
            finally
            {
                root.parent = parent;
                root.position = position;
                root.rotation = rotation;
                root.localScale = localScale;
            }
            return result;
        }

        private static void MeasureInPlace(Humanoid h, GameObject model, ref Result result)
        {
            Animator animator = h.Animator;
            var humanBones = new HashSet<Transform>();
            if (animator != null && animator.isHuman)
            {
                for (int b = 0; b < (int)HumanBodyBones.LastBone; b++)
                {
                    Transform t = animator.GetBoneTransform((HumanBodyBones)b);
                    if (t != null)
                        humanBones.Add(t);
                }
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                    result.HeadBoneY = head.position.y;
                Transform le = animator.GetBoneTransform(HumanBodyBones.LeftEye);
                Transform re = animator.GetBoneTransform(HumanBodyBones.RightEye);
                if (le != null && re != null)
                    result.EyeY = (le.position.y + re.position.y) * 0.5f;
            }

            if (_bakeTarget == null)
                _bakeTarget = new Mesh { name = "SizeboxTweaks height bake" };

            float allTop = float.NegativeInfinity;
            float bodyTop = float.NegativeInfinity;

            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy)
                    continue;

                if (r is SkinnedMeshRenderer smr)
                {
                    Mesh shared = smr.sharedMesh;
                    if (shared == null)
                        continue;
                    result.SkinnedRenderers++;

                    _bakeTarget.Clear();
                    // Legacy bake: vertices come out relative to the renderer's position and
                    // rotation, without its scale.
                    smr.BakeMesh(_bakeTarget);
                    Vector3[] verts = _bakeTarget.vertices;
                    Matrix4x4 toWorld = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);

                    // Which vertices belong to the humanoid skeleton (skin/face), not hair or props
                    bool[] isBody = null;
                    if (humanBones.Count > 0 && shared.isReadable)
                    {
                        BoneWeight[] weights = shared.boneWeights;
                        Transform[] bones = smr.bones;
                        if (weights != null && weights.Length == verts.Length && bones != null && bones.Length > 0)
                        {
                            var boneIsBody = new bool[bones.Length];
                            for (int i = 0; i < bones.Length; i++)
                                boneIsBody[i] = bones[i] != null && humanBones.Contains(bones[i]);
                            isBody = new bool[verts.Length];
                            for (int i = 0; i < verts.Length; i++)
                            {
                                int dom = Dominant(weights[i]);
                                isBody[i] = dom >= 0 && dom < boneIsBody.Length && boneIsBody[dom];
                            }
                        }
                    }

                    for (int i = 0; i < verts.Length; i++)
                    {
                        float y = toWorld.MultiplyPoint3x4(verts[i]).y;
                        if (y > allTop)
                            allTop = y;
                        if (isBody != null && isBody[i])
                        {
                            result.BodyVerts++;
                            if (y > bodyTop)
                                bodyTop = y;
                        }
                    }
                }
                else if (r is MeshRenderer)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null)
                        continue;
                    result.StaticRenderers++;
                    // Static meshes on a character are accessories; count them for AllMesh only.
                    float top = mf.sharedMesh.isReadable ? TopOf(mf.sharedMesh.vertices, r.transform.localToWorldMatrix) : r.bounds.max.y;
                    if (top > allTop)
                        allTop = top;
                }
            }

            if (!float.IsNegativeInfinity(allTop))
                result.AllMesh = allTop;
            // Body top must be above the head bone or the weights were not what we expected.
            if (!float.IsNegativeInfinity(bodyTop) && bodyTop > result.HeadBoneY)
                result.Body = bodyTop;
            else if (result.AllMesh > 0f)
                result.Body = result.AllMesh;
        }

        private static int Dominant(BoneWeight w)
        {
            int idx = w.boneIndex0;
            float best = w.weight0;
            if (w.weight1 > best) { best = w.weight1; idx = w.boneIndex1; }
            if (w.weight2 > best) { best = w.weight2; idx = w.boneIndex2; }
            if (w.weight3 > best) { idx = w.boneIndex3; }
            return idx;
        }

        private static float TopOf(Vector3[] verts, Matrix4x4 m)
        {
            float top = float.NegativeInfinity;
            for (int i = 0; i < verts.Length; i++)
            {
                float y = m.MultiplyPoint3x4(verts[i]).y;
                if (y > top)
                    top = y;
            }
            return top;
        }
    }
}
