using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Moon1265
{
    /// <summary>
    /// Shooter-style ground movement for an EVA Kerbal. While active it removes KSP's own
    /// walking, turning and upright-correction steps from the Kerbal's idle and swimming states
    /// and drives the rigidbody directly, so movement is instant, strafes, sprints and jumps.
    /// KSP stays in charge of ragdolling and recovering, but we trigger the recovery ourselves
    /// because KSP only lets a Kerbal get up while its (locked) movement keys are held.
    /// </summary>
    internal class CombatMovement
    {
        // KerbalEVA steps that would fight us. Anything else in those states keeps running.
        private static readonly HashSet<string> StrippedSteps = new HashSet<string>
        {
            "UpdateMovement", "UpdateHeading", "correctGroundedRotation",
            "UpdatePackLinear", "UpdatePackAngular", "UpdateOrientationPID",
        };

        // KSP pins a Kerbal that has stood still for 0.5 s to the ground with a joint. RemoveRBAnchor
        // only queues the joint's destruction for the end of the frame, which would cancel the
        // velocity we set in this physics step, so we also destroy the joint ourselves.
        private static readonly MethodInfo RemoveAnchor =
            typeof(KerbalEVA).GetMethod("RemoveRBAnchor", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo AnchorJoint =
            typeof(KerbalEVA).GetField("anchorJoint", BindingFlags.Instance | BindingFlags.NonPublic);

        // What counts as ground: 0 parts, 15 local scenery, 19 physical objects, 28 terrain colliders.
        private const int GroundMask = (1 << 0) | (1 << 15) | (1 << 19) | (1 << 28);
        private const float ProbeRadius = 0.12f;
        private const float ProbeLift = 0.2f;
        private const float GroundedGap = 0.08f;   // feet this close to a floor count as standing
        private const float CoyoteTime = 0.1f;     // stay "grounded" briefly over small bumps
        private const float JumpGrace = 0.3f;
        private const float CombatGravityHeight = 200f; // only top up gravity this close to the terrain

        private readonly KerbalEVA eva;
        private readonly List<KeyValuePair<KFSMState, KFSMCallback>> originalSteps = new List<KeyValuePair<KFSMState, KFSMCallback>>();
        private readonly List<KeyValuePair<KFSMEvent, KFSMEventCondition>> originalConditions = new List<KeyValuePair<KFSMEvent, KFSMEventCondition>>();
        private readonly float originalStumbleThreshold;

        private float jumpGraceUntil;
        private float lastGroundedTime = -1f;

        public CombatMovement(KerbalEVA eva)
        {
            this.eva = eva;
            originalStumbleThreshold = eva.stumbleThreshold;
            try
            {
                Strip(eva.st_idle_gr);
                Strip(eva.st_idle_b_gr);
                Strip(eva.st_idle_fl);
                Strip(eva.st_swim_idle);
                Strip(eva.st_swim_fwd);

                // Climb, ladder-grab and board prompts would keep popping up but can't be used.
                Disable(eva.On_clamberGrabStart);
                Disable(eva.On_ladderGrabStart);
                Disable(eva.On_boardPart);

                eva.stumbleThreshold = Mathf.Max(originalStumbleThreshold, Settings.StumbleThreshold);
            }
            catch
            {
                Release();
                throw;
            }
        }

        /// <summary>0 = standing, 1 = fully crouched (smoothed).</summary>
        public float Crouch { get; private set; }
        public bool Grounded { get; private set; }
        public bool Sprinting { get; private set; }
        public bool Ragdolled { get; private set; }
        public float HorizontalSpeed { get; private set; }

        /// <summary>Puts KSP's own movement back exactly as it was.</summary>
        public void Release()
        {
            foreach (KeyValuePair<KFSMState, KFSMCallback> saved in originalSteps)
                saved.Key.OnFixedUpdate = saved.Value;
            originalSteps.Clear();
            foreach (KeyValuePair<KFSMEvent, KFSMEventCondition> saved in originalConditions)
                saved.Key.OnCheckCondition = saved.Value;
            originalConditions.Clear();
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

        private void Disable(KFSMEvent fsmEvent)
        {
            if (fsmEvent == null) return;
            originalConditions.Add(new KeyValuePair<KFSMEvent, KFSMEventCondition>(fsmEvent, fsmEvent.OnCheckCondition));
            fsmEvent.OnCheckCondition = Never;
        }

        private static void Nothing() { }
        private static bool Never(KFSMState state) { return false; }

        /// <param name="move">x = strafe (-1 left .. 1 right), y = forward (-1 .. 1).</param>
        public void Step(Vector3 lookForward, Vector3 up, Vector2 move, bool sprint, bool crouch, bool aiming, bool jump, float dt)
        {
            Crouch = Mathf.MoveTowards(Crouch, crouch ? 1f : 0f, dt * 6f);

            Rigidbody rb = eva.part.Rigidbody;
            if (rb == null || eva.vessel == null || eva.vessel.packed) return;

            Vector3 forward = Vector3.ProjectOnPlane(lookForward, up).normalized;
            Vector3 right = Vector3.Cross(up, forward);
            Vector3 wish = forward * move.y + right * move.x;
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            KFSMState state = eva.fsm.CurrentState;
            Ragdolled = state == eva.st_ragdoll;

            if (state == eva.st_swim_idle || state == eva.st_swim_fwd)
            {
                Swim(rb, forward, up, wish, dt);
                return;
            }

            bool controllable = state == eva.st_idle_gr || state == eva.st_idle_b_gr || state == eva.st_idle_fl || state == eva.st_land;
            if (!controllable)
            {
                // KSP only gets the active Kerbal back up while a movement key is held, and those
                // keys are locked in MW2 mode, so start the recovery ourselves.
                if (Ragdolled && (move != Vector2.zero || jump) && eva.canRecover && eva.fsm.TimeAtCurrentState > 0.2)
                    eva.fsm.RunEvent(eva.On_recover_start);
                Grounded = false;
                Sprinting = false;
                HorizontalSpeed = 0f;
                return;
            }

            Vector3 normal;
            float gap = GroundGap(up, out normal);
            // Our own floor probe decides; KSP's state only widens the margin slightly, in case the
            // probe's idea of where the feet are is a little off.
            bool onFloor = gap < GroundedGap || (gap < GroundedGap + 0.1f && state != eva.st_idle_fl);
            if (onFloor && Time.fixedTime >= jumpGraceUntil) lastGroundedTime = Time.fixedTime;
            Grounded = lastGroundedTime >= 0f && Time.fixedTime - lastGroundedTime < CoyoteTime;

            Sprinting = sprint && !crouch && !aiming && Grounded && move.y > 0.1f;
            float speed = crouch ? Settings.CrouchSpeed : Sprinting ? Settings.SprintSpeed : Settings.WalkSpeed;
            if (aiming) speed *= Settings.AimSpeedMultiplier;

            jump &= Grounded;
            if (wish != Vector3.zero || jump) Unanchor();

            Vector3 velocity = rb.velocity;
            if (Grounded)
            {
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
                    lastGroundedTime = -1f;
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

            // Top gravity up to CombatGravity near the ground, so low-gravity moons still play like
            // ground combat. Not in orbit or high above the terrain (e.g. on a space station).
            float height = eva.vessel.heightFromTerrain;
            float extraGravity = Settings.CombatGravity - LocalGravity();
            if (Settings.CombatGravity > 0f && extraGravity > 0f && height >= 0f && height < CombatGravityHeight)
                rb.AddForce(-up * extraGravity, ForceMode.Acceleration);

            // Stand upright, facing where we look. (Landing has its own short recovery animation.)
            if (state != eva.st_land) FaceForward(rb, forward, up);
        }

        private void Swim(Rigidbody rb, Vector3 forward, Vector3 up, Vector3 wish, float dt)
        {
            Grounded = false;
            Sprinting = false;
            // Like stock swimming: move on the water surface and let buoyancy handle the height.
            float speed = Mathf.Max(eva.swimSpeed, Settings.CrouchSpeed);
            Vector3 flat = Vector3.MoveTowards(Vector3.ProjectOnPlane(rb.velocity, up), wish * speed, Settings.AirAcceleration * dt);
            rb.velocity = flat;
            HorizontalSpeed = flat.magnitude;
            FaceForward(rb, forward, up);
        }

        private static void FaceForward(Rigidbody rb, Vector3 forward, Vector3 up)
        {
            rb.MoveRotation(Quaternion.LookRotation(forward, up));
            rb.angularVelocity = Vector3.zero;
        }

        private void Unanchor()
        {
            Joint joint = AnchorJoint != null ? AnchorJoint.GetValue(eva) as Joint : null;
            if (RemoveAnchor != null) RemoveAnchor.Invoke(eva, null); // clears KSP's anchor bookkeeping
            if (joint != null) Object.DestroyImmediate(joint);
        }

        /// <summary>
        /// Distance from the Kerbal's feet down to a walkable floor (walls and steep faces don't count),
        /// or float.MaxValue if there is none within reach. A sphere cast, so ledge edges still count.
        /// </summary>
        private float GroundGap(Vector3 up, out Vector3 normal)
        {
            normal = up;
            Vector3 origin = eva.transform.position + up * ProbeLift;
            float best = float.MaxValue;
            foreach (RaycastHit hit in Physics.SphereCastAll(origin, ProbeRadius, -up, 2f, GroundMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.distance <= 0f || hit.distance >= best) continue; // skip overlaps at the start
                if (Vector3.Dot(hit.normal, up) <= 0.5f) continue;        // steeper than ~60 degrees
                Part part = hit.collider.GetComponentInParent<Part>();
                if (part != null && part.vessel == eva.vessel) continue;
                best = hit.distance;
                normal = hit.normal;
            }
            return best == float.MaxValue ? float.MaxValue : best + ProbeRadius - ProbeLift - eva.halfHeight;
        }

        private float LocalGravity()
        {
            CelestialBody body = eva.vessel.mainBody;
            double r = (eva.vessel.CoMD - body.position).magnitude;
            return r > 0 ? (float)(body.gravParameter / (r * r)) : 0f;
        }
    }
}
