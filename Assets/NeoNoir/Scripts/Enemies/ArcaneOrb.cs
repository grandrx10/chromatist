using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// An orb of the witch's arcane bomb: swells in out in the arena, drifts
    /// in toward her gathering runes, and detonates if it gets close enough.
    /// </summary>
    /// <remarks>
    /// In order, from <see cref="Spawn"/>:
    ///   1. Grows from a hundredth of its size to full over
    ///      <see cref="Settings.growTime"/> seconds.
    ///   2. Hangs still for <see cref="Settings.holdTime"/> seconds.
    ///   3. Floats straight in toward the enemy that made it, at
    ///      <see cref="Settings.speed"/>, keeping its height. Once within
    ///      <see cref="Settings.detonateDistance"/> of her, flat, it
    ///      detonates: a burst, a camera shake, and the callback given to
    ///      <see cref="Spawn"/>, which fires the shots.
    /// All the while, runes are written on a shell round it and sink into
    /// it, ever more of them the closer it gets.
    ///
    /// It has an <see cref="EnemyHealth"/> of its own, so every blade can hit
    /// it. Killed, it pops like a bubble instead: a quick swell as it
    /// dissolves, a little splash, and nothing fired. It pops the same way if
    /// its enemy dies, is destroyed or is disabled. Either way its runes fly
    /// loose and dissolve before it destroys itself.
    ///
    /// The player passes through it, but takes
    /// <see cref="Settings.contactDamage"/> on touching it, and again every
    /// <see cref="Settings.contactInterval"/> seconds they stay in it.
    ///
    /// Any colliders on the art are replaced by one sphere of
    /// <see cref="Settings.radius"/>, and the whole orb is put on the Enemy
    /// layer, so homing missiles, which leave that layer out, fly through it.
    /// The art dissolves through a _Fade property where its material has one,
    /// as NeoNoir/Flat does.
    /// </remarks>
    public sealed class ArcaneOrb : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Orb")]
            [Tooltip("Hit points. Killed, the orb pops without exploding.")]
            [Min(1f)] public float health = 30f;

            [Tooltip("Seconds it takes to grow from a hundredth of its size to full.")]
            [Min(0f)] public float growTime = 1f;

            [Tooltip("Seconds it hangs still once grown, before it moves.")]
            [Min(0f)] public float holdTime = 0.5f;

            [Tooltip("Speed it floats in toward the enemy, in units per second.")]
            [Min(0f)] public float speed = 4f;

            [Tooltip("Detonates once this close to the enemy, measured flat.")]
            [Min(0f)] public float detonateDistance = 5f;

            [Tooltip("Radius of the orb at full size, for blades hitting it and the player touching it.")]
            [Min(0.05f)] public float radius = 0.75f;

            [Tooltip("Size of the ball drawn when no orb art is given, across.")]
            [Min(0.05f)] public float size = 1.5f;

            [Tooltip("Colour of the ball drawn when no orb art is given. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 0.15f, 0.1f, 1f);

            [Header("Touch")]
            [Tooltip("Damage to the player each time touching the orb hurts them.")]
            [Min(0f)] public float contactDamage = 10f;

            [Tooltip("Seconds between hurts while the player stays in the orb.")]
            [Min(0.05f)] public float contactInterval = 1f;

            [Header("Gathering runes")]
            [Tooltip("Runes a second: x when the orb appears, y as it reaches the detonate distance.")]
            public Vector2 runesPerSecond = new Vector2(6f, 20f);

            [Tooltip("How far from the orb's centre the runes are written, before they sink in.")]
            [Min(0.1f)] public float shellRadius = 2.5f;

            [Tooltip("Shortest and longest seconds a rune takes to sink in.")]
            public Vector2 runeLife = new Vector2(0.6f, 0.9f);

            [Tooltip("Seconds each rune takes to be written.")]
            [Min(0.01f)] public float writeTime = 0.12f;

            [Tooltip("Height of each rune, in metres.")]
            [Min(0.05f)] public float runeSize = 0.35f;

            [Tooltip("Width of the runes' lines, in metres.")]
            [Min(0.01f)] public float lineWidth = 0.04f;

            [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
            [ColorUsage(false, true)] public Color runeColour = new Color(3f, 0.15f, 0.1f, 1f);

            [Tooltip("Material to draw the runes with. Empty uses a shared NeoNoir/Flat one. Needs a _Fade property to dissolve.")]
            public Material runeMaterial;

            [Tooltip("Seconds runes take to dissolve once flung loose by a pop or a detonation.")]
            [Min(0.01f)] public float looseTime = 0.35f;

            [Header("Pop")]
            [Tooltip("Seconds the pop takes: the orb swells as it dissolves away.")]
            [Min(0.01f)] public float popTime = 0.15f;

            [Tooltip("Size the orb swells to as it pops, times its size.")]
            [Min(1f)] public float popSwell = 1.3f;

            [Tooltip("The splash where it pops.")]
            public EnergyBurst.Settings pop = new EnergyBurst.Settings
            {
                flashSize = 0f,
                sparkCount = 12,
                sparkSize = 0.2f,
                sparkSpeed = new Vector2(2f, 4f),
                sparkLife = 0.3f,
            };

            [Header("Detonation")]
            [Tooltip("The burst where it detonates.")]
            public EnergyBurst.Settings detonation = new EnergyBurst.Settings
            {
                flashSize = 2f,
                flashTime = 0.25f,
                sparkCount = 16,
                sparkSize = 0.4f,
                sparkSpeed = new Vector2(5f, 11f),
                sparkLife = 0.45f,
            };

            [Tooltip("How far the camera shakes on a detonation. 0 disables it.")]
            [Min(0f)] public float shakeStrength = 0.3f;

            [Tooltip("Seconds the camera shake takes to die away.")]
            [Min(0.01f)] public float shakeDuration = 0.25f;
        }

        private sealed class Rune
        {
            public Transform transform;
            public RuneDrawing drawing;
            public Vector3 direction;
            public float age;
            public float life;
            public float roll;

            // Once flung loose, it flies on by itself from where it was.
            public bool loose;
            public Vector3 velocity;
            public float looseAge;
            public float looseFade;
        }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly Collider[] touching = new Collider[16];

        private readonly List<Rune> runes = new List<Rune>();
        // Made in Begin: Unity will not build one in a field initializer.
        private MaterialPropertyBlock block;

        private Settings settings;
        private Enemy owner;
        private Action<Vector3> detonated;
        private EnemyHealth health;
        private SphereCollider body;
        private Renderer[] art;
        private Transform runeHost;

        private Vector3 baseScale;
        private float startDistance;
        private float age;
        private float growth;
        private float owed;
        private int runeIndex;
        private float touchCooldown;

        private bool ending;
        private bool popping;
        private float endAge;

        /// <summary>
        /// Makes an orb of <paramref name="prefab"/> at
        /// <paramref name="position"/>, to float in on
        /// <paramref name="enemy"/>. <paramref name="onDetonate"/> is called
        /// with where it went off, if it gets there. Null art makes a plain
        /// glowing ball.
        /// </summary>
        public static ArcaneOrb Spawn(GameObject prefab, Vector3 position, Enemy enemy, Settings settings,
                                      Action<Vector3> onDetonate)
        {
            GameObject host;

            if (prefab != null)
            {
                host = Instantiate(prefab, position, Quaternion.identity);
            }
            else
            {
                host = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                host.name = "ArcaneOrb";
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

            int layer = LayerMask.NameToLayer("Enemy");

            if (layer >= 0)
            {
                foreach (Transform part in host.GetComponentsInChildren<Transform>(true))
                {
                    part.gameObject.layer = layer;
                }
            }

            ArcaneOrb orb = host.AddComponent<ArcaneOrb>();
            orb.Begin(enemy, settings, onDetonate);
            return orb;
        }

        private void Begin(Enemy enemy, Settings orbSettings, Action<Vector3> onDetonate)
        {
            settings = orbSettings;
            owner = enemy;
            detonated = onDetonate;
            art = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
            baseScale = transform.localScale;

            // Sized now, at full scale, so it shrinks and grows with the orb.
            float scale = Mathf.Max(0.0001f, Mathf.Max(transform.lossyScale.x,
                                                       Mathf.Max(transform.lossyScale.y, transform.lossyScale.z)));
            body = gameObject.AddComponent<SphereCollider>();
            body.radius = settings.radius / scale;

            // Moved by its transform; a kinematic body keeps the physics
            // engine from treating the collider as a static one.
            Rigidbody rigidbody = gameObject.AddComponent<Rigidbody>();
            rigidbody.isKinematic = true;
            rigidbody.useGravity = false;

            if (owner != null)
            {
                IgnoreCollisions(owner.transform);

                if (owner.HasTarget)
                {
                    IgnoreCollisions(owner.Target);
                }
            }

            health = gameObject.AddComponent<EnemyHealth>();
            health.SetMaxHealth(settings.health);
            health.DestroyOnDeath = false;
            health.Died += OnDied;

            runeHost = new GameObject("ArcaneOrbRunes").transform;
            startDistance = owner != null ? FlatDistanceToOwner() : 0f;

            transform.localScale = baseScale * 0.01f;
        }

        /// <summary>The player passes through, and the enemy too, should it ever get that close.</summary>
        private void IgnoreCollisions(Transform other)
        {
            foreach (Collider part in other.GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(body, part);
            }
        }

        private void OnDied(EnemyHealth _)
        {
            Pop();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (ending)
            {
                UpdateEnd(deltaTime);
                UpdateRunes(deltaTime);
                return;
            }

            if (owner == null || owner.Health.IsDead || !owner.isActiveAndEnabled)
            {
                Pop();
                UpdateRunes(deltaTime);
                return;
            }

            age += deltaTime;

            float grown = settings.growTime > 0f ? Mathf.Clamp01(age / settings.growTime) : 1f;
            growth = Mathf.Lerp(0.01f, 1f, 1f - (1f - grown) * (1f - grown));
            transform.localScale = baseScale * growth;

            if (age >= settings.growTime + settings.holdTime)
            {
                Vector3 goal = owner.transform.position;
                goal.y = transform.position.y;
                transform.position = Vector3.MoveTowards(transform.position, goal, settings.speed * deltaTime);
            }

            float distance = FlatDistanceToOwner();

            if (distance <= settings.detonateDistance)
            {
                Detonate();
                UpdateRunes(deltaTime);
                return;
            }

            Touch(deltaTime);

            // Gathers faster the closer it gets.
            float closeness = 1f - Mathf.InverseLerp(settings.detonateDistance, startDistance, distance);
            owed += Mathf.Lerp(settings.runesPerSecond.x, settings.runesPerSecond.y, closeness) * deltaTime;

            while (owed >= 1f)
            {
                owed -= 1f;
                Gather();
            }

            UpdateRunes(deltaTime);
        }

        private float FlatDistanceToOwner()
        {
            Vector3 offset = owner.transform.position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        /// <summary>Hurts the player if they are in the orb, at most once each interval.</summary>
        private void Touch(float deltaTime)
        {
            touchCooldown -= deltaTime;

            if (touchCooldown > 0f || settings.contactDamage <= 0f)
            {
                return;
            }

            int count = Physics.OverlapSphereNonAlloc(transform.position, settings.radius * growth, touching,
                                                      ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                PlayerHealth player = touching[i].GetComponentInParent<PlayerHealth>();

                if (player != null)
                {
                    player.TakeDamage(settings.contactDamage, transform.position);
                    touchCooldown = settings.contactInterval;
                    return;
                }
            }
        }

        /// <summary>Pops like a bubble: no burst of shots, just a splash.</summary>
        private void Pop()
        {
            if (ending)
            {
                return;
            }

            End();
            popping = true;
            EnergyBurst.Create(transform.position, settings.pop);
            Loosen(1f);
        }

        private void Detonate()
        {
            if (ending)
            {
                return;
            }

            End();
            ShowArt(false);

            Vector3 point = transform.position;
            EnergyBurst.Create(point, settings.detonation);

            if (settings.shakeStrength > 0f)
            {
                TopDownCamera.Shake(settings.shakeStrength, settings.shakeDuration);
            }

            Loosen(2f);
            detonated?.Invoke(point);
        }

        /// <summary>Stops it being hit, touched, or doing anything but finishing.</summary>
        private void End()
        {
            ending = true;
            body.enabled = false;
            health.Invulnerable = true;
        }

        private void UpdateEnd(float deltaTime)
        {
            endAge += deltaTime;

            if (popping)
            {
                float t = Mathf.Clamp01(endAge / settings.popTime);
                transform.localScale = baseScale * (growth * Mathf.Lerp(1f, settings.popSwell, 1f - (1f - t) * (1f - t)));
                SetArtFade(1f - t);

                if (t >= 1f)
                {
                    popping = false;
                    ShowArt(false);
                }
            }

            if (!popping && runes.Count == 0)
            {
                Destroy(gameObject);
            }
        }

        private void SetArtFade(float fade)
        {
            foreach (Renderer part in art)
            {
                if (part == null || part.sharedMaterial == null || !part.sharedMaterial.HasProperty(FadeId))
                {
                    continue;
                }

                part.GetPropertyBlock(block);
                block.SetFloat(FadeId, fade);
                part.SetPropertyBlock(block);
            }
        }

        private void ShowArt(bool show)
        {
            foreach (Renderer part in art)
            {
                if (part != null)
                {
                    part.enabled = show;
                }
            }
        }

        /// <summary>Writes a rune somewhere on the shell round the orb, to sink in.</summary>
        private void Gather()
        {
            GameObject host = new GameObject("Rune");
            host.transform.SetParent(runeHost, false);

            RuneDrawing drawing = new RuneDrawing(host, settings.runeMaterial != null ? settings.runeMaterial : FlatMaterial.Shared);
            drawing.AddRune(RuneGlyphs.Rune(runeIndex++), Vector3.zero, Vector3.forward, settings.runeSize,
                            settings.lineWidth, 0f, 1f);
            drawing.Progress = 0f;
            drawing.SetColour(settings.runeColour);

            runes.Add(new Rune
            {
                transform = host.transform,
                drawing = drawing,
                direction = UnityEngine.Random.onUnitSphere,
                life = Mathf.Max(0.05f, UnityEngine.Random.Range(settings.runeLife.x, settings.runeLife.y)),
                roll = UnityEngine.Random.Range(-30f, 30f),
            });

            PlaceRune(runes[runes.Count - 1], RuneDrawing.FacingCamera());
        }

        /// <summary>Flings every rune outward, <paramref name="speedScale"/> times as fast, to dissolve.</summary>
        private void Loosen(float speedScale)
        {
            foreach (Rune rune in runes)
            {
                if (rune.loose)
                {
                    continue;
                }

                rune.loose = true;
                rune.velocity = rune.direction * (3f * speedScale);
                rune.looseFade = RuneFade(rune);
            }
        }

        private void UpdateRunes(float deltaTime)
        {
            Quaternion facing = RuneDrawing.FacingCamera();
            float drag = Mathf.Exp(-4f * deltaTime);

            for (int i = runes.Count - 1; i >= 0; i--)
            {
                Rune rune = runes[i];
                rune.age += deltaTime;

                if (rune.loose)
                {
                    rune.looseAge += deltaTime;

                    if (rune.looseAge >= settings.looseTime)
                    {
                        RemoveRune(i);
                        continue;
                    }

                    rune.velocity *= drag;
                    rune.transform.SetPositionAndRotation(rune.transform.position + rune.velocity * deltaTime,
                                                          facing * Quaternion.AngleAxis(rune.roll, Vector3.up));
                    rune.drawing.Progress = rune.age / settings.writeTime;
                    rune.drawing.SetFade(rune.looseFade * (1f - rune.looseAge / settings.looseTime));
                    continue;
                }

                if (rune.age >= rune.life)
                {
                    RemoveRune(i);
                    continue;
                }

                PlaceRune(rune, facing);
            }
        }

        /// <summary>
        /// On its way in from the shell to the orb's surface, picking up
        /// speed as it goes, and dissolving into the orb at the end.
        /// </summary>
        private void PlaceRune(Rune rune, Quaternion facing)
        {
            float t = Mathf.Clamp01(rune.age / rune.life);
            float surface = settings.radius * growth * 0.8f;
            float reach = Mathf.Lerp(settings.shellRadius, surface, t * t);

            // The drawing faces the camera along its local up, so a roll
            // about that tilts it on screen.
            rune.transform.SetPositionAndRotation(transform.position + rune.direction * reach,
                                                  facing * Quaternion.AngleAxis(rune.roll, Vector3.up));
            rune.transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.5f, t);
            rune.drawing.Progress = rune.age / settings.writeTime;
            rune.drawing.SetFade(RuneFade(rune));
        }

        private static float RuneFade(Rune rune)
        {
            return 1f - Mathf.InverseLerp(0.7f, 1f, rune.age / rune.life);
        }

        private void RemoveRune(int index)
        {
            runes[index].drawing.Release();
            Destroy(runes[index].transform.gameObject);
            runes.RemoveAt(index);
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.Died -= OnDied;
            }

            foreach (Rune rune in runes)
            {
                rune.drawing.Release();
            }

            if (runeHost != null)
            {
                Destroy(runeHost.gameObject);
            }
        }
    }
}
