using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>Which loadout slot a blade was equipped into. Decides which of its abilities it uses.</summary>
    public enum BladeSlot
    {
        /// <summary>Always shown. Uses its primary attack on left click.</summary>
        Main,

        /// <summary>Shown one at a time in rotation. Uses its secondary ability on right click.</summary>
        Secondary,
    }

    /// <summary>
    /// Shared base for every blade the player can carry: following and
    /// orbiting the player, fading in and out of the rotation, and the hooks
    /// for its two abilities.
    /// </summary>
    /// <remarks>
    /// A blade has a primary attack and a secondary ability, and which one it
    /// uses depends only on the slot it was equipped into. A main blade runs
    /// its primary while the attack is held; a secondary blade runs its
    /// secondary when <see cref="BladeArsenal"/> activates it, and calls
    /// <see cref="FinishSecondary"/> when done so the rotation can move on.
    ///
    /// The base owns the orbit, because every blade rests the same way. A
    /// subclass gets the finished orbit pose each frame in <see cref="Tick"/>
    /// and decides how much of its own attack pose to blend over it.
    ///
    /// Must not be parented to the player: the player turns to face its
    /// movement, and the blade would spin with it.
    /// </remarks>
    public abstract class Blade : MonoBehaviour
    {
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        [Header("Owner")]
        [SerializeField]
        [Tooltip("Leave empty to find the player automatically.")]
        private Transform owner;

        [SerializeField]
        [Tooltip("Camera used to place the cursor. Empty uses Camera.main.")]
        private Camera targetCamera;

        [Header("Orbit")]
        [SerializeField]
        [Tooltip("Degrees per second around the player. Negative reverses direction. Keep it the same on every blade, or the main and secondary drift off opposite sides.")]
        private float orbitSpeed = 45f;

        [SerializeField]
        [Tooltip("Distance from the player.")]
        private float orbitRadius = 1.6f;

        [SerializeField]
        [Tooltip("Height above the player's pivot.")]
        private float orbitHeight = 1.2f;

        [SerializeField] private float bobAmplitude = 0.08f;
        [SerializeField] private float bobFrequency = 0.5f;

        [SerializeField]
        [Tooltip("Seconds the blade takes to catch up when the player moves. Higher is looser.")]
        private float followTime = 0.3f;

        [Header("Blending")]
        [SerializeField]
        [Tooltip("Seconds to blend from the orbit pose into an attack.")]
        private float engageTime = 0.12f;

        [SerializeField]
        [Tooltip("Seconds to blend from an attack back into the orbit pose.")]
        private float releaseTime = 0.3f;

        private PlayerInputReader input;
        private PlayerMotor motor;
        private PlayerHealth health;
        private Collider ownerCollider;

        private Vector3 anchor;
        private Vector3 anchorVelocity;

        private Renderer[] renderers;
        private MaterialPropertyBlock fadeBlock;
        private float visibility = 1f;

        /// <summary>Raised when a secondary ability has fully finished, so the rotation can advance.</summary>
        public event Action<Blade> SecondaryFinished;

        /// <summary>
        /// Raised when a new damaging window begins -- a new strike. Hitboxes
        /// clear who they have already hit here.
        /// </summary>
        public event Action HitWindowReset;

        /// <summary>Set by <see cref="Bind"/>. A blade placed in a scene by hand acts as a main blade.</summary>
        public BladeSlot Slot { get; private set; } = BladeSlot.Main;

        /// <summary>Degrees added to the orbit angle, so a main and a secondary blade never rest on top of each other.</summary>
        public float OrbitPhase { get; private set; }

        /// <summary>True from <see cref="ActivateSecondary"/> until the ability calls <see cref="FinishSecondary"/>.</summary>
        public bool SecondaryActive { get; private set; }

        /// <summary>
        /// 0 is fully dissolved, 1 fully visible. Drives the dithered fade in
        /// NeoNoir/Flat; renderers on any other shader just switch off below half.
        /// </summary>
        public float Visibility
        {
            get => visibility;
            set
            {
                visibility = Mathf.Clamp01(value);
                ApplyVisibility();
            }
        }

        /// <summary>
        /// True while the blade wants to stay visible after its secondary has
        /// finished -- an effect it is still part of. The rotation moves on
        /// regardless; this only holds off the fade out.
        /// </summary>
        public virtual bool Lingering => false;

        /// <summary>True while contact with this blade should hurt. Read by <see cref="BladeHitbox"/>.</summary>
        public virtual bool IsDealingDamage => false;

        /// <summary>The direction a hit from this blade was dealt in, for the slash effect.</summary>
        public virtual Vector3 HitVelocity => Vector3.zero;

        protected Transform Owner => owner;

        protected PlayerInputReader Input => input;

        protected PlayerMotor Motor => motor;

        /// <summary>The owner's health, or null if the player has none.</summary>
        protected PlayerHealth Health => health;

        /// <summary>The owner's collider, or null if the player has none.</summary>
        protected Collider OwnerCollider => ownerCollider;

        protected Camera ResolvedCamera => targetCamera != null ? targetCamera : Camera.main;

        /// <summary>The follow anchor's velocity. Useful for picking the blade up out of orbit with its momentum.</summary>
        protected Vector3 AnchorVelocity => anchorVelocity;

        protected float EngageTime => engageTime;

        protected float ReleaseTime => releaseTime;

        /// <summary>
        /// What the blade's attacks per second are multiplied by right now: 1
        /// normally, more while the owner stands in a <see cref="BluePerimeter"/>.
        /// Every primary attack rate must be scaled by this.
        /// </summary>
        protected float AttackSpeedMultiplier => owner != null ? BluePerimeter.AttackSpeedAt(owner.position) : 1f;

        /// <summary>True while a main blade's attack button is held. Always false for a secondary blade.</summary>
        protected bool PrimaryHeld => Slot == BladeSlot.Main && input != null && input.AttackHeld;

        /// <summary>Equips the blade to a player in a slot. Called by <see cref="BladeArsenal"/> right after spawning.</summary>
        public void Bind(Transform newOwner, BladeSlot slot, float orbitPhase)
        {
            owner = newOwner;
            input = null;
            motor = null;
            health = null;
            ownerCollider = null;

            Slot = slot;
            OrbitPhase = orbitPhase;

            if (ResolveOwner())
            {
                Snap();
            }
        }

        /// <summary>Starts the secondary ability. Ignored if it is already running.</summary>
        public void ActivateSecondary()
        {
            if (SecondaryActive)
            {
                return;
            }

            SecondaryActive = true;
            OnSecondaryBegin();
        }

        /// <summary>Begin the secondary ability. Must eventually call <see cref="FinishSecondary"/>, even if it does nothing.</summary>
        protected abstract void OnSecondaryBegin();

        /// <summary>
        /// Positions the blade for this frame. <paramref name="orbitPosition"/>
        /// and <paramref name="orbitRotation"/> are the resting pose; blend the
        /// attack pose over them.
        /// </summary>
        protected abstract void Tick(Vector3 orbitPosition, Quaternion orbitRotation, float deltaTime);

        /// <summary>Called by the ability when it is completely done, effects included.</summary>
        protected void FinishSecondary()
        {
            if (!SecondaryActive)
            {
                return;
            }

            SecondaryActive = false;
            SecondaryFinished?.Invoke(this);
        }

        protected void ResetHitWindow()
        {
            HitWindowReset?.Invoke();
        }

        /// <summary>
        /// Called by an enemy this blade, or anything it made, has just
        /// killed. Works while the blade is out of the rotation, since what it
        /// made can still be killing then.
        /// </summary>
        public void ReportKill(EnemyHealth enemy)
        {
            if (enemy != null)
            {
                OnKill(enemy, enemy.BodyBounds());
            }
        }

        /// <summary>
        /// Plays this blade's kill effect on <paramref name="enemy"/>, whose
        /// solid body fills <paramref name="body"/>. Nothing by default.
        /// </summary>
        protected virtual void OnKill(EnemyHealth enemy, Bounds body)
        {
        }

        /// <summary>
        /// The material on the blade's own model, read once while loading.
        /// Unlike looking it up later, it is found even while the blade is
        /// hidden out of the rotation.
        /// </summary>
        protected Material OwnMaterial { get; private set; }

        protected virtual void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            fadeBlock = new MaterialPropertyBlock();

            foreach (Renderer part in renderers)
            {
                if (part.sharedMaterial != null)
                {
                    OwnMaterial = part.sharedMaterial;
                    break;
                }
            }
        }

        protected virtual void OnEnable()
        {
            if (ResolveOwner())
            {
                Snap();
            }

            ApplyVisibility();
        }

        private void LateUpdate()
        {
            // LateUpdate so the player's interpolated rigidbody position for
            // this frame is already final; reading it earlier adds a frame of
            // jitter to everything below.
            if (!ResolveOwner())
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            anchor = Vector3.SmoothDamp(anchor, owner.position, ref anchorVelocity,
                                        Mathf.Max(0.0001f, followTime), Mathf.Infinity, deltaTime);

            // Off one clock shared by every blade, not each its own: a blade
            // that sat out of the rotation comes back exactly in step, so the
            // main and the shown secondary always stay OrbitPhase apart.
            float orbitAngle = Mathf.Repeat(Time.time * orbitSpeed, 360f);

            Vector3 radial = Quaternion.Euler(0f, orbitAngle + OrbitPhase, 0f) * Vector3.forward;
            float bob = Mathf.Sin(Time.time * bobFrequency * 2f * Mathf.PI) * bobAmplitude;
            Vector3 orbitOffset = radial * orbitRadius + Vector3.up * (orbitHeight + bob);

            // Flat turned inward, tip straight down.
            Quaternion orbitRotation = Quaternion.LookRotation(-radial, Vector3.up);

            Tick(anchor + orbitOffset, orbitRotation, deltaTime);
        }

        /// <summary>Moves an engage weight toward on or off at the shared engage and release rates.</summary>
        protected float StepEngage(float current, bool on, float deltaTime)
        {
            float time = on ? engageTime : releaseTime;
            return Mathf.MoveTowards(current, on ? 1f : 0f, deltaTime / Mathf.Max(0.0001f, time));
        }

        /// <summary>Smoothstep. Turns a linear engage weight into an eased blend.</summary>
        protected static float Ease(float weight)
        {
            return weight * weight * (3f - 2f * weight);
        }

        /// <summary>
        /// Rotation for a blade whose tip points along <paramref name="tip"/>,
        /// flat facing up so an edge leads. Model convention: tip down local -Y,
        /// flat facing local +Z.
        /// </summary>
        protected static Quaternion TipRotation(Vector3 tip, Vector3 fallbackFlat)
        {
            Vector3 flat = Vector3.ProjectOnPlane(Vector3.up, tip);

            // A tip pointing straight down has no "up" flat to face.
            if (flat.sqrMagnitude < 0.0001f)
            {
                flat = Vector3.ProjectOnPlane(fallbackFlat, tip);
            }

            return Quaternion.LookRotation(flat.normalized, -tip);
        }

        protected bool TryGetCursorPoint(float height, out Vector3 point)
        {
            point = default;
            Camera camera = ResolvedCamera;

            if (camera == null || input == null)
            {
                return false;
            }

            // The floor plane at the given height, not a physics raycast: the
            // blades work in 2D, and a raycast would catch walls, enemies and
            // the blades themselves.
            Ray ray = camera.ScreenPointToRay(input.PointerPosition);
            Plane plane = new Plane(Vector3.up, new Vector3(0f, height, 0f));

            if (!plane.Raycast(ray, out float enter))
            {
                return false;
            }

            point = ray.GetPoint(enter);
            return true;
        }

        /// <summary>
        /// World height of the middle of the player's body. Read from the
        /// collider rather than the pivot, which may sit at the feet or the
        /// centre depending on how the player was built.
        /// </summary>
        protected float OwnerMiddleHeight()
        {
            return ownerCollider != null ? ownerCollider.bounds.center.y : owner.position.y;
        }

        private bool ResolveOwner()
        {
            if (owner == null)
            {
                PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

                if (player == null)
                {
                    return false;
                }

                owner = player.transform;
                Snap();
            }

            if (input == null)
            {
                input = owner.GetComponent<PlayerInputReader>();
            }

            if (motor == null)
            {
                motor = owner.GetComponent<PlayerMotor>();
            }

            if (health == null)
            {
                health = owner.GetComponent<PlayerHealth>();
            }

            if (ownerCollider == null)
            {
                ownerCollider = owner.GetComponent<Collider>();
            }

            return true;
        }

        private void ApplyVisibility()
        {
            if (renderers == null)
            {
                return;
            }

            foreach (Renderer target in renderers)
            {
                if (target == null)
                {
                    continue;
                }

                Material material = target.sharedMaterial;

                if (material == null || !material.HasProperty(FadeId))
                {
                    target.enabled = visibility >= 0.5f;
                    continue;
                }

                target.enabled = visibility > 0f;

                // Clear the block entirely when fully visible: a renderer with
                // a property block drops out of SRP batching.
                if (visibility >= 1f)
                {
                    target.SetPropertyBlock(null);
                    continue;
                }

                target.GetPropertyBlock(fadeBlock);
                fadeBlock.SetFloat(FadeId, visibility);
                target.SetPropertyBlock(fadeBlock);
            }
        }

        /// <summary>Puts the blade straight into orbit, skipping all smoothing.</summary>
        [ContextMenu("Snap To Owner")]
        public void Snap()
        {
            if (owner == null)
            {
                return;
            }

            anchor = owner.position;
            anchorVelocity = Vector3.zero;

            OnSnap();
        }

        /// <summary>Reset any attack state. Called whenever the blade is put straight into orbit.</summary>
        protected virtual void OnSnap()
        {
        }

        protected virtual void OnValidate()
        {
            orbitRadius = Mathf.Max(0f, orbitRadius);
            followTime = Mathf.Max(0f, followTime);
            engageTime = Mathf.Max(0f, engageTime);
            releaseTime = Mathf.Max(0f, releaseTime);
        }
    }
}
