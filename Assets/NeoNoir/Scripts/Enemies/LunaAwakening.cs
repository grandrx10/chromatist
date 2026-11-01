using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Turns the Luna left hovering by the <see cref="EmperorAudience"/> into
    /// the Luna boss: swaps the blade for the boss in the same frame, in the
    /// same spot and pose, at the player's Luna's size, then bursts in a huge
    /// explosion that flings the player back to a set spot and destroys the
    /// objects listed, and drops her to the floor. From there the
    /// <see cref="LunaCaptives"/> takes over.
    /// </summary>
    /// <remarks>
    /// Goes on any object in the scene. The prefab's own scale is her final
    /// size; she arrives at 1 / <see cref="growth"/> of it, which should be
    /// the size of the player's Luna, and the <see cref="LunaCaptives"/>
    /// grows her the rest of the way. Her art should share that Luna's pivot
    /// and convention (tip down local -Y, flat facing local +Z), so the swap
    /// cannot be seen. She hovers, so give her no Rigidbody or a kinematic
    /// one. She cannot be hurt, or wake, until the captives are dealt with.
    ///
    /// The player's controls lock from the swap; the captives hand them back.
    ///
    /// The explosion: a <see cref="BlastSphere"/> round her and a
    /// <see cref="HammerImpact"/> ring on the floor under her. The player is
    /// thrown in an arc to <see cref="flingTarget"/>, landing there exactly;
    /// walls and props are not in the way of the throw. Each object in
    /// <see cref="destroyed"/> stops being solid at once, dissolves, and is
    /// removed. With the blast she drops to the floor, the lowest point of
    /// her model just touching it; where she lands is where she retreats to
    /// between the captives' fights.
    /// </remarks>
    public sealed class LunaAwakening : MonoBehaviour
    {
        [SerializeField] private EmperorAudience audience;

        [SerializeField]
        [Tooltip("The Luna boss prefab, spawned in place of the hovering blade.")]
        private Enemy bossPrefab;

        [SerializeField]
        [Tooltip("Takes over once she has landed.")]
        private LunaCaptives captives;

        [SerializeField]
        [Tooltip("How many times bigger the prefab is than the player's Luna. She arrives at the prefab's scale divided by this.")]
        [Min(0.01f)]
        private float growth = 2f;

        [Header("Explosion")]
        [SerializeField]
        [Tooltip("Seconds between taking Luna's place and the explosion.")]
        [Min(0f)]
        private float explosionDelay = 0.3f;

        [SerializeField]
        [Tooltip("Radius of the blast sphere.")]
        [Min(0.01f)]
        private float blastRadius = 14f;

        [SerializeField]
        private BlastSphere.Settings blast = new BlastSphere.Settings
        {
            growTime = 0.2f,
            fadeTime = 0.6f,
            colour = new Color(2.5f, 2.5f, 3f, 1f),
            outlineWidth = 0.4f,
            outlineColour = new Color(2f, 2f, 2f, 1f),
        };

        [SerializeField]
        [Tooltip("Material for the ring on the floor, e.g. NeoNoir/Flat in the colour you want. Empty draws no ring.")]
        private Material ringMaterial;

        [SerializeField]
        private HammerImpact.Settings ring = new HammerImpact.Settings
        {
            ringRadius = 16f,
            ringWidth = 2f,
            ringTime = 0.6f,
            debrisCount = 24,
            debrisSize = 0.7f,
            debrisUpSpeed = new Vector2(8f, 14f),
            debrisOutSpeed = new Vector2(6f, 14f),
            debrisTime = 1f,
        };

        [SerializeField]
        [Tooltip("How far the camera shakes in the explosion. 0 disables it.")]
        [Min(0f)]
        private float blastShakeStrength = 1.4f;

        [SerializeField]
        [Min(0.01f)]
        private float blastShakeDuration = 0.8f;

        [Header("Fling")]
        [SerializeField]
        [Tooltip("Where the player lands. Empty leaves the player where they are.")]
        private Transform flingTarget;

        [SerializeField]
        [Tooltip("Seconds the player is in the air.")]
        [Min(0.01f)]
        private float flingTime = 0.7f;

        [SerializeField]
        [Tooltip("How high the arc rises above the straight line between take-off and landing.")]
        [Min(0f)]
        private float flingHeight = 2.5f;

        [SerializeField]
        [Tooltip("Seconds after landing before the captives take over.")]
        [Min(0f)]
        private float landedPause = 0.5f;

        [Header("Grounding")]
        [SerializeField]
        [Tooltip("Seconds she takes to drop from the air to the floor, starting with the blast. She speeds up all the way down.")]
        [Min(0.01f)]
        private float dropTime = 0.4f;

        [SerializeField]
        [Tooltip("Raises (or, negative, lowers) where she ends up. At 0 the lowest point of her model just touches the floor.")]
        private float groundOffset;

        [SerializeField]
        [Tooltip("Her arena, for attacks that place things in it -- hers and the captives'. Optional.")]
        private ArenaBounds arena;

        [Header("Destroyed")]
        [SerializeField]
        [Tooltip("Objects the explosion destroys: each stops being solid, dissolves, and is removed.")]
        private List<GameObject> destroyed = new List<GameObject>();

        [SerializeField]
        [Tooltip("Seconds after the blast before they start dissolving.")]
        [Min(0f)]
        private float destroyDelay = 0.1f;

        [SerializeField]
        [Tooltip("Seconds they take to dissolve.")]
        [Min(0.01f)]
        private float destroyFadeTime = 0.6f;

        [Header("Burned")]
        [SerializeField]
        [Tooltip("Things flat on the floor the explosion sets alight, such as the carpet: each burns away outward from under her, and is removed once fully burnt.")]
        private List<GameObject> burned = new List<GameObject>();

        [SerializeField] private BurnAway.Settings burn = new BurnAway.Settings();

        private float floor;

        /// <summary>The Luna boss, once spawned.</summary>
        public Enemy Boss { get; private set; }

        private void OnEnable()
        {
            if (audience != null)
            {
                audience.LunaFightStarted += OnLunaFightStarted;
            }
        }

        private void OnDisable()
        {
            if (audience != null)
            {
                audience.LunaFightStarted -= OnLunaFightStarted;
            }
        }

        private void OnLunaFightStarted(EmperorAudience started)
        {
            if (bossPrefab == null || captives == null)
            {
                Debug.LogWarning($"[LunaAwakening] {name} needs a boss prefab and a Luna Captives.", this);
                return;
            }

            Blade blade = started.Luna;
            Vector3 position = blade != null ? blade.transform.position : started.transform.position;
            Quaternion rotation = blade != null ? blade.transform.rotation : bossPrefab.transform.rotation;

            Boss = Instantiate(bossPrefab, position, rotation);
            Boss.name = bossPrefab.name;
            Boss.StayDormant = true;
            Boss.Health.Invulnerable = true;

            if (arena != null)
            {
                Boss.Arena = arena;
            }

            Vector3 full = Boss.transform.localScale;
            Boss.transform.localScale = full / growth;

            if (blade != null)
            {
                Destroy(blade.gameObject);
            }

            // The Emperor's, emptied.
            BossHealthBar.Hide();

            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);
            PlayerInputReader input = player != null ? player.GetComponent<PlayerInputReader>() : null;

            if (input != null)
            {
                input.Locked = true;
            }

            StartCoroutine(Awaken(player, position, full));
        }

        /// <summary>
        /// The dev shortcut to Luna's own fight, from anywhere in the level:
        /// the audience is skipped and the Emperor killed, the player gets
        /// the default loadout back without Luna and lands at the fling
        /// target, and Luna stands already landed and full size where the
        /// explosion would have left her, the destroyed and burned objects
        /// gone. Then her fight starts as it would once the captives are
        /// dealt with. False if she is already spawned or something needed is
        /// missing.
        /// </summary>
        public bool SkipToFight()
        {
            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (Boss != null || bossPrefab == null || captives == null || player == null)
            {
                return false;
            }

            Vector3 position = audience != null ? audience.Skip() : transform.position;

            BladeArsenal arsenal = player.GetComponent<BladeArsenal>();

            if (arsenal != null)
            {
                arsenal.Equip();

                // She is the boss now, so not in the player's hands too.
                Blade inHand = audience != null ? arsenal.Surrender(audience.LunaDefinition) : null;

                if (inHand != null)
                {
                    Destroy(inHand.gameObject);
                }
            }

            PlayerInputReader input = player.GetComponent<PlayerInputReader>();

            if (input != null)
            {
                input.Locked = false;
            }

            if (flingTarget != null)
            {
                player.CancelDash();
                player.Teleport(flingTarget.position);
            }

            Vector3 toward = Vector3.ProjectOnPlane(player.transform.position - position, Vector3.up);
            Quaternion facing = toward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toward, Vector3.up) : bossPrefab.transform.rotation;

            Boss = Instantiate(bossPrefab, position, facing);
            Boss.name = bossPrefab.name;
            Boss.StayDormant = true;
            Boss.Health.Invulnerable = true;

            if (arena != null)
            {
                Boss.Arena = arena;
            }

            BossHealthBar.Hide();

            List<GameObject> gone = new List<GameObject>(destroyed);
            gone.AddRange(burned);

            foreach (GameObject thing in gone)
            {
                if (thing == null)
                {
                    continue;
                }

                // Not solid this frame, before Destroy takes it, so the floor is found under it.
                foreach (Collider solid in thing.GetComponentsInChildren<Collider>())
                {
                    solid.enabled = false;
                }

                Destroy(thing);
            }

            // Straight onto the floor, as the drop would leave her.
            floor = FloorUnder(Boss, position, player.transform.position.y);
            Boss.transform.position = position + Vector3.up * (floor - LowestPoint(Boss, position.y) + groundOffset);

            captives.BeginLunaFight(Boss, player, floor);
            return true;
        }

        private IEnumerator Awaken(PlayerMotor player, Vector3 position, Vector3 full)
        {
            if (explosionDelay > 0f)
            {
                yield return new WaitForSeconds(explosionDelay);
            }

            yield return Explode(player, position);

            captives.Begin(Boss, player, floor, full);
        }

        private IEnumerator Explode(PlayerMotor player, Vector3 centre)
        {
            BlastSphere.Create(centre, blastRadius, blast);

            if (ringMaterial != null)
            {
                // On the floor the player stands on, under her.
                float ground = player != null ? player.transform.position.y : centre.y;
                HammerImpact.Create(new Vector3(centre.x, ground, centre.z), ring, ringMaterial);
            }

            if (blastShakeStrength > 0f)
            {
                TopDownCamera.Shake(blastShakeStrength, blastShakeDuration);
            }

            foreach (GameObject thing in destroyed)
            {
                if (thing == null)
                {
                    continue;
                }

                foreach (Collider solid in thing.GetComponentsInChildren<Collider>())
                {
                    solid.enabled = false;
                }

                thing.AddComponent<RagdollFade>().Begin(destroyDelay, destroyFadeTime);
            }

            foreach (GameObject thing in burned)
            {
                BurnAway.Begin(thing, centre, burn);
            }

            float fallbackFloor = player != null ? player.transform.position.y : centre.y;
            Coroutine drop = StartCoroutine(Drop(Boss, fallbackFloor));

            if (player != null && flingTarget != null)
            {
                yield return Fling(player, flingTarget.position);
            }

            yield return drop;

            if (landedPause > 0f)
            {
                yield return new WaitForSeconds(landedPause);
            }
        }

        /// <summary>
        /// Drops her straight down until the lowest point of her model meets
        /// the floor under her, or <paramref name="fallbackFloor"/> if no
        /// floor is found, and remembers that floor's height.
        /// </summary>
        private IEnumerator Drop(Enemy boss, float fallbackFloor)
        {
            Transform body = boss.transform;
            Vector3 start = body.position;

            floor = FloorUnder(boss, start, fallbackFloor);
            float lowest = LowestPoint(boss, start.y);
            Vector3 end = start + Vector3.up * (floor - lowest + groundOffset);

            for (float clock = 0f; clock < dropTime; clock += Time.deltaTime)
            {
                float t = clock / dropTime;
                body.position = Vector3.LerpUnclamped(start, end, t * t);
                yield return null;
            }

            body.position = end;
        }

        /// <summary>The height of the first solid thing below <paramref name="from"/> that is not her or the player.</summary>
        private static float FloorUnder(Enemy boss, Vector3 from, float fallback)
        {
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
            float best = fallback;
            float nearest = float.MaxValue;

            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.GetComponentInParent<Enemy>() == boss || hit.collider.GetComponentInParent<PlayerMotor>() != null)
                {
                    continue;
                }

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    best = hit.point.y;
                }
            }

            return best;
        }

        /// <summary>The bottom of her model's bounds, or <paramref name="fallback"/> if she has no renderers.</summary>
        private static float LowestPoint(Enemy boss, float fallback)
        {
            float lowest = float.MaxValue;

            foreach (Renderer part in boss.GetComponentsInChildren<Renderer>())
            {
                lowest = Mathf.Min(lowest, part.bounds.min.y);
            }

            return lowest < float.MaxValue ? lowest : fallback;
        }

        /// <summary>
        /// Throws the player along an arc to <paramref name="landing"/>. The
        /// motor is switched off for the flight, so neither it nor physics
        /// has any say, and the body is put down exactly there at the end.
        /// </summary>
        private IEnumerator Fling(PlayerMotor player, Vector3 landing)
        {
            Rigidbody body = player.GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;

            player.CancelDash();
            player.enabled = false;

            if (body != null)
            {
                body.isKinematic = true;
            }

            Vector3 takeOff = player.transform.position;

            for (float clock = 0f; clock < flingTime; clock += Time.deltaTime)
            {
                float t = clock / flingTime;
                Vector3 point = Vector3.Lerp(takeOff, landing, t) + Vector3.up * (4f * flingHeight * t * (1f - t));

                if (body != null)
                {
                    body.position = point;
                }

                player.transform.position = point;
                yield return null;
            }

            if (body != null)
            {
                body.isKinematic = wasKinematic;
            }

            player.enabled = true;
            player.Teleport(landing);
        }
    }
}
