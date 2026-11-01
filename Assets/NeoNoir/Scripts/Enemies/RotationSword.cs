using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One of the swords of <see cref="LunaRotationPattern"/>: drops tip
    /// first into the floor beside her, sweeps round her laying a hurting
    /// trail of its colour, then stops, glistens, and explodes.
    /// </summary>
    /// <remarks>
    /// In order, its times counted from when it was made:
    ///   1. Drop: appears <see cref="Settings.dropHeight"/> above its spot and
    ///      falls over <see cref="Settings.dropTime"/> to stand tip down,
    ///      sunk <see cref="Settings.sinkDepth"/> into the floor.
    ///   2. Spin: for the spin time it is given, it circles the middle at its
    ///      own speed, edge first, a <see cref="RoseFloorTrail"/> of its
    ///      colour cut into the floor behind it.
    ///   3. Glisten: it stops, and glistens in its colour for
    ///      <see cref="Settings.glistenTime"/>.
    ///   4. It explodes: a <see cref="BlastSphere"/> of
    ///      <see cref="Settings.blastRadius"/> in its colour, and
    ///      <see cref="Settings.blastDamage"/> to the player inside it.
    /// From landing until it explodes, the player who comes within
    /// <see cref="Settings.touchRadius"/> of it, measured flat, takes
    /// <see cref="Settings.touchDamage"/>.
    ///
    /// Swords made with the same <see cref="Group"/> share the hurting: a
    /// touch of any of them waits <see cref="Settings.touchCooldown"/> before
    /// any can hurt by touch again, and however many explosions the player is
    /// in, they are hurt by one. If its owner dies it is gone at once; its
    /// trail stops hurting and fades by itself.
    ///
    /// The model, if given, should stand upright, tip down, with NeoNoir/Flat
    /// materials so it takes the colour; its colliders are removed. Without
    /// one it is a plain thin slab.
    /// </remarks>
    public sealed class RotationSword : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("The sword model, upright, tip down, with NeoNoir/Flat materials. Empty uses a plain slab.")]
            public GameObject swordPrefab;

            [Header("Drop")]
            [Tooltip("How far above its spot it appears.")]
            [Min(0f)] public float dropHeight = 20f;

            [Tooltip("Seconds it takes to fall. It speeds up all the way down.")]
            [Min(0.01f)] public float dropTime = 0.5f;

            [Tooltip("How far its tip sinks into the floor.")]
            [Min(0f)] public float sinkDepth = 0.6f;

            [Header("Touch")]
            [Tooltip("The player this close, measured flat, is hurt by touching it.")]
            [Min(0f)] public float touchRadius = 0.8f;

            [Tooltip("Damage to the player touching a sword.")]
            [Min(0f)] public float touchDamage = 15f;

            [Tooltip("Seconds after a touch hurts before any sword can hurt by touch again.")]
            [Min(0f)] public float touchCooldown = 0.5f;

            [Header("Trail")]
            [Tooltip("The trail cut behind it. Its Colour is replaced by the sword's; its petals are never shed.")]
            public RoseFloorTrail.Settings trail = new RoseFloorTrail.Settings
            {
                linger = 0.5f,
                fadeTime = 0.2f,
                damagePerSecond = 10f,
                tickInterval = 0.25f,
                radius = 0.5f,
                width = 1f,
            };

            [Header("End")]
            [Tooltip("Seconds it glistens, stopped, before it explodes.")]
            [Min(0.01f)] public float glistenTime = 1.2f;

            [Tooltip("The glisten. Its Colour is replaced by the sword's.")]
            public BladeGlisten.Settings glisten = new BladeGlisten.Settings();

            [Tooltip("Radius of the explosion, and of its damage.")]
            [Min(0f)] public float blastRadius = 3f;

            [Tooltip("Damage to the player inside an explosion. Once, however many they are in.")]
            [Min(0f)] public float blastDamage = 20f;

            [Tooltip("The explosion. Its colour is replaced by the sword's.")]
            public BlastSphere.Settings blast = new BlastSphere.Settings
            {
                growTime = 0.15f,
                fadeTime = 0.4f,
                outlineWidth = 0.2f,
                outlineColour = new Color(2f, 2f, 2f, 1f),
            };
        }

        /// <summary>Hurting shared by the swords of one attack.</summary>
        public sealed class Group
        {
            public float touchReadyAt;
            public bool blastHit;
        }

        private enum Stage { Dropping, Spinning, Glistening }

        private Settings settings;
        private Group group;
        private Enemy owner;
        private PlayerHealth player;
        private RoseFloorTrail trail;
        private BladeGlisten shine;
        private Color colour;
        private Color plain;
        private Vector3 centre;
        private Stage stage;
        private float radius;
        private float angle;
        private float speed;
        private float spinTime;
        private float standHeight;
        private float clock;

        /// <summary>
        /// Drops a sword, in <paramref name="plainColour"/> brightened to
        /// <paramref name="swordColour"/>, <paramref name="distance"/> from
        /// <paramref name="middle"/> at <paramref name="bearing"/> degrees,
        /// to circle it at <paramref name="degreesPerSecond"/> (above 0
        /// anticlockwise seen from above) for <paramref name="spin"/> seconds
        /// once landed.
        /// </summary>
        public static RotationSword Drop(Enemy owner, Vector3 middle, float bearing, float distance,
                                         float degreesPerSecond, float spin, Color swordColour, Color plainColour,
                                         Group group, Settings settings)
        {
            GameObject host;

            if (settings.swordPrefab != null)
            {
                host = Instantiate(settings.swordPrefab);
            }
            else
            {
                host = GameObject.CreatePrimitive(PrimitiveType.Cube);
                host.transform.localScale = new Vector3(0.3f, 3f, 0.08f);
                host.GetComponent<MeshRenderer>().sharedMaterial = FlatMaterial.Shared;
            }

            host.name = "RotationSword";

            foreach (Collider part in host.GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
                Destroy(part);
            }

            RotationSword sword = host.AddComponent<RotationSword>();
            sword.Begin(owner, middle, bearing, distance, degreesPerSecond, spin, swordColour, plainColour, group, settings);
            return sword;
        }

        private void Begin(Enemy enemy, Vector3 middle, float bearing, float distance, float degreesPerSecond,
                           float spin, Color swordColour, Color plainColour, Group swordGroup, Settings swordSettings)
        {
            owner = enemy;
            settings = swordSettings;
            group = swordGroup;
            centre = middle;
            angle = bearing;
            radius = distance;
            speed = degreesPerSecond;
            spinTime = spin;
            colour = swordColour;
            plain = plainColour;
            player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

            MaterialPropertyBlock block = new MaterialPropertyBlock();

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                if (part is MeshRenderer || part is SkinnedMeshRenderer)
                {
                    FlatMaterial.Tint(part, colour, block);
                    part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    part.receiveShadows = false;
                }
            }

            // Measured stood upright, as it stays.
            transform.rotation = Facing();
            standHeight = centre.y + TipBelowPivot() - settings.sinkDepth;
            Place(standHeight + settings.dropHeight);
        }

        private void Update()
        {
            if (owner == null || owner.Health.IsDead || !owner.isActiveAndEnabled)
            {
                Destroy(gameObject);
                return;
            }

            clock += Time.deltaTime;

            switch (stage)
            {
                case Stage.Dropping:
                {
                    float t = Mathf.Clamp01(clock / settings.dropTime);
                    Place(standHeight + settings.dropHeight * (1f - t * t));

                    if (t >= 1f)
                    {
                        Advance(Stage.Spinning);
                        StartTrail();
                    }

                    break;
                }

                case Stage.Spinning:
                {
                    angle += speed * Time.deltaTime;
                    Place(standHeight);

                    if (trail != null)
                    {
                        Vector3 under = transform.position;
                        under.y = centre.y;
                        trail.Emit(under, Tangent());
                    }

                    Touch();

                    if (clock >= spinTime)
                    {
                        Advance(Stage.Glistening);
                        EndTrail();

                        BladeGlisten.Settings glisten = JsonUtility.FromJson<BladeGlisten.Settings>(JsonUtility.ToJson(settings.glisten));
                        glisten.colour = plain;
                        shine = BladeGlisten.Begin(transform, glisten, settings.glistenTime);
                    }

                    break;
                }

                case Stage.Glistening:
                {
                    shine.Tick(clock);
                    Touch();

                    if (clock >= settings.glistenTime)
                    {
                        Explode();
                    }

                    break;
                }
            }
        }

        private void Advance(Stage next)
        {
            stage = next;
            clock = 0f;
        }

        private void StartTrail()
        {
            RoseFloorTrail.Settings trailSettings = JsonUtility.FromJson<RoseFloorTrail.Settings>(JsonUtility.ToJson(settings.trail));
            trailSettings.colour = colour;
            trail = RoseFloorTrail.Create(owner, trailSettings, null);
        }

        /// <summary>Leaves the trail to fade by itself, and be gone once it has.</summary>
        private void EndTrail()
        {
            if (trail != null)
            {
                Destroy(trail.gameObject, settings.trail.linger + settings.trail.fadeTime + 0.1f);
                trail = null;
            }
        }

        private void Touch()
        {
            if (player == null || player.IsDead || settings.touchDamage <= 0f || Time.time < group.touchReadyAt)
            {
                return;
            }

            Vector3 offset = player.transform.position - transform.position;
            offset.y = 0f;

            if (offset.sqrMagnitude <= settings.touchRadius * settings.touchRadius)
            {
                player.TakeDamage(settings.touchDamage, transform.position);
                group.touchReadyAt = Time.time + settings.touchCooldown;
            }
        }

        private void Explode()
        {
            Vector3 middle = transform.position;
            middle.y = centre.y + 1f;

            if (settings.blastRadius > 0f)
            {
                BlastSphere.Settings blast = settings.blast;
                BlastSphere.Create(middle, settings.blastRadius, new BlastSphere.Settings
                {
                    growTime = blast.growTime,
                    fadeTime = blast.fadeTime,
                    colour = colour,
                    outlineWidth = blast.outlineWidth,
                    outlineColour = blast.outlineColour,
                    material = blast.material,
                });
            }

            if (player != null && !player.IsDead && settings.blastDamage > 0f && !group.blastHit)
            {
                Vector3 offset = player.transform.position - middle;
                offset.y = 0f;

                if (offset.sqrMagnitude <= settings.blastRadius * settings.blastRadius)
                {
                    player.TakeDamage(settings.blastDamage, middle);
                    group.blastHit = true;
                }
            }

            Destroy(gameObject);
        }

        private void Place(float height)
        {
            float radians = angle * Mathf.Deg2Rad;
            Vector3 point = centre + new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * radius;
            point.y = height;
            transform.SetPositionAndRotation(point, Facing());
        }

        /// <summary>The way round it goes, flat.</summary>
        private Vector3 Tangent()
        {
            float radians = angle * Mathf.Deg2Rad;
            Vector3 tangent = new Vector3(-Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            return speed < 0f ? -tangent : tangent;
        }

        /// <summary>Upright, edge first along the way it goes round.</summary>
        private Quaternion Facing()
        {
            return Quaternion.LookRotation(Tangent(), Vector3.up);
        }

        private void OnDestroy()
        {
            EndTrail();
        }

        /// <summary>How far its art reaches below its pivot, as it stands now.</summary>
        private float TipBelowPivot()
        {
            float lowest = float.MaxValue;

            foreach (Renderer part in GetComponentsInChildren<Renderer>())
            {
                if (part.enabled && (part is MeshRenderer || part is SkinnedMeshRenderer))
                {
                    lowest = Mathf.Min(lowest, part.bounds.min.y);
                }
            }

            return lowest == float.MaxValue ? 0f : Mathf.Max(0f, transform.position.y - lowest);
        }
    }
}
