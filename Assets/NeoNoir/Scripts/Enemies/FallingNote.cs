using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A double note forming in the sky over a spot on the floor, then
    /// dropping onto it to spill a pool of music that hurts the player
    /// standing in it.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. Forming: the note swells from nothing to full size
    ///      <see cref="Settings.formHeight"/> above the spot over
    ///      <see cref="Settings.formTime"/>, while a <see cref="WarningCircle"/>
    ///      the size of the pool starts filling on the floor under it.
    ///   2. It hangs there, rocking, for <see cref="Settings.hangTime"/>.
    ///   3. It drops, gathering speed, to land
    ///      <see cref="Settings.dropTime"/> later, just as the circle fills.
    ///   4. On landing: the circle goes, a small <see cref="EnergyBurst"/>
    ///      splashes, and a <see cref="PotionPool"/> spreads from the spot in
    ///      the <see cref="Settings.pool"/> settings. The note itself does no
    ///      damage; only the pool does.
    ///
    /// The note faces the camera throughout, like a sprite, and is taken to
    /// face -Z as modelled. Any colliders on it are removed. Without art, a
    /// plain ball in the pool's accent colour stands in.
    ///
    /// Made at runtime by <see cref="BlueSymphonyPattern"/>; needs no prefab of
    /// its own.
    /// </remarks>
    public sealed class FallingNote : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Fall")]
            [Tooltip("How high above the floor the note forms.")]
            [Min(0f)] public float formHeight = 8f;

            [Tooltip("Seconds the note takes to swell to full size.")]
            [Min(0.01f)] public float formTime = 0.5f;

            [Tooltip("Seconds it hangs in the sky once formed, before it drops.")]
            [Min(0f)] public float hangTime = 0.6f;

            [Tooltip("Seconds the drop takes. It gathers speed all the way down.")]
            [Min(0.01f)] public float dropTime = 0.4f;

            [Tooltip("Uniform scale applied to the note art.")]
            [Min(0.01f)] public float scale = 1.5f;

            [Tooltip("Degrees the note rocks side to side while it hangs and falls.")]
            [Range(0f, 45f)] public float rock = 15f;

            [Header("Warning")]
            [Tooltip("Width of the warning circle's outline. The circle is the size of the pool.")]
            [Min(0f)] public float warningOutline = 0.08f;

            [Tooltip("Colour of the warning circle.")]
            public Color warningColour = new Color(0.1f, 0.45f, 1f);

            [Tooltip("Height above the floor the warning circle is drawn at.")]
            [Range(0f, 0.5f)] public float warningHeight = 0.05f;

            [Header("Landing")]
            [Tooltip("Burst where the note lands.")]
            public EnergyBurst.Settings splash = new EnergyBurst.Settings
            {
                flashSize = 1f,
                flashTime = 0.15f,
                sparkCount = 10,
                sparkSize = 0.2f,
                sparkSpeed = new Vector2(3f, 7f),
                sparkLife = 0.3f,
                colour = new Color(0.3f, 1.2f, 3.5f, 1f),
            };

            [Tooltip("The pool of music it spills. Its Max Radius is also the warning circle's.")]
            public PotionPool.Settings pool = new PotionPool.Settings
            {
                maxRadius = 2f,
                spreadTime = 0.4f,
                duration = 5f,
                shrinkTime = 0.6f,
                damagePerSecond = 5f,
                tickInterval = 0.25f,
                colour = new Color(0.04f, 0.2f, 1f, 1f),
                accentColour = new Color(0.3f, 1.2f, 3.5f, 1f),
                rimWidth = 0.15f,
                bubblesPerSecond = 3f,
                bubbleSize = new Vector2(0.12f, 0.25f),
                fumesPerSecond = 4f,
                fumeSize = 0.35f,
                fumeRiseSpeed = new Vector2(0.6f, 1.2f),
                fumeLife = 1.4f,
            };
        }

        private static readonly List<FallingNote> live = new List<FallingNote>();

        /// <summary>Every note forming or falling right now, not yet landed.</summary>
        public static IReadOnlyList<FallingNote> Live => live;

        /// <summary>The spot on the floor it will land on.</summary>
        public Vector3 Floor => floor;

        /// <summary>Radius of the pool it will spill.</summary>
        public float PoolRadius => settings.pool.maxRadius;

        private Settings settings;
        private Vector3 floor;
        private Transform art;
        private Vector3 baseScale;
        private WarningCircle circle;
        private float age;
        private float phase;

        /// <summary>Forms <paramref name="noteArt"/>, a prefab, over <paramref name="floorPoint"/> and drops it there. Null art makes a plain ball.</summary>
        public static FallingNote Create(GameObject noteArt, Vector3 floorPoint, Settings settings)
        {
            GameObject host = new GameObject("FallingNote");
            host.transform.position = floorPoint + Vector3.up * settings.formHeight;

            FallingNote note = host.AddComponent<FallingNote>();
            note.Begin(noteArt, floorPoint, settings);
            return note;
        }

        private void Begin(GameObject noteArt, Vector3 floorPoint, Settings noteSettings)
        {
            settings = noteSettings;
            floor = floorPoint;
            live.Add(this);
            phase = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            GameObject piece;

            if (noteArt != null)
            {
                piece = Instantiate(noteArt, transform);
                baseScale = noteArt.transform.localScale * settings.scale;
            }
            else
            {
                piece = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                piece.transform.SetParent(transform, false);

                MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = FlatMaterial.Shared;
                FlatMaterial.Tint(renderer, settings.pool.accentColour, new MaterialPropertyBlock());
                baseScale = Vector3.one * (0.5f * settings.scale);
            }

            foreach (Collider part in piece.GetComponentsInChildren<Collider>())
            {
                Destroy(part);
            }

            foreach (Renderer part in piece.GetComponentsInChildren<Renderer>())
            {
                part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                part.receiveShadows = false;
            }

            art = piece.transform;
            art.localPosition = Vector3.zero;
            art.localScale = Vector3.zero;

            circle = WarningCircle.Create(floor + Vector3.up * settings.warningHeight, settings.pool.maxRadius,
                                          settings.warningOutline, null, settings.warningColour);
        }

        private void Update()
        {
            age += Time.deltaTime;

            float dropStart = settings.formTime + settings.hangTime;
            float end = dropStart + settings.dropTime;

            if (age >= end)
            {
                Land();
                return;
            }

            float swell = Mathf.Clamp01(age / settings.formTime);
            swell = 1f - (1f - swell) * (1f - swell);
            art.localScale = baseScale * swell;

            // Gathering speed: the square of the time into the drop.
            float fall = Mathf.Clamp01((age - dropStart) / settings.dropTime);
            transform.position = floor + Vector3.up * (settings.formHeight * (1f - fall * fall));

            circle.Fill = age / end;
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;

            if (camera != null && art != null)
            {
                float rock = Mathf.Sin(phase + age * Mathf.PI * 2f) * settings.rock;
                art.rotation = camera.transform.rotation * Quaternion.Euler(0f, 0f, rock);
            }
        }

        private void Land()
        {
            live.Remove(this);
            circle.Release();
            circle = null;

            EnergyBurst.Create(floor, settings.splash);
            PotionPool.Create(floor, settings.pool);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            live.Remove(this);

            // Gone before landing, with a scene unloading.
            if (circle != null)
            {
                circle.Release();
            }
        }
    }
}
