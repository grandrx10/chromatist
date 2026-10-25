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

        private Rigidbody body;
        private PlayerInputReader input;

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

        // Keyed by whoever applied each one, so a source can only ever
        // remove its own and two sources stack by multiplying.
        private readonly Dictionary<object, float> speedModifiers = new Dictionary<object, float>();

        /// <summary>Raised when a dash begins. Hook i-frames, the cape snap, and impact sparks here.</summary>
        public event Action DashStarted;

        /// <summary>Raised when a dash ends, whether it completed or was cut short.</summary>
        public event Action DashEnded;

        public bool IsDashing => dashTimeRemaining > 0f;

        public bool CanDash => !IsDashing && dashCooldownRemaining <= 0f;

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

            facing = transform.forward;
        }

        private void Update()
        {
            if (input != null && input.DashQueued && CanDash)
            {
                Dash(input.MoveWorld);
                input.ConsumeDash();
            }
        }

        private void FixedUpdate()
        {
            float deltaTime = Time.fixedDeltaTime;

            TickDash(deltaTime);
            TickExternal(deltaTime);

            Vector3 wish = (input != null ? input.MoveWorld : Vector3.zero) * (moveSpeed * SpeedMultiplier);
            float rate = wish.sqrMagnitude > 0.000001f ? acceleration : deceleration;
            driveVelocity = Vector3.MoveTowards(driveVelocity, wish, rate * deltaTime);

            Vector3 planar = IsDashing ? dashVelocity : driveVelocity;
            planar += externalVelocity;

            // Preserve gravity. Overwriting Y here would leave the character
            // hanging on slopes and floating off ledges.
            Vector3 current = body.linearVelocity;
            body.linearVelocity = new Vector3(planar.x, current.y, planar.z);

            UpdateFacing(planar, deltaTime);
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
            driveVelocity = dashExitSpeed >= 0f
                ? Vector3.ClampMagnitude(dashVelocity, dashExitSpeed)
                : dashVelocity;

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

            Vector3 direction = new Vector3(planar.x, 0f, planar.z);
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
    }
}
