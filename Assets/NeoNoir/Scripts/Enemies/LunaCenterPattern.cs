using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Luna flies up into the sky, plunges point first into the middle of
    /// the arena, sends spiralling arms of earth cubes out, and calls in
    /// spheres of light that split on her blade into coloured shots lobbed
    /// at the player.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. Windup: she hangs still for <see cref="windup"/> seconds.
    ///   2. Liftoff: she shoots up <see cref="riseHeight"/> over
    ///      <see cref="riseTime"/> seconds, and a big red
    ///      <see cref="WarningCircle"/> appears on the landing spot, filling
    ///      in over <see cref="fillTime"/> seconds from liftoff.
    ///   3. At the top she moves, out of sight, to <see cref="dropHeight"/>
    ///      above the landing spot and waits for the circle to fill.
    ///   4. The fall: she drops, gathering speed, to land
    ///      <see cref="dropTime"/> seconds later with her point
    ///      <see cref="stabDepth"/> into the floor.
    ///   5. On landing: the circle goes, a <see cref="HammerImpact"/> bursts,
    ///      the camera shakes, and <see cref="armCount"/>
    ///      <see cref="EarthRipple"/>s run out evenly spaced round the
    ///      compass, each curling by the ripple's Curve Per Cube and opening
    ///      out by its Curve Falloff. The landing itself does no damage.
    ///   6. Light: for <see cref="duration"/> seconds she stays planted. For
    ///      the first <see cref="spawnWindow"/> of them, a
    ///      <see cref="LightOrb"/> appears every <see cref="spawnInterval"/>
    ///      seconds, the first straight away, out round the arena, and flies
    ///      in on her. Each one that touches her splits into
    ///      <see cref="splitCount"/> <see cref="PrismShot"/>s, their colours
    ///      spread evenly round the rainbow from a random start, each lobbed
    ///      at its own spot within <see cref="aimSpread"/> of where the
    ///      player stands at that moment.
    ///   7. Pulling out: she rises back to the height she hovered at before
    ///      the attack, over <see cref="pullOutTime"/> seconds, staying in
    ///      the middle, then waits <see cref="recovery"/> more. Orbs still
    ///      out carry on by themselves.
    ///
    /// The landing spot is the enemy's <see cref="Enemy.ArenaCentre"/>, which
    /// <see cref="BossIntro"/> sets; without one, she comes back down where
    /// she took off.
    ///
    /// How far her point reaches below her pivot is measured from her mesh
    /// renderers before liftoff, so the stab needs no setting up beyond
    /// <see cref="stabDepth"/>. Her colliders are off from liftoff to
    /// landing, so blades pass under her, and back on once she is planted,
    /// for the orbs to touch. Her Rigidbody, if she has one, is kinematic
    /// throughout and put back as it was at the end.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Luna/Luna Center", fileName = "LunaCenter")]
    public sealed class LunaCenterPattern : AttackPattern
    {
        private const int SpawnTries = 8;

        [Header("Windup")]
        [SerializeField]
        [Tooltip("Seconds she hangs still before flying up.")]
        [Min(0f)]
        private float windup = 0.5f;

        [Header("Jump")]
        [SerializeField]
        [Tooltip("How far up she flies before she is moved over the landing spot. High enough to leave the screen.")]
        [Min(0f)]
        private float riseHeight = 30f;

        [SerializeField]
        [Tooltip("Seconds the flight up takes. Fastest at the start, slowing towards the top.")]
        [Min(0.01f)]
        private float riseTime = 0.5f;

        [Header("Fall")]
        [SerializeField]
        [Tooltip("How far above the landing spot she starts falling from.")]
        [Min(0f)]
        private float dropHeight = 30f;

        [SerializeField]
        [Tooltip("Seconds the fall takes. It speeds up all the way down.")]
        [Min(0.01f)]
        private float dropTime = 0.5f;

        [SerializeField]
        [Tooltip("How far her point sinks into the floor on landing.")]
        [Min(0f)]
        private float stabDepth = 1f;

        [SerializeField]
        [Tooltip("What counts as floor when finding the landing height. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [Header("Indicator")]
        [SerializeField]
        [Tooltip("Seconds from liftoff for the circle to fill. The fall starts once it is full. Never shorter than the rise.")]
        [Min(0f)]
        private float fillTime = 2f;

        [SerializeField]
        [Tooltip("Radius of the circle on the floor.")]
        [Min(0.1f)]
        private float indicatorRadius = 6f;

        [SerializeField]
        [Tooltip("Thickness of the circle's outline.")]
        [Min(0f)]
        private float outlineWidth = 0.2f;

        [SerializeField]
        [Tooltip("Height above the floor the circle is drawn at.")]
        [Range(0f, 0.5f)]
        private float floorOffset = 0.03f;

        [SerializeField]
        [Tooltip("Material for the circle, e.g. NeoNoir/Flat. Its colour is replaced by the one below. Empty makes a NeoNoir/Flat one.")]
        private Material indicatorMaterial;

        [SerializeField]
        [ColorUsage(false, true)]
        [Tooltip("Colour of the circle. Keep it strongly saturated, or the spot colour threshold crushes it to black or white.")]
        private Color indicatorColour = new Color(1f, 0.04f, 0.08f, 1f);

        [Header("Landing impact")]
        [SerializeField]
        [Tooltip("Material for the landing burst, e.g. NeoNoir/Flat in the colour you want.")]
        private Material impactMaterial;

        [SerializeField]
        private HammerImpact.Settings impact = new HammerImpact.Settings
        {
            ringRadius = 7f,
            ringWidth = 1.2f,
            ringTime = 0.5f,
            debrisCount = 16,
            debrisSize = 0.6f,
            debrisUpSpeed = new Vector2(7f, 12f),
            debrisOutSpeed = new Vector2(3f, 8f),
            debrisTime = 0.9f,
        };

        [SerializeField]
        [Tooltip("How far the camera shakes on landing. 0 disables it.")]
        [Min(0f)]
        private float shakeStrength = 0.9f;

        [SerializeField]
        [Tooltip("Seconds the camera shake takes to die away.")]
        [Min(0.01f)]
        private float shakeDuration = 0.6f;

        [Header("Spiral")]
        [SerializeField]
        [Tooltip("Arms of cubes sent out from the landing, evenly spaced around the full circle.")]
        [Range(1, 32)]
        private int armCount = 6;

        [SerializeField]
        [Tooltip("Degrees the whole pattern is turned from world forward (+Z).")]
        private float angleOffset;

        [SerializeField]
        [Tooltip("How far from the landing spot each arm's first cube rises.")]
        [Min(0f)]
        private float armStartDistance = 2.5f;

        [SerializeField]
        private EarthRipple.Settings ripple = new EarthRipple.Settings
        {
            count = 16,
            curvePerCube = 20f,
            curveFalloff = 0.92f,
        };

        [Header("Light")]
        [SerializeField]
        [Tooltip("Seconds from landing she stays planted, calling in light.")]
        [Min(0f)]
        private float duration = 10f;

        [SerializeField]
        [Tooltip("Orbs appear only during this many seconds from landing.")]
        [Min(0f)]
        private float spawnWindow = 8f;

        [SerializeField]
        [Tooltip("Seconds between one orb and the next.")]
        [Min(0.05f)]
        private float spawnInterval = 1f;

        [SerializeField]
        [Tooltip("Nearest and furthest from her an orb appears, measured flat.")]
        private Vector2 spawnDistance = new Vector2(20f, 30f);

        [SerializeField]
        [Tooltip("Height above the floor the orbs appear and fly at. About chest height.")]
        private float spawnHeight = 1.5f;

        [SerializeField]
        [Tooltip("Walls that orbs must not appear behind. Leave the Enemy and Player layers out.")]
        private LayerMask obstacleMask = 1;

        [SerializeField]
        [Tooltip("How far short of a wall an orb is brought in.")]
        [Min(0f)]
        private float wallMargin = 1.5f;

        [SerializeField]
        [Tooltip("Art for each orb. Its colliders are removed. Empty draws a glowing ball.")]
        private GameObject orbPrefab;

        [SerializeField] private LightOrb.Settings orb = new LightOrb.Settings();

        [Header("Coloured shots")]
        [SerializeField]
        [Tooltip("Shots an orb splits into.")]
        [Range(1, 16)]
        private int splitCount = 3;

        [SerializeField]
        [Tooltip("Each shot lands somewhere within this far of where the player stood when the orb split, measured flat.")]
        [Min(0f)]
        private float aimSpread = 4f;

        [SerializeField]
        [Tooltip("Brightness of the shots' colours. Above 1 blooms.")]
        [Min(0f)]
        private float shotIntensity = 3f;

        [SerializeField] private PrismShot.Settings shot = new PrismShot.Settings();

        [Header("Pulling out")]
        [SerializeField]
        [Tooltip("Seconds she takes to pull out of the floor and rise back to her hover height.")]
        [Min(0.01f)]
        private float pullOutTime = 0.6f;

        [SerializeField]
        [Tooltip("Seconds after pulling out before the attack counts as over.")]
        [Min(0f)]
        private float recovery = 0.5f;

        public override IEnumerator Perform(Enemy enemy)
        {
            // Whatever the movement says, she goes nowhere but up and down.
            enemy.VelocityOverride = Vector3.zero;

            if (windup > 0f)
            {
                yield return new WaitForSeconds(windup);
            }

            Transform root = enemy.transform;
            Rigidbody body = enemy.GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            float tipReach = TipBelowPivot(enemy);
            List<Collider> switchedOff = new List<Collider>();

            foreach (Collider part in enemy.GetComponentsInChildren<Collider>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    switchedOff.Add(part);
                }
            }

            // Measured with her colliders off, so the ray never finds her.
            float hoverHeight = root.position.y - FindFloor(root.position).y;

            Vector3 floor = FindFloor(enemy.ArenaCentre ?? root.position);
            Vector3 landing = floor + Vector3.up * (tipReach - stabDepth);

            WarningCircle circle = WarningCircle.Create(floor + Vector3.up * floorOffset, indicatorRadius,
                                                        outlineWidth, indicatorMaterial, indicatorColour);
            circle.DestroyWhen(() => enemy == null || enemy.Health.IsDead);

            float fullAt = Mathf.Max(fillTime, riseTime);
            Vector3 takeoff = root.position;
            float clock = 0f;

            while (clock < riseTime)
            {
                yield return null;
                clock += Time.deltaTime;
                circle.Fill = fullAt > 0f ? clock / fullAt : 1f;

                // Launched hard, slowing as she climbs.
                float t = Mathf.Clamp01(clock / riseTime);
                root.position = takeoff + Vector3.up * (riseHeight * (1f - (1f - t) * (1f - t)));
            }

            root.position = landing + Vector3.up * dropHeight;

            while (clock < fullAt)
            {
                yield return null;
                clock += Time.deltaTime;
                circle.Fill = clock / fullAt;
            }

            circle.Fill = 1f;

            float falling = 0f;

            while (falling < dropTime)
            {
                yield return null;
                falling += Time.deltaTime;

                float t = Mathf.Clamp01(falling / dropTime);
                root.position = landing + Vector3.up * (dropHeight * (1f - t * t));
            }

            root.position = landing;

            foreach (Collider part in switchedOff)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }

            Destroy(circle.gameObject);
            Land(floor);

            float elapsed = 0f;
            float nextSpawn = 0f;

            while (elapsed < duration)
            {
                if (enemy.Health.IsDead)
                {
                    yield break;
                }

                while (elapsed >= nextSpawn && nextSpawn < spawnWindow)
                {
                    Spawn(enemy, floor);
                    nextSpawn += spawnInterval;
                }

                yield return null;
                elapsed += Time.deltaTime;
            }

            Vector3 hover = floor + Vector3.up * hoverHeight;
            float pulling = 0f;

            while (pulling < pullOutTime)
            {
                yield return null;
                pulling += Time.deltaTime;

                float t = Mathf.Clamp01(pulling / pullOutTime);
                root.position = Vector3.Lerp(landing, hover, t * t * (3f - 2f * t));
            }

            root.position = hover;

            if (body != null)
            {
                body.isKinematic = wasKinematic;

                if (!wasKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }
            }

            if (recovery > 0f)
            {
                yield return new WaitForSeconds(recovery);
            }
        }

        private void Land(Vector3 floor)
        {
            HammerImpact.Create(floor, impact, impactMaterial);

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }

            for (int i = 0; i < armCount; i++)
            {
                float angle = angleOffset + 360f * i / armCount;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward;

                EarthRipple.Create(floor + direction * armStartDistance, direction, ripple);
            }
        }

        private void Spawn(Enemy enemy, Vector3 floor)
        {
            Vector3 centre = floor + Vector3.up * spawnHeight;
            float near = Mathf.Min(spawnDistance.x, spawnDistance.y);
            float far = Mathf.Max(spawnDistance.x, spawnDistance.y);

            Vector3 best = centre;
            float bestReach = -1f;

            for (int i = 0; i < SpawnTries; i++)
            {
                Vector3 bearing = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
                float distance = Random.Range(near, far);

                // Brought in short of any wall on the way out.
                if (Physics.Raycast(centre, bearing, out RaycastHit hit, distance, obstacleMask,
                                    QueryTriggerInteraction.Ignore))
                {
                    distance = hit.distance - wallMargin;
                }

                if (distance >= near)
                {
                    best = centre + bearing * distance;
                    break;
                }

                // Every way blocked: the bearing with the most room will do.
                if (distance > bestReach)
                {
                    best = centre + bearing * Mathf.Max(0f, distance);
                    bestReach = distance;
                }
            }

            LightOrb.Spawn(orbPrefab, best, enemy, orb, point => Split(enemy, point));
        }

        private void Split(Enemy enemy, Vector3 origin)
        {
            if (enemy == null || enemy.Health.IsDead || !enemy.HasTarget)
            {
                return;
            }

            Vector3 aim = enemy.Target.position;
            float hue = Random.value;

            for (int i = 0; i < splitCount; i++)
            {
                Vector2 offset = Random.insideUnitCircle * aimSpread;
                Vector3 landing = FindFloor(aim + new Vector3(offset.x, 0f, offset.y));

                Color colour = Color.HSVToRGB(Mathf.Repeat(hue + (float)i / splitCount, 1f), 1f, 1f) * shotIntensity;
                colour.a = 1f;

                PrismShot.Launch(origin, landing, colour, shot);
            }
        }

        /// <summary>
        /// How far her point reaches below her pivot: the bottom of her mesh
        /// renderers. 0 if she has none.
        /// </summary>
        private static float TipBelowPivot(Enemy enemy)
        {
            float lowest = float.MaxValue;

            foreach (Renderer part in enemy.GetComponentsInChildren<Renderer>())
            {
                if (part.enabled && (part is MeshRenderer || part is SkinnedMeshRenderer))
                {
                    lowest = Mathf.Min(lowest, part.bounds.min.y);
                }
            }

            return lowest == float.MaxValue ? 0f : enemy.transform.position.y - lowest;
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

        private void Reset()
        {
            delayAfter = 2f;
            Movement.type = MovementType.StandStill;
            obstacleMask = LayerMask.GetMask("Default");
            groundMask = LayerMask.GetMask("Default");
        }
    }
}
