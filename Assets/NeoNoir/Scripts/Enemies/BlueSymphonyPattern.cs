using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// Blue the Banjo's fight, all one attack: it leaps to the middle of the
    /// arena and hovers there, then, until it is beaten, charges up and
    /// looses batches of homing notes at the player, while double notes form
    /// in the sky round them and drop to leave pools of music that hurt.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The drop: it appears <see cref="dropHeight"/> straight above the
    ///      enemy's <see cref="Enemy.ArenaCentre"/>, upright and facing the
    ///      player, and drops over <see cref="dropTime"/> (see
    ///      <see cref="BladeDrop"/>), while a
    ///      <see cref="WarningCircle"/> of <see cref="landingRadius"/> fills
    ///      where it will land. It settles hovering
    ///      <see cref="hoverHeight"/> off the floor, with a jolt of the camera
    ///      and <see cref="landingDamage"/> to the player inside the circle.
    ///      Its colliders are off in the air. Without an arena centre it drops
    ///      where it is. It hovers there for the rest of the fight,
    ///      turning to face the player.
    ///   2. Homing notes, over and over: a <see cref="SceptorCharge"/> gathers
    ///      at its note point for <see cref="chargeTime"/>; then the glow goes
    ///      in an <see cref="EnergyBurst"/>, the camera shakes, and
    ///      <see cref="noteCount"/> <see cref="HomingMissile"/>s carrying
    ///      single notes scatter out of it and turn on the player, as the
    ///      sceptre's do. The next charge starts straight away. The player's
    ///      parry catches them.
    ///   3. Double notes, all the while from the landing: every
    ///      <see cref="dropInterval"/>, a <see cref="FallingNote"/> forms in
    ///      the sky over a random spot within <see cref="dropRadius"/> of the
    ///      player, inside the enemy's <see cref="Enemy.Arena"/> if it has
    ///      one, and drops to spill a pool of music there.
    ///
    /// The note point is the object called <see cref="notePointName"/> under
    /// the enemy; without one, the middle of its model.
    ///
    /// The single notes face the camera as they fly, like Blue's own shots.
    /// The mini notes rising off the pools are the pool settings' Fume Art.
    ///
    /// The attack never ends by itself, so give the enemy only this pattern.
    /// Its art is posed tip down local -Y, its front facing local -Z. Its Rigidbody, if it has one, is kinematic
    /// throughout.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Blade Attacks/Blue Symphony", fileName = "BlueSymphony")]
    public sealed class BlueSymphonyPattern : AttackPattern
    {
        /// <summary>Spots tried for each double note before it is skipped.</summary>
        private const int SpotTries = 16;

        [Header("Drop to the middle")]
        [SerializeField]
        [Tooltip("How far above where it settles it appears, to drop straight down.")]
        [FormerlySerializedAs("leapHeight")]
        [Min(0f)]
        private float dropHeight = 8f;

        [SerializeField]
        [Tooltip("Seconds the drop takes. It gathers speed all the way down.")]
        [FormerlySerializedAs("leapTime")]
        [Min(0.01f)]
        private float dropTime = 0.9f;

        [SerializeField]
        [Tooltip("How far off the floor the bottom of its model hovers once landed.")]
        [Min(0f)]
        private float hoverHeight = 1f;

        [SerializeField]
        [Tooltip("Radius of the warning circle where it lands, and of the landing's damage.")]
        [Min(0f)]
        private float landingRadius = 3f;

        [SerializeField]
        [Tooltip("Width of the warning circle's outline.")]
        [Min(0f)]
        private float landingOutline = 0.12f;

        [SerializeField]
        [Tooltip("Damage to the player inside the circle as it lands.")]
        [Min(0f)]
        private float landingDamage = 15f;

        [SerializeField]
        private Color warningColour = new Color(1f, 0.1f, 0.15f);

        [SerializeField]
        [Tooltip("Height above the floor the warning circle is drawn at, to clear a rug or carpet.")]
        [Range(0f, 1f)]
        private float warningHeight = 0.12f;

        [SerializeField]
        [Tooltip("How far the landing jolts the camera. 0 for none.")]
        [Min(0f)]
        private float landingShake = 0.4f;

        [SerializeField]
        [Min(0.01f)]
        private float landingShakeDuration = 0.3f;

        [SerializeField]
        [Tooltip("Degrees a second it turns to keep facing the player while it hovers.")]
        [Min(0f)]
        private float turnRate = 360f;

        [SerializeField]
        [Tooltip("What counts as floor. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [Header("Homing notes")]
        [SerializeField]
        [Tooltip("Seconds each charge builds before a batch is loosed. The next starts straight after.")]
        [Min(0.1f)]
        private float chargeTime = 2f;

        [SerializeField]
        [Tooltip("Name of the object the notes gather at and fly from, somewhere under the enemy. Not found, the middle of its model is used.")]
        private string notePointName = "NotePoint";

        [SerializeField]
        private SceptorCharge.Settings charge = new SceptorCharge.Settings
        {
            colour = new Color(0.2f, 0.8f, 3f, 1f),
        };

        [SerializeField]
        private EnergyBurst.Settings release = new EnergyBurst.Settings
        {
            flashSize = 1.4f,
            flashTime = 0.2f,
            sparkCount = 14,
            sparkSize = 0.35f,
            sparkSpeed = new Vector2(5f, 10f),
            sparkLife = 0.4f,
            colour = new Color(0.2f, 0.8f, 3f, 1f),
        };

        [SerializeField]
        [Tooltip("How far the camera shakes on each release. 0 for none.")]
        [Min(0f)]
        private float shakeStrength = 0.2f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.2f;

        [SerializeField]
        [Tooltip("Single note art for the homing notes, e.g. NoteSingle. Faces -Z as modelled. Empty flies plain glowing balls.")]
        private GameObject singleNote;

        [SerializeField]
        [Tooltip("Uniform scale applied to the single note art.")]
        [Min(0.01f)]
        private float singleNoteScale = 1f;

        [SerializeField]
        [Tooltip("Degrees the single notes rock side to side as they fly.")]
        [Range(0f, 45f)]
        private float singleNoteRock = 15f;

        [SerializeField]
        [Tooltip("Notes in each batch.")]
        [Range(1, 16)]
        private int noteCount = 5;

        [SerializeField]
        [Tooltip("Degrees each scatter direction may stray from an even spread.")]
        [Range(0f, 90f)]
        private float scatterJitter = 30f;

        [SerializeField]
        [Tooltip("Most degrees a note may be tilted upward as it scatters.")]
        [Range(0f, 80f)]
        private float scatterLift = 25f;

        [SerializeField]
        private HomingMissile.Settings missile = new HomingMissile.Settings
        {
            damage = 5f,
            colour = new Color(0.2f, 0.8f, 3f, 1f),
            blastRadius = 1f,
            blast = new BlastSphere.Settings
            {
                growTime = 0.1f,
                fadeTime = 0.25f,
                colour = new Color(0.2f, 0.8f, 3f, 1f),
                outlineWidth = 0.08f,
            },
        };

        [Header("Double notes")]
        [SerializeField]
        [Tooltip("Double note art for the notes that drop from the sky, e.g. Note. Faces -Z as modelled. Empty drops plain balls.")]
        private GameObject doubleNote;

        [SerializeField]
        [Tooltip("Seconds between one double note starting to form and the next.")]
        [Min(0.05f)]
        private float dropInterval = 0.75f;

        [SerializeField]
        [Tooltip("Furthest from the player, measured flat, a double note drops.")]
        [Min(0f)]
        private float dropRadius = 10f;

        [SerializeField]
        [Tooltip("The double note's fall and the pool it spills. Set the pool's Fume Art to the mini notes that rise off it.")]
        private FallingNote.Settings falling = new FallingNote.Settings();

        public override IEnumerator Perform(Enemy enemy)
        {
            enemy.VelocityOverride = Vector3.zero;

            // Posed by hand for the whole fight: the enemy's own turning
            // only yaws, and would not hold the hover.
            enemy.PhysicsDriven = true;

            Rigidbody body = enemy.GetComponent<Rigidbody>();

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            Vector3 centre = enemy.ArenaCentre ?? enemy.transform.position;

            foreach (object frame in Leap(enemy, centre))
            {
                yield return frame;
            }

            Transform point = FindPoint(enemy);
            float dropWait = 0f;

            // Until it dies, which stops this from outside.
            while (true)
            {
                SceptorCharge gathering = SceptorCharge.Create(point, enemy, chargeTime, charge);

                for (float clock = 0f; clock < chargeTime; clock += Time.deltaTime)
                {
                    FacePlayer(enemy, Time.deltaTime);
                    dropWait = DropNotes(enemy, centre, dropWait - Time.deltaTime);
                    yield return null;
                }

                if (gathering != null)
                {
                    Destroy(gathering.gameObject);
                }

                Release(enemy, point.position);
            }
        }

        /// <summary>
        /// Drops a double note if one is due, given <paramref name="wait"/>
        /// left until it is. Returns the wait until the next.
        /// </summary>
        private float DropNotes(Enemy enemy, Vector3 centre, float wait)
        {
            while (wait <= 0f)
            {
                wait += dropInterval;

                Vector3 around = enemy.HasTarget ? enemy.Target.position : centre;

                // The first spot clear of every pool, or failing that, the
                // one that overlaps them least.
                Vector3? best = null;
                float bestOverlap = float.MaxValue;

                for (int attempt = 0; attempt < SpotTries && bestOverlap > 0f; attempt++)
                {
                    Vector2 offset = Random.insideUnitCircle * dropRadius;
                    Vector3 spot = around + new Vector3(offset.x, 0f, offset.y);

                    if (enemy.Arena != null && !enemy.Arena.Contains(spot))
                    {
                        continue;
                    }

                    float overlap = PoolOverlap(spot);

                    if (overlap < bestOverlap)
                    {
                        best = spot;
                        bestOverlap = overlap;
                    }
                }

                if (best.HasValue)
                {
                    FallingNote.Create(doubleNote, FindFloor(best.Value), falling);
                }
            }

            return wait;
        }

        /// <summary>
        /// How far a pool spilt at <paramref name="spot"/> would reach into
        /// the deepest of those on the floor or still falling, measured flat.
        /// 0 if it is clear of them all.
        /// </summary>
        private float PoolOverlap(Vector3 spot)
        {
            float radius = falling.pool.maxRadius;
            float worst = 0f;

            foreach (PotionPool pool in PotionPool.Live)
            {
                worst = Mathf.Max(worst, radius + pool.MaxRadius - Flat(pool.transform.position - spot).magnitude);
            }

            foreach (FallingNote note in FallingNote.Live)
            {
                worst = Mathf.Max(worst, radius + note.PoolRadius - Flat(note.Floor - spot).magnitude);
            }

            return worst;
        }

        /// <summary>The burst at the end of a charge, and a batch of homing notes out of it.</summary>
        private void Release(Enemy enemy, Vector3 origin)
        {
            EnergyBurst.Create(origin, release);

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }

            Transform target = enemy.HasTarget ? enemy.Target : null;
            float start = Random.Range(0f, 360f);

            for (int i = 0; i < noteCount; i++)
            {
                float heading = start + 360f * i / noteCount + Random.Range(-scatterJitter, scatterJitter);
                float lift = Random.Range(0f, scatterLift);
                Vector3 direction = Quaternion.Euler(-lift, heading, 0f) * Vector3.forward;

                HomingMissile.Launch(NoteArt(), origin, direction, target, enemy.transform, missile);
            }
        }

        /// <summary>
        /// A single note for a homing missile to carry: a holder the missile
        /// turns along its flight, with the note inside it facing the camera.
        /// Null without art, for the missile's own glowing ball.
        /// </summary>
        private GameObject NoteArt()
        {
            if (singleNote == null)
            {
                return null;
            }

            GameObject holder = new GameObject("HomingNote");
            GameObject note = Instantiate(singleNote, holder.transform);
            note.transform.localPosition = Vector3.zero;
            note.transform.localScale = singleNote.transform.localScale * singleNoteScale;

            foreach (Renderer part in note.GetComponentsInChildren<Renderer>())
            {
                part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                part.receiveShadows = false;
            }

            FaceCamera.Add(note, singleNoteRock, 2.5f);
            return holder;
        }

        /// <summary>Turns it on the spot, upright, toward the player.</summary>
        private void FacePlayer(Enemy enemy, float deltaTime)
        {
            if (!enemy.HasTarget)
            {
                return;
            }

            Vector3 toward = Flat(enemy.TargetPoint - enemy.transform.position);

            if (toward.sqrMagnitude < 0.0001f)
            {
                return;
            }

            Transform model = enemy.Model;
            model.rotation = Quaternion.RotateTowards(model.rotation, Upright(toward), turnRate * deltaTime);
        }

        /// <summary>
        /// A drop straight down onto <paramref name="centre"/>, upright toward
        /// the player, to settle hovering over the floor there.
        /// </summary>
        private IEnumerable Leap(Enemy enemy, Vector3 centre)
        {
            Transform root = enemy.transform;
            Transform model = enemy.Model;

            List<Collider> switchedOff = new List<Collider>();

            foreach (Collider part in enemy.GetComponentsInChildren<Collider>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    switchedOff.Add(part);
                }
            }

            // Measured with its colliders off, so the ray never finds it.
            Vector3 floor = FindFloor(centre);
            Vector3 start = root.position;

            Vector3 facing = enemy.HasTarget ? Flat(enemy.TargetPoint - floor) : Flat(floor - start);

            if (facing.sqrMagnitude < 0.0001f)
            {
                facing = Flat(model.forward);
            }

            facing = facing.sqrMagnitude > 0.0001f ? facing.normalized : Vector3.forward;

            Quaternion upright = Upright(facing);
            Vector3 hover = AmberTrianglePattern.Buried(root, model, floor, upright, floor.y + hoverHeight);

            WarningCircle warning = WarningCircle.Create(floor + Vector3.up * warningHeight, landingRadius,
                                                         landingOutline, null, warningColour);
            model.rotation = upright;

            foreach (object frame in BladeDrop.Fall(root, hover, dropHeight, dropTime, warning))
            {
                yield return frame;
            }

            warning.Release();

            if (landingShake > 0f)
            {
                TopDownCamera.Shake(landingShake, landingShakeDuration);
            }

            Hurt(enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null, floor, landingRadius, landingDamage);

            foreach (Collider part in switchedOff)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }
        }

        /// <summary>
        /// Upright, facing <paramref name="toward"/>, a flat direction. Blue's
        /// art faces back along its local +Z, so it is turned half round.
        /// </summary>
        private static Quaternion Upright(Vector3 toward)
        {
            return Quaternion.LookRotation(toward, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
        }

        /// <summary>Hurts <paramref name="player"/> if within <paramref name="radius"/> of <paramref name="centre"/>, measured flat.</summary>
        private static void Hurt(PlayerHealth player, Vector3 centre, float radius, float damage)
        {
            if (player == null || player.IsDead || damage <= 0f)
            {
                return;
            }

            Vector3 offset = Flat(player.transform.position - centre);

            if (offset.sqrMagnitude <= radius * radius)
            {
                player.TakeDamage(damage, centre);
            }
        }

        /// <summary>
        /// The named note point under the enemy, or, without one, a new one
        /// made at the middle of its model.
        /// </summary>
        private Transform FindPoint(Enemy enemy)
        {
            if (!string.IsNullOrEmpty(notePointName))
            {
                foreach (Transform child in enemy.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == notePointName)
                    {
                        return child;
                    }
                }
            }

            Transform model = enemy.Model;
            Bounds bounds = new Bounds(model.position, Vector3.zero);
            bool found = false;

            foreach (Renderer part in model.GetComponentsInChildren<Renderer>())
            {
                if (part.enabled && !(part is ParticleSystemRenderer) && !(part is LineRenderer))
                {
                    if (found)
                    {
                        bounds.Encapsulate(part.bounds);
                    }
                    else
                    {
                        bounds = part.bounds;
                        found = true;
                    }
                }
            }

            Transform point = new GameObject(notePointName).transform;
            point.SetParent(model, false);
            point.position = bounds.center;
            return point;
        }

        /// <summary>The floor under <paramref name="point"/>, or the point itself if there is none.</summary>
        private Vector3 FindFloor(Vector3 point)
        {
            // From well above, so a raised floor still counts.
            if (Physics.Raycast(point + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f,
                                groundMask, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }

            return point;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private void Reset()
        {
            delayAfter = 0f;
            Movement.type = MovementType.StandStill;
            groundMask = LayerMask.GetMask("Default");
            missile.hitMask = LayerMask.GetMask("Default", "Player");
        }
    }
}
