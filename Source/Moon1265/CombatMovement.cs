using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Moon1265
{
    /// <summary>
    /// Shooter-style ground movement for an EVA Kerbal. While active it removes KSP's own
    /// walking, turning and upright-correction steps from the Kerbal's idle states and drives
    /// the rigidbody directly, so movement is instant, strafes, sprints and jumps.
    /// KSP stays in charge of everything else (ragdoll, recovering, swimming, ladders).
    /// </summary>
    internal class CombatMovement
    {
        // KerbalEVA steps that would fight us. Anything else in those states keeps running.
        private static readonly HashSet<string> StrippedSteps = new HashSet<string>
        {
            "UpdateMovement", "UpdateHeading", "correctGroundedRotation",
            "UpdatePackLinear", "UpdatePackAngular", "UpdateOrientationPID", "CheckLadderTriggers",
        };

        // KSP pins a Kerbal that stands still to the ground; we unpin it when we want to move.
        private static readonly MethodInfo RemoveAnchor =
            typeof(KerbalEVA).GetMethod("RemoveRBAnchor", BindingFlags.Instance | BindingFlags.NonPublic);

        // What counts as ground: 0 parts, 15 local scenery, 19 physical objects, 28 terrain colliders.
        private const int GroundMask = (1 << 0) | (1 << 15) | (1 << 19) | (1 << 28);
        private const float JumpGrace = 0.3f;

        private readonly KerbalEVA eva;
        private readonly List<KeyValuePair<KFSMState, KFSMCallback>> originalSteps = new List<KeyValuePair<KFSMState, KFSMCallback>>();
        private readonly float originalStumbleThreshold;

        private float jumpGraceUntil;

        public CombatMovement(KerbalEVA eva)
        {
            this.eva = eva;
            Strip(eva.st_idle_gr);
            Strip(eva.st_idle_b_gr);
            Strip(eva.st_idle_fl);

            originalStumbleThreshold = eva.stumbleThreshold;
            eva.stumbleThreshold = Mathf.Max(originalStumbleThreshold, Settings.StumbleThreshold);
        }

        /// <summary>0 = standing, 1 = fully crouched (smoothed).</summary>
        public float Crouch { get; private set; }
        public bool Grounded { get; private set; }
        public bool Sprinting { get; private set; }
        public float HorizontalSpeed { get; private set; }

        /// <summary>Puts KSP's own movement back exactly as it was.</summary>
        public void Release()
        {
            foreach (KeyValuePair<KFSMState, KFSMCallback> saved in originalSteps)
                saved.Key.OnFixedUpdate = saved.Value;
            originalSteps.Clear();
            if (eva != null) eva.stumbleThreshold = originalStumbleThreshold;
        }

        private void Strip(KFSMState state)
        {
            if (state == null || state.OnFixedUpdate == null) return;
            originalSteps.Add(new KeyValuePair<KFSMState, KFSMCallback>(state, state.OnFixedUpdate));

            KFSMCallback kept = null;
            foreach (System.Delegate step in state.OnFixedUpdate.GetInvocationList())
            {
                if (!StrippedSteps.Contains(step.Method.Name))
                    kept = (KFSMCallback)System.Delegate.Combine(kept, step);
            }
            state.OnFixedUpdate = kept ?? Nothing;
        }

        private static void Nothing() { }

        /// <param name="move">x = strafe (-1 left .. 1 right), y = forward (-1 .. 1).</param>
        public void Step(Vector3 lookForward, Vector3 up, Vector2 move, bool sprint, bool crouch, bool aiming, bool jump, float dt)
        {
            Crouch = Mathf.MoveTowards(Crouch, crouch ? 1f : 0f, dt * 6f);

            Rigidbody rb = eva.part.Rigidbody;
            if (rb == null || eva.vessel == null || eva.vessel.packed) return;

            KFSMState state = eva.fsm.CurrentState;
            bool controllable = state == eva.st_idle_gr || state == eva.st_idle_b_gr || state == eva.st_idle_fl || state == eva.st_land;
            if (!controllable)
            {
                // Ragdoll, recovering, swimming, ladder... let KSP handle it.
                Grounded = false;
                Sprinting = false;
                return;
            }

            Grounded = state != eva.st_idle_fl && Time.fixedTime >= jumpGraceUntil;

            Vector3 forward = Vector3.ProjectOnPlane(lookForward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward);
            Vector3 wish = forward * move.y + right * move.x;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            Sprinting = sprint && !crouch && !aiming && Grounded && move.y > 0.1f;
            float speed = crouch ? Settings.CrouchSpeed : Sprinting ? Settings.SprintSpeed : Settings.WalkSpeed;
            if (aiming) speed *= Settings.AimSpeedMultiplier;

            jump &= Grounded;
            if ((wish != Vector3.zero || jump) && RemoveAnchor != null) RemoveAnchor.Invoke(eva, null);

            Vector3 velocity = rb.velocity;
            if (Grounded)
            {
                Vector3 normal = GroundNormal(up);
                Vector3 target = wish == Vector3.zero
                    ? Vector3.zero
                    : Vector3.ProjectOnPlane(wish, normal).normalized * (wish.magnitude * speed);
                Vector3 along = Vector3.MoveTowards(Vector3.ProjectOnPlane(velocity, normal), target, Settings.GroundAcceleration * dt);
                // Keep motion into the ground (so gravity holds us down) but never bounce off it.
                velocity = along + normal * Mathf.Min(Vector3.Dot(velocity, normal), 0f);

                if (jump)
                {
                    velocity += up * Settings.JumpSpeed;
                    jumpGraceUntil = Time.fixedTime + JumpGrace;
                }
            }
            else
            {
                Vector3 vertical = Vector3.Project(velocity, up);
                Vector3 horizontal = Vector3.MoveTowards(velocity - vertical, wish * speed, Settings.AirAcceleration * dt);
                velocity = horizontal + vertical;
            }
            rb.velocity = velocity;
            HorizontalSpeed = Vector3.ProjectOnPlane(velocity, up).magnitude;

            // Top gravity up to CombatGravity so low-gravity moons still play like ground combat.
            float extraGravity = Settings.CombatGravity - LocalGravity();
            if (Settings.CombatGravity > 0f && extraGravity > 0f) rb.AddForce(-up * extraGravity, ForceMode.Acceleration);

            // Stand upright, facing where we look. (Landing has its own short recovery animation.)
            if (state != eva.st_land)
            {
                rb.MoveRotation(Quaternion.LookRotation(forward, up));
                rb.angularVelocity = Vector3.zero;
            }
        }

        private Vector3 GroundNormal(Vector3 up)
        {
            Vector3 origin = eva.transform.position + up * 0.2f;
            RaycastHit[] hits = Physics.RaycastAll(origin, -up, 2f, GroundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 normal = up;
            foreach (RaycastHit hit in hits)
            {
                if (hit.distance >= best) continue;
                Part part = hit.collider.GetComponentInParent<Part>();
                if (part != null && part.vessel == eva.vessel) continue;
                best = hit.distance;
                normal = hit.normal;
            }
            // Treat anything steeper than ~60 degrees as a wall, not a floor.
            return Vector3.Dot(normal, up) > 0.5f ? normal : up;
        }

        private float LocalGravity()
        {
            CelestialBody body = eva.vessel.mainBody;
            double r = (eva.vessel.CoMD - body.position).magnitude;
            return r > 0 ? (float)(body.gravParameter / (r * r)) : 0f;
        }
    }
}
