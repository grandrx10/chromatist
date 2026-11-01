using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A sword stuck in a rock, waiting for a boss to wrench it free and
    /// throw it: it lights up in a colour of its own, jerks its way out in a
    /// spray of dust, hangs a moment turning on the player, then homes in on
    /// them, sticks in whatever it hits, and explodes.
    /// </summary>
    /// <remarks>
    /// Goes on the root of the BladeInStone prefab. The sword is the child
    /// called "Blade" and the stone the child called "Rock", unless set in
    /// the Inspector. The blade is built hilt up, tip down its local -Y.
    ///
    /// <see cref="StoneSwordPattern"/> picks swords that are
    /// <see cref="IsReady"/> and calls <see cref="BeginPull"/> on each, then
    /// <see cref="Launch"/>. From then on the sword runs by itself:
    ///   1. Over <see cref="Settings.tintTime"/> the blade turns from black to
    ///      its colour.
    ///   2. <see cref="Settings.jerkCount"/> times it jerks up out of the
    ///      stone by <see cref="Settings.jerkHeight"/>, straining and
    ///      shaking between tugs, dust bursting from the stone at each.
    ///   3. A last yank tears it free, up to <see cref="Settings.hoverHeight"/>
    ///      above where it sat, in a bigger burst of dust, and the rock
    ///      shrinks and dissolves away.
    ///   4. It hangs there, bobbing, its tip turning toward the player, until
    ///      launched.
    ///   5. It flies tip first, speeding up, and turns after the player for
    ///      <see cref="Settings.trackTime"/>, no faster than
    ///      <see cref="Settings.turnRate"/>, as a <see cref="HomingMissile"/>
    ///      does. After that it stops chasing and tips over into a dive, so a
    ///      sword that misses always comes down into the floor.
    ///   6. Whatever it hits, it sticks in. The player it hits takes
    ///      <see cref="Settings.stabDamage"/> and carries the sword round with
    ///      them. A warning circle <see cref="Settings.blastRadius"/> across
    ///      fills under it as the blade blinks faster and faster, and after
    ///      <see cref="Settings.fuseTime"/> it explodes, hurting the player
    ///      within the radius for <see cref="Settings.blastDamage"/>.
    ///   7. Then the sword is gone for good: the whole object, what is left
    ///      of its stone included, is destroyed. Stones never grow back.
    ///
    /// Caught by a <see cref="ParryBarrier"/> in flight, the sword bursts
    /// harmlessly. If the boss that pulled it dies before it is launched, it
    /// drops straight down, sticks in the floor and goes off as usual. The
    /// trail, warning, explosion and sparks all take the sword's colour.
    ///
    /// A sword can also be grown out of the floor mid-fight, rock and all,
    /// with <see cref="Spawn"/>, as <see cref="MaceLeapPattern"/> does where
    /// each of its blocks lands. It cannot be pulled until it has finished
    /// growing; then it is like any other. Spawned swords are copies of a
    /// template made once per prefab -- the prefab with its meshes built and
    /// every script but its Stone Sword stripped -- so a ProBuilder model is
    /// not rebuilt for each one.
    ///
    /// The rock and blade are tinted and dissolved through property blocks,
    /// so their materials need NeoNoir/Flat's _BaseColor and _Fade. The
    /// prefab must not be marked Static, or the blade cannot move.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StoneSword : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Pull")]
            [Tooltip("Seconds the blade takes to turn from black to its colour, before the first tug.")]
            [Min(0f)] public float tintTime = 0.2f;

            [Tooltip("Tugs before the last yank tears it free.")]
            [Range(0, 8)] public int jerkCount = 3;

            [Tooltip("How far each tug draws the blade out.")]
            [Min(0f)] public float jerkHeight = 0.2f;

            [Tooltip("Seconds each tug takes.")]
            [Min(0.01f)] public float jerkTime = 0.06f;

            [Tooltip("Seconds the blade strains between tugs.")]
            [Min(0f)] public float jerkPause = 0.22f;

            [Tooltip("How far the blade shakes as it strains.")]
            [Min(0f)] public float strainShake = 0.04f;

            [Tooltip("Degrees the blade wobbles as it strains.")]
            [Min(0f)] public float strainWobble = 3f;

            [Tooltip("Seconds of the last yank, up to the hover.")]
            [Min(0.01f)] public float yankTime = 0.25f;

            [Tooltip("Height above where it sat in the stone that the blade hangs at once free.")]
            [Min(0f)] public float hoverHeight = 2.5f;

            [Tooltip("How far the blade bobs up and down while it hangs.")]
            [Min(0f)] public float hoverBob = 0.1f;

            [Tooltip("Degrees per second the hanging blade turns its tip toward the player.")]
            [Min(0f)] public float aimTurnSpeed = 360f;

            [Tooltip("How far the camera jolts as the blade tears free. 0 for none.")]
            [Min(0f)] public float yankShake = 0.1f;

            [Header("Rock")]
            [Tooltip("Seconds the rock takes to shrink and dissolve once the blade is free.")]
            [Min(0.01f)] public float rockFadeTime = 0.45f;

            [Header("Dust")]
            [Tooltip("Puffs at each tug.")]
            [Min(0)] public int dustPerJerk = 6;

            [Tooltip("Puffs as the blade tears free.")]
            [Min(0)] public int dustOnYank = 16;

            [Tooltip("How far round the blade the puffs start.")]
            [Min(0f)] public float dustRadius = 0.4f;

            [Tooltip("Speed the puffs fly outward.")]
            [Min(0f)] public float dustSpeed = 2.5f;

            public DustTrail.Settings dust = new DustTrail.Settings
            {
                size = 0.3f,
                lifetime = 0.6f,
                rise = 1.2f,
                colour = new Color(0.8f, 0.8f, 0.8f),
            };

            [Header("Flight")]
            [Tooltip("Speed the sword sets off at.")]
            [Min(0f)] public float startSpeed = 4f;

            [Tooltip("Top speed, reached after the Acceleration Time.")]
            [Min(0.1f)] public float speed = 16f;

            [Tooltip("Seconds from the start speed to top speed.")]
            [Min(0f)] public float accelerationTime = 0.35f;

            [Tooltip("Degrees per second it turns after the player. Lower is easier to dodge.")]
            [Min(0f)] public float turnRate = 140f;

            [Tooltip("Seconds it chases the player before giving up and diving into the floor.")]
            [Min(0f)] public float trackTime = 1.4f;

            [Tooltip("Degrees per second it tips over into its dive once it stops chasing.")]
            [Min(0f)] public float diveTurnRate = 180f;

            [Tooltip("Seconds of flight before it goes off in the air by itself.")]
            [Min(0.1f)] public float lifetime = 5f;

            [Tooltip("Size of the tip for hits and parries.")]
            [Min(0.01f)] public float radius = 0.25f;

            [Tooltip("What the sword sticks in. Leave the Enemy layer out. Trigger colliders are always ignored.")]
            public LayerMask hitMask = ~0;

            [Tooltip("How deep the tip sinks into the floor or a wall.")]
            [Min(0f)] public float embedDepth = 0.6f;

            [Tooltip("How deep the tip sinks into the player.")]
            [Min(0f)] public float stickDepth = 0.25f;

            [Tooltip("Damage to the player the sword stabs.")]
            [Min(0f)] public float stabDamage = 10f;

            [Tooltip("Seconds of travel the trail covers.")]
            [Min(0.01f)] public float trailTime = 0.3f;

            [Tooltip("Width of the trail at the sword. It narrows to nothing at the tail.")]
            [Min(0.001f)] public float trailWidth = 0.35f;

            [Header("Explosion")]
            [Tooltip("Seconds from sticking in to exploding.")]
            [Min(0f)] public float fuseTime = 2f;

            [Tooltip("Reach of the explosion.")]
            [Min(0.1f)] public float blastRadius = 5f;

            [Tooltip("Damage to the player within reach.")]
            [Min(0f)] public float blastDamage = 15f;

            [Tooltip("Blinks a second as the fuse starts, and as it ends.")]
            public Vector2 blinkRate = new Vector2(2f, 12f);

            [Tooltip("How much brighter the blade flashes on each blink.")]
            [Min(1f)] public float blinkBoost = 2f;

            [Tooltip("Width of the warning circle's outline.")]
            [Min(0f)] public float warningOutline = 0.12f;

            [Tooltip("Layers the warning circle is laid on.")]
            public LayerMask floorMask = 1;

            [Tooltip("How far the camera jolts on the explosion. 0 for none.")]
            [Min(0f)] public float blastShake = 0.4f;

            [Min(0.01f)] public float shakeDuration = 0.25f;

            [Tooltip("The ball of the explosion. Its colour is the sword's.")]
            public BlastSphere.Settings sphere = new BlastSphere.Settings();

            [Tooltip("Sparks of the explosion. Their colour is the sword's.")]
            public EnergyBurst.Settings burst = new EnergyBurst.Settings
            {
                flashSize = 2f,
                sparkCount = 18,
                sparkSize = 0.35f,
                sparkSpeed = new Vector2(6f, 14f),
                sparkLife = 0.45f,
            };

            [Tooltip("Sparks when a parry catches the sword. Their colour is the sword's.")]
            public EnergyBurst.Settings shatter = new EnergyBurst.Settings
            {
                flashSize = 0.8f,
                flashTime = 0.12f,
                sparkCount = 8,
                sparkSize = 0.2f,
                sparkSpeed = new Vector2(3f, 7f),
                sparkLife = 0.3f,
            };
        }

        private enum State
        {
            Growing,
            InStone,
            Pulling,
            Hovering,
            Flying,
            Stuck,
            Spent,
        }

        private static readonly List<StoneSword> all = new List<StoneSword>();
        private static readonly Dictionary<GameObject, GameObject> Templates = new Dictionary<GameObject, GameObject>();
        private static readonly HashSet<GameObject> NotSwords = new HashSet<GameObject>();

        // How far each template's pivot is above the bottom of it, at full size.
        private static readonly Dictionary<GameObject, float> PivotAboveBottom = new Dictionary<GameObject, float>();
        private static readonly RaycastHit[] hits = new RaycastHit[16];
        private static readonly Collider[] touching = new Collider[32];
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        // Heights the warning circles take turns at, so no two share a plane.
        private const int WarningLayers = 8;
        private const float WarningLayerStep = 0.012f;
        private static int nextWarningLayer;

        [SerializeField]
        [Tooltip("The sword, built hilt up and tip down its local -Y. Empty finds the child called Blade.")]
        private Transform blade;

        [SerializeField]
        [Tooltip("The stone. Empty finds the child called Rock.")]
        private Transform rock;

        private Renderer[] bladeRenderers;
        private Renderer[] rockRenderers;
        private Collider[] rockColliders;
        private MaterialPropertyBlock block;

        private Vector3 rockHomeScale;

        private State state = State.InStone;
        private Settings settings;
        private Enemy owner;
        private Transform ownerRoot;
        private Color colour;
        private float tipReach = 1f;

        private bool launched;
        private Transform target;
        private Collider targetBody;
        private TrailRenderer trail;

        /// <summary>Every sword in a stone in the loaded scenes, ready or not.</summary>
        public static IReadOnlyList<StoneSword> All => all;

        /// <summary>Sitting in its stone, free to be pulled.</summary>
        public bool IsReady => state == State.InStone && blade != null && isActiveAndEnabled;

        /// <summary>Where the sword stands, for picking which to pull.</summary>
        public Vector3 Position => blade != null ? blade.position : transform.position;

        /// <summary>Still being drawn out of the stone, not yet hanging free.</summary>
        public bool IsPulling => state == State.Pulling;

        private void Awake()
        {
            if (blade == null)
            {
                blade = transform.Find("Blade");
            }

            if (rock == null)
            {
                rock = transform.Find("Rock");
            }

            if (blade == null)
            {
                Debug.LogWarning($"[StoneSword] {name} has no child called Blade.", this);
                return;
            }

            bladeRenderers = blade.GetComponentsInChildren<Renderer>(true);
            rockRenderers = rock != null ? rock.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            rockColliders = rock != null ? rock.GetComponentsInChildren<Collider>(true) : new Collider[0];
            block = new MaterialPropertyBlock();

            rockHomeScale = rock != null ? rock.localScale : Vector3.one;
        }

        private void OnEnable()
        {
            all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }

        /// <summary>
        /// Grows a sword in a stone of <paramref name="prefab"/> out of the
        /// floor at <paramref name="floor"/>, turned <paramref name="yaw"/>
        /// degrees about the vertical, the bottom of it <paramref name="sink"/>
        /// under the floor, wherever the prefab's pivot is: it swells up from
        /// nothing to full size over <paramref name="growTime"/> seconds, and
        /// can be pulled once grown. Null if the prefab has no Stone Sword.
        /// </summary>
        public static StoneSword Spawn(GameObject prefab, Vector3 floor, float yaw, float growTime, float sink)
        {
            GameObject template = TemplateOf(prefab);

            if (template == null)
            {
                return null;
            }

            GameObject made = Instantiate(template, floor, Quaternion.Euler(0f, yaw, 0f) * template.transform.rotation);
            made.name = prefab.name;

            StoneSword sword = made.GetComponent<StoneSword>();
            Vector3 bottom = floor - Vector3.up * sink;
            sword.StartCoroutine(sword.Grow(template.transform.localScale, bottom, PivotAboveBottom[template], growTime));
            return sword;
        }

        /// <summary>Makes the template <see cref="Spawn"/> copies, if it is not made yet, so the first does not build it mid-fight.</summary>
        public static void Prepare(GameObject prefab)
        {
            TemplateOf(prefab);
        }

        /// <summary>
        /// The template swords of <paramref name="prefab"/> are spawned from,
        /// made the first time it is wanted: meshes built, every script but
        /// the Stone Sword stripped, parked out of sight. Null with no prefab,
        /// or none with a Stone Sword on it.
        /// </summary>
        private static GameObject TemplateOf(GameObject prefab)
        {
            if (prefab == null || NotSwords.Contains(prefab))
            {
                return null;
            }

            // A scene change destroys the template with it.
            if (Templates.TryGetValue(prefab, out GameObject template) && template != null)
            {
                return template;
            }

            // Made active once, so a ProBuilder model builds its meshes.
            template = Instantiate(prefab);
            template.name = prefab.name + " (template)";
            StoneSword sword = template.GetComponent<StoneSword>();

            if (sword == null)
            {
                Debug.LogWarning($"[StoneSword] {prefab.name} has no Stone Sword on its root, so none can be spawned from it.", prefab);
                Destroy(template);
                NotSwords.Add(prefab);
                return null;
            }

            // Last added first: a script another needs goes after it.
            MonoBehaviour[] scripts = template.GetComponentsInChildren<MonoBehaviour>(true);

            for (int i = scripts.Length - 1; i >= 0; i--)
            {
                if (scripts[i] != sword)
                {
                    DestroyImmediate(scripts[i]);
                }
            }

            float lowest = template.transform.position.y;

            foreach (Renderer part in template.GetComponentsInChildren<Renderer>())
            {
                lowest = Mathf.Min(lowest, part.bounds.min.y);
            }

            PivotAboveBottom[template] = template.transform.position.y - lowest;
            ArtTemplate.Hold(template);
            Templates[prefab] = template;
            return template;
        }

        /// <summary>
        /// Swells from nothing to <paramref name="size"/>, its bottom held at
        /// <paramref name="bottom"/> so it grows up out of the floor, then
        /// waits in its stone. <paramref name="pivotAboveBottom"/> is how far
        /// the pivot is above the bottom at full size.
        /// </summary>
        private IEnumerator Grow(Vector3 size, Vector3 bottom, float pivotAboveBottom, float seconds)
        {
            state = State.Growing;

            for (float elapsed = 0f; elapsed < seconds; elapsed += Time.deltaTime)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / seconds);
                transform.localScale = size * t;
                transform.position = bottom + Vector3.up * (pivotAboveBottom * t);
                yield return null;
            }

            transform.localScale = size;
            transform.position = bottom + Vector3.up * pivotAboveBottom;
            state = State.InStone;
        }

        /// <summary>
        /// Starts drawing the sword out of its stone for
        /// <paramref name="enemy"/>, in <paramref name="swordColour"/>, after
        /// <paramref name="delay"/> seconds. It hangs free until
        /// <see cref="Launch"/>.
        /// </summary>
        public void BeginPull(Enemy enemy, Settings swordSettings, Color swordColour, float delay)
        {
            if (!IsReady)
            {
                return;
            }

            state = State.Pulling;
            owner = enemy;
            ownerRoot = enemy != null ? enemy.transform : null;
            settings = swordSettings;
            colour = swordColour;
            launched = false;
            target = null;
            StartCoroutine(Run(delay));
        }

        /// <summary>Throws the hanging sword at <paramref name="homeOn"/>. Null drops it straight down.</summary>
        public void Launch(Transform homeOn)
        {
            target = homeOn;
            targetBody = homeOn != null ? homeOn.GetComponent<Collider>() : null;
            launched = true;
        }

        private IEnumerator Run(float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            MeasureTip();

            Vector3 home = blade.position;
            Quaternion homeRotation = blade.rotation;
            Vector3 axis = blade.up;
            Vector3 dustPoint = DustPoint(home);
            DustTrail.Cloud cloud = DustTrail.Cloud.Create("StoneSwordDust");

            for (float elapsed = 0f; elapsed < settings.tintTime; elapsed += Time.deltaTime)
            {
                Paint(bladeRenderers, Color.Lerp(Color.black, colour, elapsed / settings.tintTime));
                yield return null;
            }

            Paint(bladeRenderers, colour);
            float lift = 0f;

            for (int jerk = 0; jerk < settings.jerkCount; jerk++)
            {
                Puff(cloud, dustPoint, settings.dustPerJerk, 1f);
                float from = lift;
                float to = lift + settings.jerkHeight;

                for (float elapsed = 0f; elapsed < settings.jerkTime; elapsed += Time.deltaTime)
                {
                    float t = 1f - (1f - elapsed / settings.jerkTime) * (1f - elapsed / settings.jerkTime);
                    lift = Mathf.Lerp(from, to, t);
                    blade.SetPositionAndRotation(home + axis * lift, homeRotation);
                    yield return null;
                }

                lift = to;

                for (float elapsed = 0f; elapsed < settings.jerkPause; elapsed += Time.deltaTime)
                {
                    Vector3 shake = UnityEngine.Random.insideUnitSphere * settings.strainShake;
                    Quaternion wobble = Quaternion.Euler(UnityEngine.Random.insideUnitSphere * settings.strainWobble);
                    blade.SetPositionAndRotation(home + axis * lift + shake, homeRotation * wobble);
                    yield return null;
                }
            }

            // Torn free.
            Puff(cloud, dustPoint, settings.dustOnYank, 1.6f);
            cloud.Release();
            TopDownCamera.Shake(settings.yankShake, 0.15f);
            StartCoroutine(FadeRock());

            float yankFrom = lift;

            for (float elapsed = 0f; elapsed < settings.yankTime; elapsed += Time.deltaTime)
            {
                float t = 1f - Mathf.Pow(1f - elapsed / settings.yankTime, 3f);
                blade.SetPositionAndRotation(home + axis * Mathf.Lerp(yankFrom, settings.hoverHeight, t), homeRotation);
                yield return null;
            }

            // Hangs, turning on the player, until thrown.
            state = State.Hovering;
            Vector3 hover = home + axis * settings.hoverHeight;
            Quaternion rotation = homeRotation;
            float age = 0f;

            while (!launched)
            {
                if (owner == null || owner.Health.IsDead)
                {
                    Launch(null);
                    break;
                }

                age += Time.deltaTime;

                if (owner.HasTarget)
                {
                    Vector3 aim = AimPoint(owner.Target, owner.Target.GetComponent<Collider>()) - blade.position;

                    if (aim.sqrMagnitude > 0.0001f)
                    {
                        rotation = Quaternion.RotateTowards(rotation, TipToward(aim),
                                                            settings.aimTurnSpeed * Time.deltaTime);
                    }
                }

                blade.SetPositionAndRotation(hover + Vector3.up * (Mathf.Sin(age * 4f) * settings.hoverBob), rotation);
                yield return null;
            }

            yield return Fly();
        }

        private IEnumerator Fly()
        {
            state = State.Flying;
            BuildTrail();

            Vector3 direction = target != null ? -blade.up : Vector3.down;
            float speed = settings.startSpeed;
            float acceleration = settings.accelerationTime > 0f
                ? Mathf.Abs(settings.speed - settings.startSpeed) / settings.accelerationTime
                : float.PositiveInfinity;
            float age = 0f;

            while (true)
            {
                float deltaTime = Time.deltaTime;
                age += deltaTime;

                if (age >= settings.lifetime)
                {
                    Explode(Tip());
                    yield break;
                }

                if (target != null && age < settings.trackTime)
                {
                    Vector3 aim = AimPoint(target, targetBody) - Tip();

                    if (aim.sqrMagnitude > 0.0001f)
                    {
                        direction = Vector3.RotateTowards(direction, aim.normalized,
                                                          settings.turnRate * Mathf.Deg2Rad * deltaTime, 0f);
                    }
                }
                else
                {
                    direction = Vector3.RotateTowards(direction, Vector3.down,
                                                      settings.diveTurnRate * Mathf.Deg2Rad * deltaTime, 0f);
                }

                speed = Mathf.MoveTowards(speed, settings.speed, acceleration * deltaTime);

                Vector3 from = Tip();
                Vector3 step = direction * (speed * deltaTime);

                if (ParryBarrier.TryCatch(from, from + step, settings.radius, direction * speed))
                {
                    EnergyBurst.Create(from, Recolour(settings.shatter));
                    Spend();
                    yield break;
                }

                if (Cast(from, from + step, out RaycastHit hit))
                {
                    Vector3 contact = from + direction * hit.distance;
                    PlayerHealth player = hit.collider.GetComponentInParent<PlayerHealth>();
                    float depth = player != null ? settings.stickDepth : settings.embedDepth;
                    PlaceTip(contact + direction * depth, direction);

                    if (player != null)
                    {
                        player.TakeDamage(settings.stabDamage, from);
                    }

                    yield return Fuse(player != null ? player.transform : null);
                    yield break;
                }

                blade.SetPositionAndRotation(blade.position + step, TipToward(direction));
                yield return null;
            }
        }

        /// <summary>Stuck in, counting down, riding along on <paramref name="stuckIn"/> if given.</summary>
        private IEnumerator Fuse(Transform stuckIn)
        {
            state = State.Stuck;
            DropTrail();

            bool riding = stuckIn != null;
            Vector3 localPosition = riding ? stuckIn.InverseTransformPoint(blade.position) : Vector3.zero;
            Quaternion localRotation = riding ? Quaternion.Inverse(stuckIn.rotation) * blade.rotation : Quaternion.identity;

            // Each warning on a height of its own, so where two overlap the
            // newer is drawn cleanly over the older instead of the two
            // flickering together in the same plane.
            Vector3 lift = Vector3.up * (WarningLayerStep * (nextWarningLayer++ % WarningLayers));
            WarningCircle warning = WarningCircle.Create(Floor(Tip()) + lift, settings.blastRadius,
                                                         settings.warningOutline, null, colour);
            float blink = 0f;

            for (float elapsed = 0f; elapsed < settings.fuseTime; elapsed += Time.deltaTime)
            {
                if (riding && stuckIn != null)
                {
                    blade.SetPositionAndRotation(stuckIn.TransformPoint(localPosition), stuckIn.rotation * localRotation);
                }

                float fused = elapsed / settings.fuseTime;
                blink += Mathf.Lerp(settings.blinkRate.x, settings.blinkRate.y, fused) * Time.deltaTime;
                Paint(bladeRenderers, blink % 1f < 0.5f ? colour : colour * settings.blinkBoost);

                warning.transform.position = Floor(Tip()) + lift;
                warning.Fill = fused;
                yield return null;
            }

            Destroy(warning.gameObject);

            if (riding && stuckIn != null)
            {
                blade.SetPositionAndRotation(stuckIn.TransformPoint(localPosition), stuckIn.rotation * localRotation);
            }

            Explode(Tip());
        }

        private void Explode(Vector3 point)
        {
            BlastSphere.Settings ball = settings.sphere;
            BlastSphere.Create(point, settings.blastRadius, new BlastSphere.Settings
            {
                growTime = ball.growTime,
                fadeTime = ball.fadeTime,
                colour = colour,
                outlineWidth = ball.outlineWidth,
                outlineColour = ball.outlineColour,
                material = ball.material,
            });
            EnergyBurst.Create(point, Recolour(settings.burst));
            TopDownCamera.Shake(settings.blastShake, settings.shakeDuration);

            int count = Physics.OverlapSphereNonAlloc(point, settings.blastRadius, touching, ~0,
                                                      QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerHealth player = touching[i].GetComponentInParent<PlayerHealth>();

                if (player != null)
                {
                    player.TakeDamage(settings.blastDamage, point);
                    break;
                }
            }

            Spend();
        }

        /// <summary>Gone from the world for good: the whole sword, stone and all, is destroyed.</summary>
        private void Spend()
        {
            state = State.Spent;
            DropTrail();
            Destroy(gameObject);
        }

        private IEnumerator FadeRock()
        {
            foreach (Collider part in rockColliders)
            {
                part.enabled = false;
            }

            if (rock == null)
            {
                yield break;
            }

            for (float elapsed = 0f; elapsed < settings.rockFadeTime; elapsed += Time.deltaTime)
            {
                float t = elapsed / settings.rockFadeTime;
                rock.localScale = rockHomeScale * (1f - t * t);
                Fade(rockRenderers, 1f - t);
                yield return null;
            }

            rock.gameObject.SetActive(false);
        }

        /// <summary>Dust flung out round the blade where it leaves the stone.</summary>
        private void Puff(DustTrail.Cloud cloud, Vector3 point, int count, float strength)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 velocity = outward * (settings.dustSpeed * strength * UnityEngine.Random.Range(0.5f, 1f))
                                   + Vector3.up * (settings.dust.rise * strength * UnityEngine.Random.Range(0.6f, 1.2f));
                cloud.Emit(point + outward * settings.dustRadius, velocity, settings.dust);
            }
        }

        /// <summary>The top of the stone, under the blade.</summary>
        private Vector3 DustPoint(Vector3 bladePosition)
        {
            if (rockRenderers.Length == 0)
            {
                return bladePosition;
            }

            Bounds bounds = rockRenderers[0].bounds;

            for (int i = 1; i < rockRenderers.Length; i++)
            {
                bounds.Encapsulate(rockRenderers[i].bounds);
            }

            return new Vector3(bladePosition.x, bounds.max.y, bladePosition.z);
        }

        /// <summary>How far down the blade's local -Y its tip reaches from its pivot.</summary>
        private void MeasureTip()
        {
            float lowest = 0f;
            bool found = false;

            foreach (MeshFilter part in blade.GetComponentsInChildren<MeshFilter>())
            {
                Mesh mesh = part.sharedMesh;

                if (mesh == null)
                {
                    continue;
                }

                Bounds bounds = mesh.bounds;

                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                    float y = blade.InverseTransformPoint(part.transform.TransformPoint(local)).y;

                    if (!found || y < lowest)
                    {
                        lowest = y;
                        found = true;
                    }
                }
            }

            // Local units to world: the reach is used along the world-space blade.
            tipReach = found ? -lowest * blade.lossyScale.y : 1f;
        }

        private Vector3 Tip()
        {
            return blade.position - blade.up * tipReach;
        }

        /// <summary>Turns the blade tip first along <paramref name="direction"/>, and moves it so the tip is at <paramref name="tip"/>.</summary>
        private void PlaceTip(Vector3 tip, Vector3 direction)
        {
            Quaternion rotation = TipToward(direction);
            blade.SetPositionAndRotation(tip + rotation * Vector3.up * tipReach, rotation);
        }

        /// <summary>The rotation pointing the blade's tip, its local -Y, along <paramref name="direction"/>.</summary>
        private static Quaternion TipToward(Vector3 direction)
        {
            Vector3 up = Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(direction, up) * Quaternion.Euler(-90f, 0f, 0f);
        }

        private static Vector3 AimPoint(Transform aimAt, Collider body)
        {
            return body != null ? body.bounds.center : aimAt.position;
        }

        /// <summary>The floor under <paramref name="point"/>, for the warning circle.</summary>
        private Vector3 Floor(Vector3 point)
        {
            if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 30f,
                                settings.floorMask, QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * 0.02f;
            }

            return point;
        }

        /// <summary>The nearest thing the tip touches between the two points, the boss aside.</summary>
        private bool Cast(Vector3 from, Vector3 to, out RaycastHit nearest)
        {
            nearest = default;
            Vector3 path = to - from;
            float distance = path.magnitude;

            if (distance <= 0f)
            {
                return false;
            }

            int count = Physics.SphereCastNonAlloc(from, settings.radius, path / distance, hits, distance,
                                                   settings.hitMask, QueryTriggerInteraction.Ignore);
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                Transform other = hits[i].collider.transform;

                if ((ownerRoot != null && other.IsChildOf(ownerRoot)) || other.IsChildOf(transform))
                {
                    continue;
                }

                if (!found || hits[i].distance < nearest.distance)
                {
                    nearest = hits[i];
                    found = true;
                }
            }

            return found;
        }

        private void BuildTrail()
        {
            GameObject host = new GameObject("Trail");
            host.transform.SetParent(blade, false);

            trail = host.AddComponent<TrailRenderer>();
            trail.time = settings.trailTime;
            trail.widthMultiplier = settings.trailWidth;
            trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            trail.minVertexDistance = 0.05f;
            trail.numCapVertices = 0;
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.sharedMaterial = FlatMaterial.DoubleSided;

            if (trail.sharedMaterial != null)
            {
                FlatMaterial.Tint(trail, colour, new MaterialPropertyBlock());
            }
        }

        /// <summary>Leaves the trail in the world to shrink away.</summary>
        private void DropTrail()
        {
            if (trail == null)
            {
                return;
            }

            trail.transform.SetParent(null, true);
            trail.emitting = false;
            Destroy(trail.gameObject, trail.time);
            trail = null;
        }

        private EnergyBurst.Settings Recolour(EnergyBurst.Settings from)
        {
            return new EnergyBurst.Settings
            {
                flashSize = from.flashSize,
                flashTime = from.flashTime,
                sparkCount = from.sparkCount,
                sparkSize = from.sparkSize,
                sparkSpeed = from.sparkSpeed,
                sparkLife = from.sparkLife,
                colour = colour,
            };
        }

        private void Paint(Renderer[] renderers, Color tint)
        {
            foreach (Renderer part in renderers)
            {
                FlatMaterial.Tint(part, tint, block);
            }
        }

        private void Fade(Renderer[] renderers, float amount)
        {
            foreach (Renderer part in renderers)
            {
                part.GetPropertyBlock(block);
                block.SetFloat(FadeId, amount);
                part.SetPropertyBlock(block);
            }
        }
    }
}
