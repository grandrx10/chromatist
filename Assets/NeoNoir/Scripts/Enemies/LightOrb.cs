using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A sphere of light from Luna's centre attack: swells in out in the
    /// arena, streams in toward her leaving a trail, and splits into
    /// <see cref="PrismShot"/>s when it touches her.
    /// </summary>
    /// <remarks>
    /// In order, from <see cref="Spawn"/>:
    ///   1. Grows from a hundredth of its size to full over
    ///      <see cref="Settings.growTime"/> seconds.
    ///   2. Hangs still for <see cref="Settings.holdTime"/> seconds.
    ///   3. Flies straight in toward the enemy that made it, at
    ///      <see cref="Settings.speed"/>, keeping its height. The moment it
    ///      touches one of her colliders it splits: a burst, and the callback
    ///      given to <see cref="Spawn"/>, which fires the shots. Should it
    ///      reach her middle without touching any, it splits there.
    ///
    /// It cannot be destroyed: it has no health and no collider, so blades
    /// pass straight through. The player takes
    /// <see cref="Settings.contactDamage"/> on touching it, and again every
    /// <see cref="Settings.contactInterval"/> seconds they stay in it.
    ///
    /// If its enemy dies, is destroyed or is disabled, it shrinks away with
    /// nothing fired. Any colliders on the art are removed. Its trail is left
    /// behind when it goes, to shrink away rather than vanish.
    /// </remarks>
    public sealed class LightOrb : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Orb")]
            [Tooltip("Seconds it takes to grow from a hundredth of its size to full.")]
            [Min(0f)] public float growTime = 0.6f;

            [Tooltip("Seconds it hangs still once grown, before it moves.")]
            [Min(0f)] public float holdTime = 0.3f;

            [Tooltip("Speed it flies in toward the enemy, in units per second.")]
            [Min(0f)] public float speed = 6f;

            [Tooltip("Radius of the orb at full size, for touching the player and the enemy.")]
            [Min(0.05f)] public float radius = 0.6f;

            [Tooltip("Size of the ball drawn when no orb art is given, across.")]
            [Min(0.05f)] public float size = 1.2f;

            [Tooltip("Colour of the ball drawn when no orb art is given, and of the trail. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 3f, 3f, 1f);

            [Header("Trail")]
            [Tooltip("Seconds of travel the trail covers. Longer is a longer tail.")]
            [Min(0.01f)] public float trailTime = 0.5f;

            [Tooltip("Width of the trail at the orb. It narrows to nothing at the tail.")]
            [Min(0.001f)] public float trailWidth = 0.6f;

            [Header("Touch")]
            [Tooltip("Damage to the player each time touching the orb hurts them.")]
            [Min(0f)] public float contactDamage = 10f;

            [Tooltip("Seconds between hurts while the player stays in the orb.")]
            [Min(0.05f)] public float contactInterval = 1f;

            [Header("Split")]
            [Tooltip("The burst where it splits.")]
            public EnergyBurst.Settings split = new EnergyBurst.Settings
            {
                flashSize = 1.6f,
                flashTime = 0.2f,
                sparkCount = 12,
                sparkSize = 0.3f,
                sparkSpeed = new Vector2(4f, 9f),
                sparkLife = 0.35f,
                colour = new Color(3f, 3f, 3f, 1f),
            };

            [Tooltip("Seconds it takes to shrink away if its enemy dies.")]
            [Min(0.01f)] public float fadeTime = 0.2f;
        }

        private static readonly Collider[] touching = new Collider[16];

        private Settings settings;
        private Enemy owner;
        private Action<Vector3> split;
        private TrailRenderer trail;

        private Vector3 baseScale;
        private float age;
        private float growth;
        private float touchCooldown;

        private bool fading;
        private float fadeAge;

        /// <summary>
        /// Makes an orb of <paramref name="prefab"/> at
        /// <paramref name="position"/>, to fly in on <paramref name="enemy"/>.
        /// <paramref name="onSplit"/> is called with where it split, if it
        /// gets there. Null art makes a plain glowing ball.
        /// </summary>
        public static LightOrb Spawn(GameObject prefab, Vector3 position, Enemy enemy, Settings settings,
                                     Action<Vector3> onSplit)
        {
            GameObject host;

            if (prefab != null)
            {
                host = Instantiate(prefab, position, Quaternion.identity);
            }
            else
            {
                host = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                host.name = "LightOrb";
                host.transform.position = position;
                host.transform.localScale = Vector3.one * settings.size;

                MeshRenderer renderer = host.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.sharedMaterial = FlatMaterial.Shared;
                FlatMaterial.Tint(renderer, settings.colour, new MaterialPropertyBlock());
            }

            foreach (Collider part in host.GetComponentsInChildren<Collider>())
            {
                Destroy(part);
            }

            LightOrb orb = host.AddComponent<LightOrb>();
            orb.Begin(enemy, settings, onSplit);
            return orb;
        }

        private void Begin(Enemy enemy, Settings orbSettings, Action<Vector3> onSplit)
        {
            settings = orbSettings;
            owner = enemy;
            split = onSplit;
            baseScale = transform.localScale;
            transform.localScale = baseScale * 0.01f;
            BuildTrail();
        }

        private void BuildTrail()
        {
            GameObject host = new GameObject("Trail");
            host.transform.SetParent(transform, false);

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
                FlatMaterial.Tint(trail, settings.colour, new MaterialPropertyBlock());
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (fading)
            {
                fadeAge += deltaTime;
                float t = Mathf.Clamp01(fadeAge / settings.fadeTime);
                transform.localScale = baseScale * (growth * (1f - t));

                if (t >= 1f)
                {
                    Finish();
                }

                return;
            }

            if (owner == null || owner.Health.IsDead || !owner.isActiveAndEnabled)
            {
                fading = true;
                return;
            }

            age += deltaTime;

            float grown = settings.growTime > 0f ? Mathf.Clamp01(age / settings.growTime) : 1f;
            growth = Mathf.Lerp(0.01f, 1f, 1f - (1f - grown) * (1f - grown));
            transform.localScale = baseScale * growth;

            if (age < settings.growTime + settings.holdTime)
            {
                Touch(deltaTime, false);
                return;
            }

            Vector3 goal = owner.transform.position;
            goal.y = transform.position.y;
            transform.position = Vector3.MoveTowards(transform.position, goal, settings.speed * deltaTime);

            if (Touch(deltaTime, true) || (transform.position - goal).sqrMagnitude < 0.0001f)
            {
                Split();
            }
        }

        /// <summary>
        /// Hurts the player if they are in the orb, at most once each
        /// interval. True if it is touching its enemy and
        /// <paramref name="checkOwner"/> is set.
        /// </summary>
        private bool Touch(float deltaTime, bool checkOwner)
        {
            touchCooldown -= deltaTime;

            int count = Physics.OverlapSphereNonAlloc(transform.position, settings.radius * growth, touching,
                                                      Physics.AllLayers, QueryTriggerInteraction.Ignore);
            bool hitOwner = false;

            for (int i = 0; i < count; i++)
            {
                if (checkOwner && touching[i].transform.IsChildOf(owner.transform))
                {
                    hitOwner = true;
                    continue;
                }

                if (touchCooldown > 0f || settings.contactDamage <= 0f)
                {
                    continue;
                }

                PlayerHealth player = touching[i].GetComponentInParent<PlayerHealth>();

                if (player != null)
                {
                    player.TakeDamage(settings.contactDamage, transform.position);
                    touchCooldown = settings.contactInterval;
                }
            }

            return hitOwner;
        }

        private void Split()
        {
            Vector3 point = transform.position;
            EnergyBurst.Create(point, settings.split);
            Finish();
            split?.Invoke(point);
        }

        private void Finish()
        {
            DropTrail();
            Destroy(gameObject);
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
    }
}
