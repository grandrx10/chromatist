using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Blue the Banjo. As a main blade, plays slow music notes at the cursor
    /// that stack rings on the enemies they hit; five rings burst for heavy
    /// damage and halve every cooldown. As a secondary blade, draws a ring on
    /// the floor that doubles the player's attack speed while they stand in it.
    /// </summary>
    /// <remarks>
    /// Model convention matches every blade: tip down local -Y, flat facing
    /// local +Z. While playing, the banjo is held in front of the player with
    /// its tip toward the cursor, tilted by <see cref="tipTilt"/>, and further
    /// turned by <see cref="holdRotation"/> for a model that wants a different
    /// angle. Each note gives it a little strum: a quick rock about the aim
    /// that settles before the next.
    ///
    /// Primary. While the attack is held, a <see cref="SoundNote"/> leaves
    /// every 1 / <see cref="attacksPerSecond"/> seconds, scaled by any attack
    /// speed buff, picked at random from <see cref="notePrefabs"/>. Notes deal
    /// their own damage; an enemy that survives gets a ring of its
    /// <see cref="BlueMark"/>. Hits make no slash effect -- the rings are the
    /// feedback.
    ///
    /// Marks only exist while Blue is the main blade. Disabling or destroying
    /// the banjo clears them.
    ///
    /// Secondary. A <see cref="BluePerimeter"/> is drawn round the player's
    /// feet where they stand. The ability counts as finished at once, so the
    /// rotation moves straight on to the next blade, but the banjo stays
    /// behind: it floats, slowly turning, over the middle of the ring, and
    /// fades out as the ring dissolves.
    ///
    /// The rings, burst and perimeter all use <see cref="effectMaterial"/>,
    /// expected to be NeoNoir/Flat in Blue's colour, so they dissolve with the
    /// same dither as the blades.
    /// </remarks>
    public sealed class BlueBanjo : Blade
    {
        [Header("Primary: sound shot")]
        [SerializeField]
        [Tooltip("Notes per second while the attack is held.")]
        [Range(0.2f, 10f)]
        private float attacksPerSecond = 2f;

        [SerializeField] private SoundNote.Settings note = new SoundNote.Settings();

        [SerializeField]
        [Tooltip("The notes to play, one picked at random per shot. Empty uses a plain stand-in.")]
        private List<GameObject> notePrefabs = new List<GameObject>();

        [SerializeField]
        [Tooltip("Height notes fly at, relative to the middle of the player's collider.")]
        private float shotHeightOffset;

        [SerializeField]
        [Tooltip("How far ahead of the banjo, toward the cursor, notes appear.")]
        [Min(0f)]
        private float muzzleDistance = 0.5f;

        [SerializeField]
        [Tooltip("Layers that stop a note. Enemies always stop it, whatever their layer.")]
        private LayerMask obstacleLayers = 1;

        [Header("Primary: marks")]
        [SerializeField] private BlueMark.Settings marks = new BlueMark.Settings();

        [Header("Primary: hold")]
        [SerializeField]
        [Tooltip("Distance from the player to the banjo's pivot, in the direction of the cursor, while playing.")]
        [Min(0f)]
        private float holdDistance = 1.1f;

        [SerializeField]
        [Tooltip("Height the banjo holds at while playing, relative to the middle of the player's collider.")]
        private float holdHeightOffset;

        [SerializeField]
        [Tooltip("Degrees the tip angles below horizontal while held.")]
        [Range(-80f, 80f)]
        private float tipTilt = 20f;

        [SerializeField]
        [Tooltip("Extra degrees to turn the banjo by while held, on top of pointing its tip at the cursor.")]
        private Vector3 holdRotation;

        [SerializeField]
        [Tooltip("Seconds the banjo takes to follow a change of aim.")]
        [Min(0f)]
        private float aimFollowTime = 0.05f;

        [SerializeField]
        [Tooltip("Degrees the banjo rocks about the aim on each note.")]
        [Range(0f, 45f)]
        private float strumAngle = 12f;

        [SerializeField]
        [Tooltip("Seconds a strum takes to settle.")]
        [Min(0.01f)]
        private float strumTime = 0.18f;

        [Header("Secondary: perimeter")]
        [SerializeField] private BluePerimeter.Settings perimeter = new BluePerimeter.Settings();

        [Header("Secondary: float")]
        [SerializeField]
        [Tooltip("Height above the ring the banjo floats at while the ring lasts.")]
        private float floatHeight = 1.4f;

        [SerializeField]
        [Tooltip("Degrees per second the floating banjo turns at.")]
        private float floatSpin = 60f;

        [SerializeField]
        [Tooltip("Height the floating banjo bobs up and down by.")]
        [Min(0f)]
        private float floatBob = 0.12f;

        [SerializeField]
        [Tooltip("Bobs per second.")]
        [Min(0f)]
        private float floatBobRate = 0.6f;

        [SerializeField]
        [Tooltip("Degrees to turn the banjo by while floating, so it hangs the way you want. Zero is as modelled.")]
        private Vector3 floatRotation;

        [SerializeField]
        [Tooltip("Seconds the banjo takes to fly from the player to its float.")]
        [Min(0.01f)]
        private float floatEngageTime = 0.25f;

        [Header("Visuals")]
        [SerializeField]
        [Tooltip("Material for the rings, burst and perimeter, normally NeoNoir/Flat in Blue's colour. Empty uses the banjo's own.")]
        private Material effectMaterial;

        private readonly Dictionary<EnemyHealth, BlueMark> markByEnemy = new Dictionary<EnemyHealth, BlueMark>();
        private readonly List<EnemyHealth> staleMarks = new List<EnemyHealth>();

        private BladeArsenal arsenal;

        // Primary state.
        private Vector3 aim = Vector3.forward;
        private float holdYaw;
        private float holdYawVelocity;
        private float engage;
        private float cooldown;
        private float strum;
        private float strumSide = 1f;

        // Secondary state.
        private float perimeterRemaining;
        private bool perimeterRunning;
        private Vector3 floatCentre;
        private float floatBlend;
        private float floatYaw;

        private float AttacksPerSecond => attacksPerSecond * AttackSpeedMultiplier;

        /// <summary>
        /// Floating over the ring until it starts to dissolve, so the
        /// arsenal's fade out runs alongside the ring's.
        /// </summary>
        public override bool Lingering => perimeterRunning && perimeterRemaining > perimeter.fadeTime;

        /// <summary>The effect material, or the banjo's own when none is set. Never copied or edited.</summary>
        private Material ResolvedEffectMaterial
        {
            get
            {
                if (effectMaterial != null)
                {
                    return effectMaterial;
                }

                Renderer own = GetComponentInChildren<Renderer>();
                return own != null ? own.sharedMaterial : null;
            }
        }

        protected override void Awake()
        {
            base.Awake();

            // Built now, while the loadout is being spawned, rather than as a
            // hitch on the first shot or burst.
            NoteModels.Warm(notePrefabs);
        }

        protected override void Tick(Vector3 orbitPosition, Quaternion orbitRotation, float deltaTime)
        {
            Vector3 ownerPosition = Owner.position;
            bool held = PrimaryHeld;

            if (held)
            {
                UpdateAim(ownerPosition);
            }

            bool wasEngaged = engage > 0f;

            cooldown = Mathf.Max(0f, cooldown - deltaTime);
            strum = Mathf.MoveTowards(strum, 0f, deltaTime / strumTime);

            if (held && cooldown <= 0f)
            {
                Play(ownerPosition);
                cooldown = 1f / AttacksPerSecond;
            }

            engage = StepEngage(engage, held, deltaTime);

            if (!wasEngaged && engage > 0f)
            {
                // Coming out of orbit: face the aim straight away rather than
                // swinging round to it from wherever the yaw was left.
                holdYaw = Yaw(aim);
                holdYawVelocity = 0f;
            }

            holdYaw = Mathf.SmoothDampAngle(holdYaw, Yaw(aim), ref holdYawVelocity,
                                            Mathf.Max(0.0001f, aimFollowTime), Mathf.Infinity, deltaTime);

            if (Slot == BladeSlot.Main)
            {
                PruneMarks();
            }

            TickPerimeter(deltaTime);

            Vector3 position = orbitPosition;
            Quaternion rotation = orbitRotation;

            float weight = Ease(engage);
            if (weight > 0f)
            {
                HoldPose(ownerPosition, out Vector3 holdPosition, out Quaternion holdPoseRotation);
                position = Vector3.Lerp(position, holdPosition, weight);
                rotation = Quaternion.Slerp(rotation, holdPoseRotation, weight);
            }

            float floatWeight = Ease(floatBlend);
            if (floatWeight > 0f)
            {
                FloatPose(out Vector3 floatPosition, out Quaternion floatPoseRotation);
                position = Vector3.Lerp(position, floatPosition, floatWeight);
                rotation = Quaternion.Slerp(rotation, floatPoseRotation, floatWeight);
            }

            transform.SetPositionAndRotation(position, rotation);
        }

        private void UpdateAim(Vector3 ownerPosition)
        {
            if (!TryGetCursorPoint(ShotHeight(), out Vector3 point))
            {
                return;
            }

            Vector3 desired = point - ownerPosition;
            desired.y = 0f;

            // Clicking on the player gives no direction, so keep the last one.
            if (desired.sqrMagnitude > 0.0001f)
            {
                aim = desired.normalized;
            }
        }

        private float ShotHeight()
        {
            return OwnerMiddleHeight() + shotHeightOffset;
        }

        private static float Yaw(Vector3 flat)
        {
            return Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
        }

        // ---- pose ---------------------------------------------------------

        private void HoldPose(Vector3 ownerPosition, out Vector3 position, out Quaternion rotation)
        {
            Vector3 direction = Quaternion.Euler(0f, holdYaw, 0f) * Vector3.forward;

            position = ownerPosition + direction * holdDistance;
            position.y = OwnerMiddleHeight() + holdHeightOffset;

            float tilt = tipTilt * Mathf.Deg2Rad;
            Vector3 tip = direction * Mathf.Cos(tilt) + Vector3.down * Mathf.Sin(tilt);

            // Strum: a quick rock about the aim, eased out so it snaps then settles.
            float rock = strumSide * strumAngle * strum * strum;

            // Turned half round its own length, so the strings face up
            // rather than into the floor.
            rotation = Quaternion.AngleAxis(rock, direction)
                     * TipRotation(tip, -direction)
                     * Quaternion.Euler(0f, 180f, 0f)
                     * Quaternion.Euler(holdRotation);
        }

        /// <summary>Hanging over the middle of the ring, turning slowly and bobbing.</summary>
        private void FloatPose(out Vector3 position, out Quaternion rotation)
        {
            float bob = Mathf.Sin(Time.time * floatBobRate * Mathf.PI * 2f) * floatBob;

            position = floatCentre + Vector3.up * (floatHeight + bob);
            rotation = Quaternion.Euler(0f, floatYaw, 0f) * Quaternion.Euler(floatRotation);
        }

        // ---- primary ------------------------------------------------------

        private void Play(Vector3 ownerPosition)
        {
            Vector3 origin = ownerPosition + aim * (holdDistance + muzzleDistance);
            origin.y = ShotHeight();

            GameObject made = MakeNote();
            made.transform.position = origin;

            SoundNote flight = made.GetComponent<SoundNote>();
            if (flight == null)
            {
                flight = made.AddComponent<SoundNote>();
            }

            flight.Launch(origin, aim, note, obstacleLayers, OnNoteHit);

            strum = 1f;
            strumSide = -strumSide;
        }

        private GameObject MakeNote()
        {
            GameObject prefab = notePrefabs.Count > 0 ? notePrefabs[Random.Range(0, notePrefabs.Count)] : null;
            GameObject made;

            if (prefab != null)
            {
                made = NoteModels.Spawn(prefab);
            }
            else
            {
                made = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                made.transform.localScale = Vector3.one * 0.3f;

                Material material = ResolvedEffectMaterial;
                if (material != null)
                {
                    made.GetComponent<Renderer>().sharedMaterial = material;
                }
            }

            made.name = "SoundNote";
            return made;
        }

        private void OnNoteHit(EnemyHealth enemy, Vector3 point)
        {
            // Marks belong to Blue being the main blade.
            if (Slot != BladeSlot.Main || enemy == null || enemy.IsDead)
            {
                return;
            }

            if (!markByEnemy.TryGetValue(enemy, out BlueMark mark) || mark == null)
            {
                mark = BlueMark.Create(enemy, marks, ResolvedEffectMaterial, notePrefabs, OnMarkBurst);
                markByEnemy[enemy] = mark;
            }

            mark.AddRing();
        }

        /// <summary>A full mark cuts every cooldown the player has: each secondary's and the dash.</summary>
        private void OnMarkBurst(BlueMark mark)
        {
            if (arsenal == null && Owner != null)
            {
                arsenal = Owner.GetComponent<BladeArsenal>();
            }

            if (arsenal != null)
            {
                arsenal.ScaleCooldowns(marks.cooldownFactor);
            }

            if (Motor != null)
            {
                Motor.ScaleDashCooldown(marks.cooldownFactor);
            }
        }

        /// <summary>Forgets marks that have removed themselves.</summary>
        private void PruneMarks()
        {
            foreach (KeyValuePair<EnemyHealth, BlueMark> entry in markByEnemy)
            {
                if (entry.Key == null || entry.Value == null)
                {
                    staleMarks.Add(entry.Key);
                }
            }

            foreach (EnemyHealth enemy in staleMarks)
            {
                markByEnemy.Remove(enemy);
            }

            staleMarks.Clear();
        }

        private void ClearMarks()
        {
            foreach (BlueMark mark in markByEnemy.Values)
            {
                if (mark != null)
                {
                    Destroy(mark.gameObject);
                }
            }

            markByEnemy.Clear();
        }

        // ---- secondary ----------------------------------------------------

        protected override void OnSecondaryBegin()
        {
            Vector3 ownerPosition = Owner.position;
            float floor = OwnerCollider != null ? OwnerCollider.bounds.min.y : ownerPosition.y;

            floatCentre = new Vector3(ownerPosition.x, floor, ownerPosition.z);
            BluePerimeter.Create(floatCentre, perimeter, ResolvedEffectMaterial, notePrefabs);

            perimeterRemaining = perimeter.duration;
            perimeterRunning = true;

            // Done as far as the rotation is concerned: the next blade can
            // be used straight away while the banjo floats over the ring.
            FinishSecondary();
        }

        private void TickPerimeter(float deltaTime)
        {
            if (perimeterRunning)
            {
                perimeterRemaining -= deltaTime;
                floatYaw = Mathf.Repeat(floatYaw + floatSpin * deltaTime, 360f);

                if (perimeterRemaining <= 0f)
                {
                    perimeterRunning = false;
                }
            }

            floatBlend = Mathf.MoveTowards(floatBlend, perimeterRunning ? 1f : 0f, deltaTime / floatEngageTime);
        }

        // ---- lifecycle ----------------------------------------------------

        protected override void OnSnap()
        {
            engage = 0f;
            strum = 0f;
        }

        private void OnDisable()
        {
            // Put away: the ring plays out on its own, and the banjo comes
            // back from orbit next time rather than from its old float.
            perimeterRunning = false;
            floatBlend = 0f;

            // The marks belong to Blue being out; another main blade has no
            // use for them.
            ClearMarks();
        }
    }
}
