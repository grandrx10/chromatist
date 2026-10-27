using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Runes left hanging in the air behind a dashing enemy, each one a
    /// projectile that hurts the player who walks into it.
    /// </summary>
    /// <remarks>
    /// Drops runes only on frames something calls <see cref="KeepEmitting"/>,
    /// the way <see cref="DustTrail"/> kicks up dust, so whatever drives the
    /// dash calls it every frame it wants runes. Every
    /// <see cref="Settings.spacing"/> the enemy travels it drops
    /// <see cref="Settings.runesPerDrop"/>, spread across its width and up its
    /// height, so a dash leaves a thick band of them.
    ///
    /// Each rune is written in as it appears and floats where it was
    /// dropped, facing the camera: bobbing gently, drifting slowly off in a
    /// direction of its own and rocking a little. It lasts <see cref="Settings.lifetime"/> seconds,
    /// dissolving over the last <see cref="Settings.fadeTime"/>. Until it
    /// starts to dissolve, the player touching it takes
    /// <see cref="Settings.damage"/> and the rune bursts and is gone, as a
    /// projectile would be. A guarding <see cref="ParryBarrier"/> pops the
    /// runes it touches harmlessly.
    ///
    /// Not parented to the enemy, so runes stay where they were dropped and
    /// finish even if the enemy dies. The trail destroys itself once it has
    /// not been asked for runes in a while and the last one is gone.
    /// </remarks>
    public sealed class RuneTrail : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Distance the enemy travels between one drop of runes and the next.")]
            [Min(0.05f)] public float spacing = 1f;

            [Tooltip("Runes dropped each time.")]
            [Min(1)] public int runesPerDrop = 1;

            [Tooltip("How far either side of the enemy's path runes are dropped.")]
            [Min(0f)] public float halfWidth = 0.4f;

            [Tooltip("Lowest and highest above the enemy's feet runes are dropped.")]
            public Vector2 height = new Vector2(-0.2f, 0.4f);

            [Tooltip("Seconds each rune lasts.")]
            [Min(0.1f)] public float lifetime = 2f;

            [Tooltip("Seconds at the end of its life a rune spends dissolving. It does no harm once it starts.")]
            [Min(0.01f)] public float fadeTime = 0.5f;

            [Header("Hits")]
            [Tooltip("Damage to the player for each rune they touch.")]
            [Min(0f)] public float damage = 6f;

            [Tooltip("How close the player's body must come to a rune's centre to touch it.")]
            [Min(0.01f)] public float radius = 0.5f;

            [Tooltip("Burst where a rune hits the player or is parried.")]
            public EnergyBurst.Settings pop = new EnergyBurst.Settings
            {
                flashSize = 0.4f,
                flashTime = 0.1f,
                sparkCount = 4,
                sparkSize = 0.15f,
                sparkSpeed = new Vector2(2f, 4f),
                sparkLife = 0.2f,
            };

            [Header("Look")]
            [Tooltip("Height of each rune, in metres.")]
            [Min(0.05f)] public float runeSize = 1.2f;

            [Tooltip("Width of the runes' lines, in metres.")]
            [Min(0.01f)] public float lineWidth = 0.1f;

            [Header("Float")]
            [Tooltip("How far each rune bobs up and down from where it was dropped.")]
            [Min(0f)] public float bobHeight = 0.15f;

            [Tooltip("Bobs up and down this many times a second.")]
            [Min(0f)] public float bobRate = 0.6f;

            [Tooltip("Slowest and fastest each rune drifts, in a direction of its own, in units per second.")]
            public Vector2 drift = new Vector2(0.05f, 0.2f);

            [Tooltip("Most degrees each rune sways, rocking side to side as it floats.")]
            [Range(0f, 45f)] public float sway = 8f;

            [Tooltip("Seconds each rune takes to be written as it appears.")]
            [Min(0.01f)] public float writeTime = 0.1f;

            [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 0.15f, 0.1f, 1f);

            [Tooltip("Material to draw with. Empty uses a shared NeoNoir/Flat one. Needs a _Fade property to dissolve.")]
            public Material material;
        }

        private sealed class Rune
        {
            public Transform transform;
            public RuneDrawing drawing;
            public Vector3 anchor;
            public Vector3 drift;
            public float age;
            public float roll;
            public float phase;
        }

        /// <summary>Seconds without a call to <see cref="KeepEmitting"/> before an empty trail removes itself.</summary>
        private const float IdleLifetime = 1f;

        private readonly List<Rune> runes = new List<Rune>();

        private Settings settings;
        private Transform mover;
        private PlayerHealth player;
        private Collider playerBody;

        private int keptFrame = -1;
        private float idle;
        private Vector3 lastPosition;
        private float travelled;
        private int runeIndex;

        /// <summary>
        /// Starts a trail behind <paramref name="enemy"/>, its runes hurting
        /// the player it is fighting. Drops nothing until
        /// <see cref="KeepEmitting"/> is called.
        /// </summary>
        public static RuneTrail Create(Enemy enemy, Settings settings)
        {
            GameObject host = new GameObject("RuneTrail");
            RuneTrail trail = host.AddComponent<RuneTrail>();
            trail.settings = settings;
            trail.mover = enemy.transform;
            trail.lastPosition = enemy.transform.position;

            if (enemy.HasTarget)
            {
                trail.player = enemy.Target.GetComponentInParent<PlayerHealth>();

                foreach (Collider part in enemy.Target.GetComponentsInChildren<Collider>())
                {
                    if (!part.isTrigger)
                    {
                        trail.playerBody = part;
                        break;
                    }
                }
            }

            return trail;
        }

        /// <summary>Drops runes this frame as the enemy moves. Call every frame runes are wanted.</summary>
        public void KeepEmitting()
        {
            keptFrame = Time.frameCount;
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            bool emitting = keptFrame >= Time.frameCount - 1 && mover != null;

            if (mover != null)
            {
                Vector3 position = mover.position;

                if (emitting)
                {
                    Vector3 step = position - lastPosition;
                    step.y = 0f;
                    travelled += step.magnitude;

                    while (travelled >= settings.spacing)
                    {
                        travelled -= settings.spacing;
                        Drop(position, step);
                    }
                }
                else
                {
                    travelled = 0f;
                }

                lastPosition = position;
            }

            idle = emitting ? 0f : idle + deltaTime;

            UpdateRunes(deltaTime);

            if (runes.Count == 0 && idle > IdleLifetime)
            {
                Destroy(gameObject);
            }
        }

        private void Drop(Vector3 feet, Vector3 heading)
        {
            Vector3 side = heading.sqrMagnitude > 0.000001f
                ? Vector3.Cross(Vector3.up, heading.normalized)
                : Vector3.right;

            for (int i = 0; i < settings.runesPerDrop; i++)
            {
                Vector3 position = feet
                                   + side * UnityEngine.Random.Range(-settings.halfWidth, settings.halfWidth)
                                   + Vector3.up * UnityEngine.Random.Range(settings.height.x, settings.height.y);

                GameObject host = new GameObject("Rune");
                host.transform.SetParent(transform, false);
                host.transform.position = position;

                RuneDrawing drawing = new RuneDrawing(host, settings.material != null ? settings.material : FlatMaterial.Shared);
                drawing.AddRune(RuneGlyphs.Rune(runeIndex++), Vector3.zero, Vector3.forward, settings.runeSize,
                                settings.lineWidth, 0f, 1f);
                drawing.Progress = 0f;
                drawing.SetColour(settings.colour);

                Vector3 drift = UnityEngine.Random.onUnitSphere;
                drift.y *= 0.3f;

                runes.Add(new Rune
                {
                    transform = host.transform,
                    drawing = drawing,
                    anchor = position,
                    drift = drift.normalized * UnityEngine.Random.Range(settings.drift.x, settings.drift.y),
                    roll = UnityEngine.Random.Range(-30f, 30f),
                    phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                });
            }
        }

        private void UpdateRunes(float deltaTime)
        {
            Quaternion facing = RuneDrawing.FacingCamera();
            float harmlessFrom = settings.lifetime - settings.fadeTime;

            // The player's body, read once for every rune.
            bool canHit = player != null && !player.IsDead && playerBody != null && playerBody.enabled;
            Bounds body = canHit ? playerBody.bounds : default;

            for (int i = runes.Count - 1; i >= 0; i--)
            {
                Rune rune = runes[i];
                rune.age += deltaTime;

                if (rune.age >= settings.lifetime)
                {
                    Remove(i);
                    continue;
                }

                rune.anchor += rune.drift * deltaTime;
                float wave = rune.phase + rune.age * settings.bobRate * Mathf.PI * 2f;
                Vector3 position = rune.anchor + Vector3.up * (Mathf.Sin(wave) * settings.bobHeight);

                if (rune.age < harmlessFrom)
                {
                    if (ParryBarrier.TryCatch(position, position, settings.radius, Vector3.zero))
                    {
                        Pop(i);
                        continue;
                    }

                    if (canHit && (body.ClosestPoint(position) - position).sqrMagnitude <= settings.radius * settings.radius)
                    {
                        player.TakeDamage(settings.damage, position);
                        Pop(i);
                        canHit = !player.IsDead;
                        continue;
                    }
                }

                // The drawing faces the camera along its local up, so a roll
                // about that tilts it on screen.
                float rock = Mathf.Sin(wave * 0.7f) * settings.sway;
                rune.transform.SetPositionAndRotation(position,
                                                      facing * Quaternion.AngleAxis(rune.roll + rock, Vector3.up));
                rune.drawing.Progress = rune.age / settings.writeTime;
                rune.drawing.SetFade(1f - Mathf.InverseLerp(harmlessFrom, settings.lifetime, rune.age));
            }
        }

        private void Pop(int index)
        {
            EnergyBurst.Create(runes[index].transform.position, settings.pop);
            Remove(index);
        }

        private void Remove(int index)
        {
            runes[index].drawing.Release();
            Destroy(runes[index].transform.gameObject);
            runes.RemoveAt(index);
        }

        private void OnDestroy()
        {
            foreach (Rune rune in runes)
            {
                rune.drawing.Release();
            }
        }
    }
}
