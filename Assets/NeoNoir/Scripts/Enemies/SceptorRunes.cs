using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Runes spewing out of a sceptre's tip while it casts: each written as
    /// it leaves, drifting off and slowing, then dissolving away.
    /// </summary>
    /// <remarks>
    /// Spews more thickly as the cast builds over the time given to
    /// <see cref="Create"/>, and <see cref="Release"/> throws out a last
    /// burst and stops it. Runes already out are left to dissolve before it
    /// destroys itself, so the effect never cuts off. If the point, or the
    /// enemy it was made for, goes away, dies or is disabled, or the cast
    /// runs past its time without a release, it stops the same way.
    ///
    /// Not parented to the point, and never moved itself: each rune leaves
    /// from wherever the tip is, read in LateUpdate after the animator has
    /// posed it, then drifts on its own however the sceptre swings. The runes cycle through
    /// <see cref="RuneGlyphs"/> and face the camera. They fade by dithered
    /// dissolve, which this game's two-tone look needs in place of
    /// transparency.
    /// </remarks>
    public sealed class SceptorRunes : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Runes a second by the end of the cast. They start at a third of this. 0 spews none.")]
            [Min(0f)] public float runesPerSecond = 12f;

            [Tooltip("Runes thrown out at once on the release.")]
            [Range(0, 32)] public int releaseBurst = 8;

            [Tooltip("Slowest and fastest a rune leaves, in metres per second. The release burst goes half again as fast.")]
            public Vector2 speed = new Vector2(1.5f, 3.5f);

            [Tooltip("How quickly the runes slow once out. Higher stops them sooner.")]
            [Min(0f)] public float drag = 2f;

            [Tooltip("How much the runes lean upward as they leave, 0 every way to 1 mostly up.")]
            [Range(0f, 1f)] public float upwardBias = 0.5f;

            [Tooltip("Shortest and longest seconds a rune lasts.")]
            public Vector2 life = new Vector2(0.8f, 1.4f);

            [Tooltip("When a rune starts to dissolve, as a fraction of its life.")]
            [Range(0f, 1f)] public float fadeFrom = 0.35f;

            [Tooltip("Seconds each rune takes to be written as it leaves.")]
            [Min(0.01f)] public float writeTime = 0.12f;

            [Tooltip("Height of each rune, in metres.")]
            [Min(0.05f)] public float runeSize = 0.35f;

            [Tooltip("Width of the runes' lines, in metres.")]
            [Min(0.01f)] public float lineWidth = 0.04f;

            [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 0.15f, 0.1f, 1f);

            [Tooltip("Material to draw with. Empty uses a shared NeoNoir/Flat one. Needs a _Fade property to dissolve.")]
            public Material material;
        }

        private sealed class Rune
        {
            public Transform transform;
            public RuneDrawing drawing;
            public Vector3 velocity;
            public float age;
            public float life;
            public float roll;
        }

        private readonly List<Rune> runes = new List<Rune>();

        private Settings settings;
        private Transform point;
        private Enemy owner;
        private Vector3 origin;
        private float castTime;
        private float age;
        private float owed;
        private int runeIndex;
        private bool spewing = true;

        /// <summary>
        /// Starts spewing from <paramref name="sceptrePoint"/>, building to
        /// full over <paramref name="seconds"/>. Stops if
        /// <paramref name="enemy"/> dies.
        /// </summary>
        public static SceptorRunes Create(Transform sceptrePoint, Enemy enemy, float seconds, Settings settings)
        {
            GameObject host = new GameObject("SceptorRunes");
            host.transform.position = sceptrePoint.position;

            SceptorRunes spew = host.AddComponent<SceptorRunes>();
            spew.settings = settings;
            spew.point = sceptrePoint;
            spew.owner = enemy;
            spew.origin = sceptrePoint.position;
            spew.castTime = Mathf.Max(0.01f, seconds);
            return spew;
        }

        /// <summary>Throws out the release burst and stops spewing. Destroys itself once the last rune is gone.</summary>
        public void Release()
        {
            if (!spewing)
            {
                return;
            }

            for (int i = 0; i < settings.releaseBurst; i++)
            {
                Spew(1.5f);
            }

            spewing = false;
        }

        private void LateUpdate()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            // Also stops if the cast was cut off without a release -- the
            // enemy disabled, or its attack stopped -- once it is overdue.
            if (point == null || owner == null || owner.Health.IsDead || !owner.isActiveAndEnabled
                || age > castTime + 0.5f)
            {
                spewing = false;
            }
            else
            {
                origin = point.position;
            }

            if (spewing)
            {
                owed += settings.runesPerSecond * Mathf.Lerp(0.33f, 1f, Mathf.Clamp01(age / castTime)) * deltaTime;

                while (owed >= 1f)
                {
                    owed -= 1f;
                    Spew(1f);
                }
            }

            UpdateRunes(deltaTime);

            if (!spewing && runes.Count == 0)
            {
                Destroy(gameObject);
            }
        }

        private void Spew(float speedScale)
        {
            Vector3 direction = (UnityEngine.Random.onUnitSphere + Vector3.up * settings.upwardBias).normalized;

            GameObject host = new GameObject("Rune");
            host.transform.SetParent(transform, false);
            host.transform.position = origin;

            RuneDrawing drawing = new RuneDrawing(host, settings.material != null ? settings.material : FlatMaterial.Shared);
            drawing.AddRune(RuneGlyphs.Rune(runeIndex++), Vector3.zero, Vector3.forward, settings.runeSize,
                            settings.lineWidth, 0f, 1f);
            drawing.Progress = 0f;
            drawing.SetColour(settings.colour);

            runes.Add(new Rune
            {
                transform = host.transform,
                drawing = drawing,
                velocity = direction * (UnityEngine.Random.Range(settings.speed.x, settings.speed.y) * speedScale),
                life = UnityEngine.Random.Range(settings.life.x, settings.life.y),
                roll = UnityEngine.Random.Range(-30f, 30f),
            });
        }

        private void UpdateRunes(float deltaTime)
        {
            Quaternion facing = RuneDrawing.FacingCamera();
            float drag = Mathf.Exp(-settings.drag * deltaTime);

            for (int i = runes.Count - 1; i >= 0; i--)
            {
                Rune rune = runes[i];
                rune.age += deltaTime;

                if (rune.age >= rune.life)
                {
                    rune.drawing.Release();
                    Destroy(rune.transform.gameObject);
                    runes.RemoveAt(i);
                    continue;
                }

                rune.velocity *= drag;
                Vector3 position = rune.transform.position + rune.velocity * deltaTime;

                // The drawing faces the camera along its local up, so a
                // roll about that tilts it on screen.
                rune.transform.SetPositionAndRotation(position, facing * Quaternion.AngleAxis(rune.roll, Vector3.up));

                rune.drawing.Progress = rune.age / settings.writeTime;
                rune.drawing.SetFade(1f - Mathf.InverseLerp(settings.fadeFrom * rune.life, rune.life, rune.age));
            }
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
