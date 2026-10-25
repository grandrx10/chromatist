using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// The burst when a <see cref="BlueMark"/> fills: music notes spray out
    /// from inside the enemy while a ring on the floor flares outward and
    /// dissolves.
    /// </summary>
    /// <remarks>
    /// Purely visual: the mark deals the damage.
    ///
    /// Notes are copies of the banjo's note prefabs, picked at random. Each
    /// leaves the enemy's centre on its own heading round the circle, tipped
    /// upward, slows as it goes, and shrinks away at the end. Like the notes
    /// in flight, they face the camera. They are bare copies from
    /// <see cref="NoteModels"/>, with no colliders.
    ///
    /// Spawned at runtime and builds its own ring mesh, so it needs no prefab.
    /// Removes itself, notes and all, once everything has finished.
    /// </remarks>
    public sealed class BlueBurst : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Notes that spray out.")]
            [Range(0, 32)] public int notes = 10;

            [Tooltip("Uniform scale applied to each burst note's prefab.")]
            [Min(0.01f)] public float noteScale = 0.7f;

            [Tooltip("Speed notes leave the enemy at.")]
            [Min(0f)] public float speed = 6f;

            [Tooltip("Most degrees above horizontal a note may leave at.")]
            [Range(0f, 80f)] public float lift = 45f;

            [Tooltip("How quickly notes slow down. Higher stops them sooner.")]
            [Min(0f)] public float drag = 3f;

            [Tooltip("Most degrees per second a note spins at, about the axis toward the camera.")]
            [Min(0f)] public float spin = 360f;

            [Tooltip("Seconds a note lives, the last third of it shrinking away.")]
            [Min(0.05f)] public float lifetime = 0.7f;

            [Tooltip("Seconds the floor ring takes to flare out and dissolve.")]
            [Min(0.05f)] public float ringTime = 0.3f;

            [Tooltip("How many times its starting size the floor ring grows to.")]
            [Min(1f)] public float ringGrowth = 1.8f;

            [Tooltip("Width of the floor ring's outline.")]
            [Min(0.01f)] public float ringThickness = 0.12f;

            [Tooltip("Colour intensity of the floor ring. 1 is the material's own colour; above 1 blooms.")]
            [Min(0f)] public float ringIntensity = 4f;

            [Tooltip("Degrees to turn the note models by, if they do not face -Z (toward the viewer) as modelled.")]
            public Vector3 modelRotation;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private sealed class Note
        {
            public Transform Transform;
            public Vector3 Position;
            public Vector3 Velocity;
            public Vector3 Scale;
            public float Spin;
            public float Angle;
        }

        private readonly List<Note> notes = new List<Note>();

        private Settings settings;
        private float age;

        private GameObject ring;
        private Mesh ringMesh;
        private MeshRenderer ringRenderer;
        private MaterialPropertyBlock block;
        private Color baseColour = Color.white;
        private bool hasFade;

        /// <summary>
        /// Plays a burst: notes from <paramref name="centre"/>, and a ring of
        /// <paramref name="radius"/> on the floor at <paramref name="floor"/>.
        /// </summary>
        public static BlueBurst Play(Vector3 centre, Vector3 floor, float radius, Settings settings, Material material,
                                     IReadOnlyList<GameObject> notePrefabs)
        {
            GameObject host = new GameObject("BlueBurst");
            BlueBurst burst = host.AddComponent<BlueBurst>();
            burst.Begin(centre, floor, radius, settings, material, notePrefabs);
            return burst;
        }

        private void Begin(Vector3 centre, Vector3 floor, float radius, Settings burstSettings, Material material,
                           IReadOnlyList<GameObject> notePrefabs)
        {
            settings = burstSettings;
            transform.position = floor;

            BuildRing(radius, material);

            if (notePrefabs == null || notePrefabs.Count == 0)
            {
                return;
            }

            // Evenly round the circle with a little jitter, so the spray is
            // full without looking stamped out.
            float offset = Random.Range(0f, 360f);

            for (int i = 0; i < settings.notes; i++)
            {
                GameObject prefab = notePrefabs[Random.Range(0, notePrefabs.Count)];

                if (prefab == null)
                {
                    continue;
                }

                float yaw = offset + 360f * i / settings.notes + Random.Range(-12f, 12f);
                float pitch = Random.Range(settings.lift * 0.25f, settings.lift);
                Vector3 heading = Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;

                GameObject made = NoteModels.Spawn(prefab);
                made.name = "BlueBurstNote";
                made.transform.position = centre;

                notes.Add(new Note
                {
                    Transform = made.transform,
                    Position = centre,
                    Velocity = heading * (settings.speed * Random.Range(0.7f, 1f)),
                    Scale = made.transform.localScale * settings.noteScale,
                    Spin = Random.Range(-settings.spin, settings.spin),
                    Angle = Random.Range(-20f, 20f),
                });
            }
        }

        private void BuildRing(float radius, Material material)
        {
            ring = new GameObject("BurstRing");
            ring.transform.SetParent(transform, false);

            ringMesh = BlueMark.AnnulusMesh(radius - settings.ringThickness * 0.5f, radius + settings.ringThickness * 0.5f, 48);
            ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;

            ringRenderer = ring.AddComponent<MeshRenderer>();
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;

            if (material != null)
            {
                ringRenderer.sharedMaterial = material;
                hasFade = material.HasProperty(FadeId);

                if (material.HasProperty(BaseColorId))
                {
                    baseColour = material.GetColor(BaseColorId);
                }
            }

            block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (age >= Mathf.Max(settings.lifetime, settings.ringTime))
            {
                Destroy(gameObject);
                return;
            }

            UpdateRing();
            UpdateNotes(deltaTime);
        }

        private void UpdateRing()
        {
            if (ring == null)
            {
                return;
            }

            float t = Mathf.Clamp01(age / settings.ringTime);
            ring.SetActive(t < 1f);

            // Ease-out: flung outward, then drifting as it dissolves.
            float eased = 1f - (1f - t) * (1f - t);
            float scale = Mathf.Lerp(1f, settings.ringGrowth, eased);
            float visibility = 1f - t;

            if (!hasFade)
            {
                scale *= visibility;
            }

            ring.transform.localScale = new Vector3(scale, 1f, scale);

            Color colour = baseColour * settings.ringIntensity;
            colour.a = baseColour.a;

            block.SetColor(BaseColorId, colour);
            block.SetFloat(FadeId, visibility);
            ringRenderer.SetPropertyBlock(block);
        }

        private void UpdateNotes(float deltaTime)
        {
            Camera camera = Camera.main;
            Quaternion facing = camera != null ? camera.transform.rotation : Quaternion.identity;

            float decay = Mathf.Exp(-settings.drag * deltaTime);
            float shrinkStart = settings.lifetime * 2f / 3f;
            float size = 1f - Mathf.Clamp01((age - shrinkStart) / (settings.lifetime - shrinkStart));

            foreach (Note note in notes)
            {
                if (note.Transform == null)
                {
                    continue;
                }

                note.Position += note.Velocity * deltaTime;
                note.Velocity *= decay;
                note.Angle += note.Spin * deltaTime;

                note.Transform.SetPositionAndRotation(
                    note.Position,
                    facing * Quaternion.Euler(0f, 0f, note.Angle) * Quaternion.Euler(settings.modelRotation));
                note.Transform.localScale = note.Scale * size;
            }
        }

        private void OnDestroy()
        {
            foreach (Note note in notes)
            {
                if (note.Transform != null)
                {
                    Destroy(note.Transform.gameObject);
                }
            }

            if (ringMesh != null)
            {
                Destroy(ringMesh);
            }
        }
    }
}
