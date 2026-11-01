using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Top-down character movement for Hells.
    /// </summary>
    /// <remarks>
    /// Velocity is composed from three independent channels that are summed once
    /// per physics step:
    ///
    ///   drive     player input, accelerated toward the wish velocity
    ///   dash      a timed override that replaces drive while it runs
    ///   external  impulses that decay exponentially -- explosions, boss
    ///             shockwaves, knockback on hit, a parried bullet shoving back
    ///
    /// Keeping them separate is the point. An explosion can never be cancelled
    /// by the player releasing the stick, a dash cannot be steered mid-flight
    /// unless you choose to allow it, and adding a fourth channel later (a
    /// grapple pull, a boss vacuum) does not require touching the other three.
    ///
    /// Y velocity is left entirely to the physics engine. There is no jump, but
    /// gravity still runs so the character stays seated on the floor and rides
    /// over collider seams.
    /// </remarks>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlayerMotor : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 9f;

        [SerializeField]
        [Tooltip("Units per second squared while the stick is held. High values feel snappy and arcade-like.")]
        private float acceleration = 120f;

        [SerializeField]
        [Tooltip("Units per second squared while the stick is neutral.")]
        private float deceleration = 160f;

        [Header("Facing")]
        [SerializeField] private bool faceMoveDirection = true;
        [SerializeField] private float turnSpeed = 1440f;

        [Header("Dash")]
        [SerializeField] private float dashSpeed = 30f;
        [SerializeField] private float dashDuration = 0.13f;
        [SerializeField] private float dashCooldown = 0.4f;

        [SerializeField]
        [Tooltip("Off means the dash direction is locked at the moment it starts.")]
        private bool steerDuringDash;

        [Header("External forces")]
        [SerializeField]
        [Tooltip("How quickly knockback bleeds off. Higher is snappier recovery.")]
        private float externalDamping = 5.5f;

        [Header("Ground")]
        [SerializeField]
        [Tooltip("Steepest floor, in degrees, that counts as ground. Anything steeper is a wall.")]
        private float maxGroundAngle = 60f;

        [SerializeField]
        [Tooltip("Upward speed allowed on top of what the slope underfoot needs, for steps, seams and rising platforms.")]
        private float riseAllowance = 4f;

        private Rigidbody body;
        private PlayerInputReader input;
        private DustTrail dust;

        private Vector3 driveVelocity;
        private Vector3 dashVelocity;
        private Vector3 externalVelocity;

        private float dashTimeRemaining;
        private float dashCooldownRemaining;

        // Per-dash rules, so an ability dash can differ from the dash button.
        private bool dashSteerable;
        private float dashExitSpeed = -1f;
        private bool dashSetsCooldown = true;

        private Vector3 facing = Vector3.forward;

        // Walkable contact normals from the last physics step, gathered by the
        // collision callbacks and consumed by the next FixedUpdate.
        private readonly List<Vector3> groundNormals = new List<Vector3>();

        // Set by an upward impulse, so a deliberate launch is not clamped.
        private bool launched;

        // Keyed by whoever applied each one, so a source can only ever
        // remove its own and two sources stack by multiplying.
        private readonly Dictionary<object, float> speedModifiers = new Dictionary<object, float>();

        // Set by StartAutoRun: the velocity the player is carried along at,
        // and how fast input can steer across it.
        private Vector3? autoRunVelocity;
        private float autoRunSideSpeed;

        // Added to a dash taken mid-run, so it goes sideways without losing
        // the run's forward speed.
        private Vector3 dashCarry;

        /// <summary>Raised when a dash begins. Hook i-frames, the cape snap, and impact sparks here.</summary>
        public event Action DashStarted;

        /// <summary>Raised when a dash ends, whether it completed or was cut short.</summary>
        public event Action DashEnded;

        public bool IsDashing => dashTimeRemaining > 0f;

        public bool CanDash => !IsDashing && dashCooldownRemaining <= 0f;

        /// <summary>Normal walking speed, before any speed modifier.</summary>
        public float MoveSpeed => moveSpeed;

        /// <summary>True between <see cref="StartAutoRun"/> and <see cref="StopAutoRun"/>.</summary>
        public bool IsAutoRunning => autoRunVelocity.HasValue;

        /// <summary>
        /// While set, the character faces this flat direction instead of the
        /// way it moves: to run backwards, say, facing what chases it.
        /// </summary>
        public Vector3? FacingOverride { get; set; }

        /// <summary>
        /// Carries the player along at <paramref name="velocity"/> whatever the
        /// input says, until <see cref="StopAutoRun"/>. Input only steers
        /// across that direction, at up to <paramref name="sideSpeed"/>.
        /// Speed modifiers are ignored meanwhile. For a forced run, such as
        /// <see cref="SpearHallIntro"/>'s.
        /// </summary>
        public void StartAutoRun(Vector3 velocity, float sideSpeed)
        {
            autoRunVelocity = new Vector3(velocity.x, 0f, velocity.z);
            autoRunSideSpeed = Mathf.Max(0f, sideSpeed);
        }

        /// <summary>Hands movement back to the input. The run speed bleeds off at the usual deceleration.</summary>
        public void StopAutoRun()
        {
            autoRunVelocity = null;
        }

        /// <summary>Takes <paramref name="seconds"/> off the dash cooldown, stopping at zero.</summary>
        public void ReduceDashCooldown(float seconds)
        {
            dashCooldownRemaining = Mathf.Max(0f, dashCooldownRemaining - Mathf.Max(0f, seconds));
        }

        /// <summary>Multiplies the time left on the dash cooldown by <paramref name="factor"/>: 0.5 halves what remains.</summary>
        public void ScaleDashCooldown(float factor)
        {
            dashCooldownRemaining *= Mathf.Max(0f, factor);
        }

        /// <summary>
        /// What walking speed is multiplied by: every modifier applied,
        /// multiplied together. 1 with none. Dashes and impulses ignore it.
        /// </summary>
        public float SpeedMultiplier
        {
            get
            {
                float multiplier = 1f;

                foreach (float modifier in speedModifiers.Values)
                {
                    multiplier *= modifier;
                }

                return multiplier;
            }
        }

        /// <summary>
        /// Multiplies walking speed by <paramref name="multiplier"/> until
        /// <see cref="RemoveSpeedModifier"/> is called with the same
        /// <paramref name="source"/>. Applying again from the same source
        /// replaces its modifier rather than stacking.
        /// </summary>
        public void SetSpeedModifier(object source, float multiplier)
        {
            speedModifiers[source] = Mathf.Max(0f, multiplier);
        }

        public void RemoveSpeedModifier(object source)
        {
            speedModifiers.Remove(source);
        }

        /// <summary>Full rigidbody velocity, including the vertical component.</summary>
        public Vector3 Velocity => body != null ? body.linearVelocity : Vector3.zero;

        /// <summary>Horizontal velocity only. This is what the camera and cape should follow.</summary>
        public Vector3 PlanarVelocity
        {
            get
            {
                Vector3 velocity = Velocity;
                return new Vector3(velocity.x, 0f, velocity.z);
            }
        }

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            input = GetComponent<PlayerInputReader>();

            // Set in code as well as in the scene so a hand-built player still
            // behaves: rotation is driven manually, and a fast dash through thin
            // colliders needs continuous detection.
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            ApplyFrictionlessMaterial();

            facing = transform.forward;

            // Add a DustTrail to the player by hand to change how it looks.
            dust = DustTrail.On(gameObject);
        }

        /// <summary>
        /// Velocity is set directly every step, so friction does nothing useful
        /// here -- it only grabs the character against walls and stops it
        /// sliding along them. Minimum combine means the wall's own material
        /// cannot add friction back. Colliders given a material by hand keep it.
        /// </summary>
        private void ApplyFrictionlessMaterial()
        {
            PhysicsMaterial frictionless = null;

            foreach (Collider collider in GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || collider.sharedMaterial != null)
                {
                    continue;
                }

                if (frictionless == null)
                {
                    frictionless = new PhysicsMaterial("PlayerFrictionless")
                    {
                        dynamicFriction = 0f,
                        staticFriction = 0f,
                        bounciness = 0f,
                        frictionCombine = PhysicsMaterialCombine.Minimum,
                        bounceCombine = PhysicsMaterialCombine.Minimum,
                    };
                }

                collider.sharedMaterial = frictionless;
            }
        }

        private void Update()
        {
            if (input != null && input.DashQueued && CanDash)
            {
                if (IsAutoRunning)
                {
                    DashAcrossRun();
                }
                else
                {
                    Dash(input.MoveWorld);
                }

                input.ConsumeDash();
            }

            // Every dash, the button's and abilities' alike, and a forced run.
            if (IsDashing || IsAutoRunning)
            {
                dust.KeepEmitting();
            }
        }

        private void FixedUpdate()
        {
            float deltaTime = Time.fixedDeltaTime;

            TickDash(deltaTime);
            TickExternal(deltaTime);

            Vector3 wish = autoRunVelocity.HasValue
                ? AutoRunWish()
                : (input != null ? input.MoveWorld : Vector3.zero) * (moveSpeed * SpeedMultiplier);
            float rate = wish.sqrMagnitude > 0.000001f ? acceleration : deceleration;
            driveVelocity = Vector3.MoveTowards(driveVelocity, wish, rate * deltaTime);

            Vector3 planar = IsDashing ? dashVelocity + dashCarry : driveVelocity;
            planar += externalVelocity;

            // Preserve gravity. Overwriting Y here would leave the character
            // hanging on slopes and floating off ledges.
            Vector3 current = body.linearVelocity;
            float vertical = ClampRise(current.y, planar);
            body.linearVelocity = new Vector3(planar.x, vertical, planar.z);

            UpdateFacing(planar, deltaTime);
        }

        /// <summary>The auto-run velocity, plus the sideways part of the input across it.</summary>
        private Vector3 AutoRunWish()
        {
            Vector3 run = autoRunVelocity.Value;

            if (input == null || run.sqrMagnitude < 0.000001f)
            {
                return run;
            }

            Vector3 side = Vector3.Cross(Vector3.up, run.normalized);
            return run + side * (Vector3.Dot(input.MoveWorld, side) * autoRunSideSpeed);
        }

        /// <summary>
        /// Limits upward speed to what the ground actually asks for. Driving
        /// fast into a ramp makes the solver resolve the overlap by shoving the
        /// body up, and at dash speed that shove launches it into the sky; at
        /// the top of a ramp the climb speed would carry it off the same way.
        /// The rise a slope needs is exact from its normal, so anything past
        /// that, plus a little for steps, is the solver's doing and is cut.
        /// </summary>
        private float ClampRise(float vertical, Vector3 planar)
        {
            if (launched)
            {
                if (vertical <= 0f)
                {
                    launched = false;
                }

                groundNormals.Clear();
                return vertical;
            }

            if (vertical <= 0f)
            {
                groundNormals.Clear();
                return vertical;
            }

            // Airborne with no launch: nothing should be lifting the body.
            float maxRise = 0f;

            if (groundNormals.Count > 0)
            {
                float slopeRise = 0f;

                foreach (Vector3 normal in groundNormals)
                {
                    // Vertical speed that keeps the planar motion on this plane.
                    float rise = -(normal.x * planar.x + normal.z * planar.z) / normal.y;
                    slopeRise = Mathf.Max(slopeRise, rise);
                }

                maxRise = slopeRise + riseAllowance;
            }

            groundNormals.Clear();
            return Mathf.Min(vertical, maxRise);
        }

        private void OnCollisionEnter(Collision collision)
        {
            GatherGroundNormals(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            GatherGroundNormals(collision);
        }

        private void GatherGroundNormals(Collision collision)
        {
            float minNormalY = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);

            for (int i = 0; i < collision.contactCount; i++)
            {
                Vector3 normal = collision.GetContact(i).normal;
                if (normal.y >= minNormalY)
                {
                    groundNormals.Add(normal);
                }
            }
        }

        private void TickDash(float deltaTime)
        {
            if (dashCooldownRemaining > 0f)
            {
                dashCooldownRemaining -= deltaTime;
            }

            if (!IsDashing)
            {
                return;
            }

            if (dashSteerable && input != null && input.MoveWorld.sqrMagnitude > 0.000001f)
            {
                dashVelocity = input.MoveWorld.normalized * dashSpeed;
            }

            dashTimeRemaining -= deltaTime;

            if (dashTimeRemaining <= 0f)
            {
                EndDash();
            }
        }

        private void EndDash()
        {
            dashTimeRemaining = 0f;

            // Hand the dash speed to the drive channel so the character
            // decelerates out of the dash rather than stopping dead. A capped
            // exit keeps a very fast dash from sliding on for metres after.
            driveVelocity = (dashExitSpeed >= 0f
                ? Vector3.ClampMagnitude(dashVelocity, dashExitSpeed)
                : dashVelocity) + dashCarry;

            dashCarry = Vector3.zero;

            if (dashSetsCooldown)
            {
                dashCooldownRemaining = dashCooldown;
            }

            DashEnded?.Invoke();
        }

        private void TickExternal(float deltaTime)
        {
            if (externalVelocity.sqrMagnitude < 0.0001f)
            {
                externalVelocity = Vector3.zero;
                return;
            }

            // Exponential decay is framerate-independent and tunes more
            // predictably than a linear falloff.
            externalVelocity *= Mathf.Exp(-externalDamping * deltaTime);
        }

        private void UpdateFacing(Vector3 planar, float deltaTime)
        {
            if (!faceMoveDirection)
            {
                return;
            }

            Vector3 direction = FacingOverride ?? new Vector3(planar.x, 0f, planar.z);
            direction.y = 0f;

            if (direction.sqrMagnitude > 0.04f)
            {
                facing = direction.normalized;
            }

            Quaternion target = Quaternion.LookRotation(facing, Vector3.up);
            body.MoveRotation(Quaternion.RotateTowards(body.rotation, target, turnSpeed * deltaTime));
        }

        /// <summary>Dashes in the given direction, or in the current facing if it is zero.</summary>
        public void Dash(Vector3 direction)
        {
            if (!CanDash)
            {
                return;
            }

            Vector3 flattened = new Vector3(direction.x, 0f, direction.z);
            if (flattened.sqrMagnitude < 0.000001f)
            {
                flattened = facing;
            }

            dashVelocity = flattened.normalized * dashSpeed;
            dashTimeRemaining = dashDuration;
            facing = flattened.normalized;
            dashCarry = Vector3.zero;

            dashSteerable = steerDuringDash;
            dashExitSpeed = -1f;
            dashSetsCooldown = true;

            DashStarted?.Invoke();
        }

        /// <summary>
        /// A dash that covers exactly <paramref name="distance"/> in
        /// <paramref name="duration"/> seconds, in a straight line. For
        /// abilities: it cannot be steered, ignores and does not trigger the
        /// dash button's cooldown, and exits at walking speed. Returns false
        /// if a dash is already running.
        /// </summary>
        public bool DashFixed(Vector3 direction, float distance, float duration)
        {
            if (IsDashing)
            {
                return false;
            }

            Vector3 flattened = new Vector3(direction.x, 0f, direction.z);
            if (flattened.sqrMagnitude < 0.000001f)
            {
                flattened = facing;
            }

            float fixedStep = Time.fixedDeltaTime;
            duration = Mathf.Max(fixedStep, duration);

            dashVelocity = flattened.normalized * (Mathf.Max(0f, distance) / duration);
            dashCarry = Vector3.zero;

            // TickDash counts down before the velocity is applied, so a dash
            // of exactly N physics steps would only move for N - 1 of them.
            // Half a step of slack makes it run all N.
            dashTimeRemaining = duration + fixedStep * 0.5f;
            facing = flattened.normalized;

            dashSteerable = false;
            dashExitSpeed = moveSpeed;
            dashSetsCooldown = false;

            DashStarted?.Invoke();
            return true;
        }

        /// <summary>
        /// The dash button mid-run: dashes straight left or right across the
        /// run, whichever way the stick leans, keeping the run's speed. With
        /// no lean either way the press does nothing.
        /// </summary>
        private void DashAcrossRun()
        {
            Vector3 run = autoRunVelocity.Value;
            Vector3 side = Vector3.Cross(Vector3.up, run.normalized);
            float lean = Vector3.Dot(input.MoveWorld, side);

            if (Mathf.Abs(lean) < 0.3f)
            {
                return;
            }

            Dash(side * Mathf.Sign(lean));
            dashCarry = run;

            // Facing the way the player is really going, not straight across.
            facing = (dashVelocity + dashCarry).normalized;
        }

        /// <summary>Dashes in the direction currently faced.</summary>
        public void Dash()
        {
            Dash(facing);
        }

        /// <summary>Cuts a dash short. Use on a wall hit or a cancel into an attack.</summary>
        public void CancelDash()
        {
            if (!IsDashing)
            {
                return;
            }

            EndDash();
        }

        /// <summary>
        /// Adds an instant velocity change that decays on its own. This is the
        /// entry point for knockback, parries and boss shoves.
        /// </summary>
        public void AddImpulse(Vector3 impulse)
        {
            externalVelocity += new Vector3(impulse.x, 0f, impulse.z);

            if (Mathf.Abs(impulse.y) > 0.0001f && body != null)
            {
                // Vertical goes to the rigidbody so gravity brings it back down.
                body.linearVelocity += new Vector3(0f, impulse.y, 0f);
                launched |= impulse.y > 0f;
            }
        }

        /// <summary>
        /// Radial knockback from a point, falling off linearly to nothing at the
        /// edge of the radius.
        /// </summary>
        public void AddExplosionForce(float force, Vector3 origin, float radius, float upwardsModifier = 0f)
        {
            Vector3 offset = transform.position - origin;
            offset.y = 0f;

            float distance = offset.magnitude;
            if (radius <= 0f || distance > radius)
            {
                return;
            }

            float falloff = 1f - (distance / radius);

            // Directly overlapping the origin gives no direction to push along,
            // so pick the facing rather than dividing by zero.
            Vector3 direction = distance > 0.0001f ? offset / distance : facing;

            AddImpulse(direction * (force * falloff) + Vector3.up * (upwardsModifier * falloff));
        }

        /// <summary>Moves the character without the physics engine sweeping it there.</summary>
        public void Teleport(Vector3 position)
        {
            body.position = position;
            transform.position = position;
            driveVelocity = Vector3.zero;
            dashVelocity = Vector3.zero;
            externalVelocity = Vector3.zero;
            body.linearVelocity = Vector3.zero;
        }

        /// <summary>Teleports the character and turns it to face <paramref name="forward"/>, flattened.</summary>
        public void Teleport(Vector3 position, Vector3 forward)
        {
            Teleport(position);

            forward.y = 0f;

            if (forward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            facing = forward.normalized;
            Quaternion rotation = Quaternion.LookRotation(facing, Vector3.up);
            body.rotation = rotation;
            transform.rotation = rotation;
        }
    }
}
