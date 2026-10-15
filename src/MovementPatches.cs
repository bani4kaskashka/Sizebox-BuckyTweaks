using HarmonyLib;
using UnityEngine;

namespace SizeboxTweaks
{
    // The micro player controller moves with
    //   velocity = Lerp(previousVelocity, target, movementSmoothing * fixedDeltaTime)
    // and turns the body with Slerp(rotation, target, turnSmoothing * fixedDeltaTime).
    // With the shipped values (4 and 4) the body keeps drifting for a quarter second after
    // you let go and lags behind the move direction while turning, so the character visibly
    // slides sideways. We feed the controller's own settings different numbers right before
    // it runs, using a separate value for speeding up and for stopping.
    [HarmonyPatch(typeof(NewPlayerMicroController), "MovementManagement")]
    internal static class MicroMovementPatch
    {
        private static bool _captured, _applied;
        private static float _smoothing, _turn, _walk, _run, _sprint, _damp, _jump, _cooldown;

        private static readonly AccessTools.FieldRef<NewPlayerMicroController, AnimatedMicroNPC> Micro =
            AccessTools.FieldRefAccess<NewPlayerMicroController, AnimatedMicroNPC>("_micro");

        private static void Prefix(NewPlayerMicroController __instance)
        {
            PlayerMicroSettings s = __instance.Settings;
            if (s == null)
                return;

            if (!_captured)
            {
                // Remember the shipped values once so turning the option off restores them.
                _captured = true;
                _smoothing = s.movementSmoothing;
                _turn = s.turnSmoothing;
                _walk = s.walkSpeed;
                _run = s.runSpeed;
                _sprint = s.sprintSpeed;
                _damp = s.speedDampTime;
                _jump = s.jumpHeight;
                _cooldown = s.jumpCooldown;
                Plugin.Log.LogInfo($"Shipped jump speed {_jump}, ground check paused {_cooldown} s after take-off (peaks at {_jump * _jump / (4f * Gravity.GravityDensity):0.00} body heights)");
                Plugin.Log.LogInfo($"Shipped micro movement: smoothing {_smoothing}, turn {_turn}, walk/run/sprint {_walk}/{_run}/{_sprint}, anim damp {_damp}, baseSpeed {s.baseSpeed}");
            }

            if (!Plugin.MovementEnabled.Value)
            {
                // Put the shipped values back once, then leave the settings alone so Lua
                // scripts (Game.GetLocalPlayerSettings) can change them.
                if (_applied)
                {
                    _applied = false;
                    s.movementSmoothing = _smoothing;
                    s.turnSmoothing = _turn;
                    s.walkSpeed = _walk;
                    s.runSpeed = _run;
                    s.sprintSpeed = _sprint;
                    s.speedDampTime = _damp;
                    s.jumpHeight = _jump;
                    s.jumpCooldown = _cooldown;
                }
                return;
            }
            _applied = true;

            s.movementSmoothing = __instance.IsMoving ? Plugin.Acceleration.Value : Plugin.Deceleration.Value;
            s.turnSmoothing = Plugin.TurnSpeed.Value;
            s.walkSpeed = Plugin.WalkSpeed.Value;
            s.runSpeed = Plugin.RunSpeed.Value;
            s.sprintSpeed = Plugin.SprintSpeed.Value;
            s.speedDampTime = Plugin.AnimationDamp.Value;
            // Jump: take-off speed is jumpHeight * scale and gravity is GravityDensity * scale, and a
            // micro is 2 units tall per unit of scale, so the peak in body heights is
            // jumpHeight^2 / (4 * GravityDensity). Solve for the wanted peak.
            s.jumpHeight = Mathf.Sqrt(4f * Gravity.GravityDensity * Plugin.JumpHeight.Value);
            // After a jump the controller ignores the ground for jumpCooldown seconds (so it doesn't
            // snap back down at take-off). The game's 1 s matched its huge jump; a normal jump lands
            // in about 0.7 s, and for the rest of that second you stand on the ground "in the air":
            // stuck in the jump animation with only air control. Scale it to the actual flight time.
            float flight = 2f * s.jumpHeight / Gravity.GravityDensity;
            s.jumpCooldown = Mathf.Clamp(0.35f * flight, 0.15f, _cooldown);
        }

        // Step up onto stairs and kerbs: when walking into a low ledge whose top is free and no
        // higher than StepHeight body heights, lift the body onto it.
        private static void Postfix(NewPlayerMicroController __instance, bool isGrounded)
        {
            if (!Plugin.MovementEnabled.Value || !isGrounded || Plugin.StepHeight.Value <= 0f)
                return;
            AnimatedMicroNPC micro = Micro(__instance);
            if (micro == null || micro.Rigidbody == null || micro.Capsule == null)
                return;
            StepUp.TryStep(micro.Rigidbody, micro.Capsule, micro.MeshHeight, Plugin.StepHeight.Value);
        }
    }

    internal static class StepUp
    {
        internal static int Steps;

        internal static bool TryStep(Rigidbody rb, CapsuleCollider capsule, float bodyHeight, float stepFraction)
        {
            Vector3 flat = rb.velocity;
            flat.y = 0f;
            if (flat.magnitude < 0.05f * bodyHeight)
                return false;
            Vector3 dir = flat.normalized;
            Vector3 up = Vector3.up;
            Vector3 feet = rb.position;
            float radius = capsule.radius * Mathf.Max(capsule.transform.lossyScale.x, capsule.transform.lossyScale.z);
            float maxStep = stepFraction * bodyHeight;
            int mask = Layers.walkableMask;
            float reach = radius + 0.12f * bodyHeight;

            // Something in the way at ankle height that is steeper than a walkable slope...
            if (!Physics.Raycast(feet + up * (0.04f * bodyHeight), dir, out RaycastHit low, reach, mask, QueryTriggerInteraction.Ignore))
                return false;
            if (low.normal.y > 0.7f)
                return false;
            // ...but nothing just above the highest step we take (a wall, not a step)...
            if (Physics.Raycast(feet + up * (maxStep + 0.03f * bodyHeight), dir, reach + 0.05f * bodyHeight, mask, QueryTriggerInteraction.Ignore))
                return false;
            // ...and a flat top to stand on just past the edge.
            Vector3 probe = feet + dir * (low.distance + 0.06f * bodyHeight) + up * (maxStep + 0.03f * bodyHeight);
            if (!Physics.Raycast(probe, Vector3.down, out RaycastHit top, maxStep + 0.03f * bodyHeight, mask, QueryTriggerInteraction.Ignore))
                return false;
            float rise = top.point.y - feet.y;
            if (top.normal.y < 0.7f || rise < 0.01f * bodyHeight || rise > maxStep)
                return false;

            rb.position = feet + up * (rise + 0.005f * bodyHeight);
            Steps++;
            return true;
        }
    }

    // Giantess speed is animation speed: walking moves her by stride length times this value,
    // and the game shrinks it with size (1 - log10(height)/5, the "Giantess Speed Affected by
    // Scale" option), so a 100 m giantess runs at 60% and a 1 km one at 40%. Scaling the value
    // here speeds up the animation and the movement together, for player and AI giantesses.
    [HarmonyPatch(typeof(AnimationManager), nameof(AnimationManager.GetCurrentSpeed))]
    internal static class GiantessSpeedPatch
    {
        private static readonly AccessTools.FieldRef<AnimationManager, Humanoid> Agent =
            AccessTools.FieldRefAccess<AnimationManager, Humanoid>("_agent");

        private static void Postfix(AnimationManager __instance, ref float __result)
        {
            if (Agent(__instance) is Giantess)
                __result *= Plugin.GtsSpeedMultiplier.Value;
        }
    }

    // Player-controlled giantess: input is smoothed by speedChange and the body turns by turnSpeed.
    [HarmonyPatch(typeof(PlayerGiantessController), nameof(PlayerGiantessController.DoFixedUpdate))]
    internal static class GiantessMovementPatch
    {
        private static bool _captured;
        private static float _speedChange, _turnSpeed;

        private static void Prefix(PlayerGiantessController __instance)
        {
            if (!_captured)
            {
                _captured = true;
                _speedChange = __instance.speedChange;
                _turnSpeed = __instance.turnSpeed;
            }

            bool tuned = Plugin.MovementEnabled.Value;
            __instance.speedChange = tuned ? Plugin.GtsSpeedChange.Value : _speedChange;
            __instance.turnSpeed = tuned ? Plugin.GtsTurnSpeed.Value : _turnSpeed;
        }
    }
}
