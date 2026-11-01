using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// Streaks of light rushing in to a point from all round, as energy
    /// gathering into a charge.
    /// </summary>
    /// <remarks>
    /// While <see cref="Rate"/> is above 0, streaks appear that many a second,
    /// <see cref="Settings.reach"/> out from the point, any way above
    /// it, and fly in to it over
    /// <see cref="Settings.travelTime"/>, speeding up, each
    /// <see cref="Settings.length"/> long and shrinking to nothing as it
    /// arrives. <see cref="Stop"/> lets those out finish, then it goes.
    ///
    /// Every streak is drawn in one mesh, a cross of two thin strips each, on
    /// the shared NeoNoir/Flat material, so however many there are they cost
    /// one draw. All share one colour; with <see cref="Settings.saturation"/>
    /// above 0 it runs round the rainbow. Spawned at runtime, so it needs no
    /// prefab.
    /// </remarks>
    public sealed class ChargeLines : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("How far out the streaks appear.")]
            [Min(0.1f)] public float reach = 7f;

            [Tooltip("Seconds a streak takes to fly in.")]
            [Min(0.02f)] public float travelTime = 0.4f;

            [Tooltip("Length of a streak as it sets off.")]
            [Min(0.01f)] public float length = 2.5f;

            [Tooltip("Thickness of a streak.")]
            [Min(0.005f)] public float width = 0.06f;

            [Tooltip("Saturation of the streaks' colour, which runs round the rainbow. 0 keeps them white.")]
            [Range(0f, 1f)] public float saturation = 0f;

            [Tooltip("Brightness of the streaks. Above 1 blooms.")]
            [Min(0f)] public float intensity = 4f;
        }

        private const int VerticesPerStreak = 8;
        private const int IndicesPerStreak = 12;

        /// <summary>Turns of the rainbow a second, when the streaks have colour.</summary>
        private const float HueSpeed = 0.5f;

        private struct Streak
        {
            public Vector3 from;
            public float age;
        }

        private readonly List<Streak> streaks = new List<Streak>();

        private Settings settings;
        private Vector3 point;
        private float owed;
        private bool stopped;

        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock block;
        private Vector3[] vertices = new Vector3[0];
        private Vector3[] normals = new Vector3[0];
        private int capacity;

        /// <summary>Streaks a second. 0 sends none.</summary>
        public float Rate { get; set; }

        /// <summary>Starts streaks rushing in to <paramref name="target"/>, at <paramref name="rate"/> a second.</summary>
        public static ChargeLines Create(Vector3 target, float rate, Settings settings)
        {
            ChargeLines lines = new GameObject("ChargeLines").AddComponent<ChargeLines>();
            lines.settings = settings;
            lines.point = target;
            lines.Rate = rate;
            lines.Build();
            return lines;
        }

        /// <summary>No more streaks; those out finish, then it goes.</summary>
        public void Stop()
        {
            stopped = true;
        }

        private void Build()
        {
            mesh = new Mesh { name = "ChargeLines" };
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.sharedMaterial = FlatMaterial.DoubleSided;
            block = new MaterialPropertyBlock();
            Tint();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;

            if (!stopped && Rate > 0f)
            {
                for (owed += Rate * deltaTime; owed >= 1f; owed -= 1f)
                {
                    // From above the floor's side, never up out of it.
                    Vector3 direction = Random.onUnitSphere;
                    direction.y = Mathf.Abs(direction.y);
                    streaks.Add(new Streak { from = point + direction * settings.reach });
                }
            }

            for (int i = streaks.Count - 1; i >= 0; i--)
            {
                Streak streak = streaks[i];
                streak.age += deltaTime;

                if (streak.age >= settings.travelTime)
                {
                    // Order does not matter: swap the last in.
                    streaks[i] = streaks[streaks.Count - 1];
                    streaks.RemoveAt(streaks.Count - 1);
                    continue;
                }

                streaks[i] = streak;
            }

            if (stopped && streaks.Count == 0)
            {
                Destroy(gameObject);
                return;
            }

            if (settings.saturation > 0f)
            {
                Tint();
            }

            Shape();
        }

        private void Tint()
        {
            if (meshRenderer.sharedMaterial == null)
            {
                return;
            }

            float hue = Mathf.Repeat(Time.time * HueSpeed, 1f);
            Color colour = Color.HSVToRGB(hue, settings.saturation, 1f) * settings.intensity;
            colour.a = 1f;
            FlatMaterial.Tint(meshRenderer, colour, block);
        }

        private void Shape()
        {
            if (streaks.Count > capacity)
            {
                Grow(Mathf.Max(streaks.Count, capacity * 2, 32));
            }

            float half = settings.width * 0.5f;

            for (int i = 0; i < capacity; i++)
            {
                int at = i * VerticesPerStreak;

                if (i >= streaks.Count)
                {
                    // Unused: folded to nothing.
                    for (int v = 0; v < VerticesPerStreak; v++)
                    {
                        vertices[at + v] = point;
                    }

                    continue;
                }

                Streak streak = streaks[i];
                float t = streak.age / settings.travelTime;

                // Speeding up all the way in, shrinking as it goes.
                Vector3 inward = (point - streak.from).normalized;
                Vector3 head = Vector3.Lerp(streak.from, point, t * t);
                Vector3 tail = head - inward * (settings.length * (1f - t));

                Vector3 side = Vector3.Cross(inward, Mathf.Abs(inward.y) > 0.9f ? Vector3.forward : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, inward);

                vertices[at] = tail - side * half;
                vertices[at + 1] = tail + side * half;
                vertices[at + 2] = head + side * half;
                vertices[at + 3] = head - side * half;
                vertices[at + 4] = tail - up * half;
                vertices[at + 5] = tail + up * half;
                vertices[at + 6] = head + up * half;
                vertices[at + 7] = head - up * half;

                for (int v = 0; v < 4; v++)
                {
                    normals[at + v] = up;
                    normals[at + 4 + v] = side;
                }
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
        }

        private void Grow(int size)
        {
            capacity = size;
            vertices = new Vector3[size * VerticesPerStreak];
            normals = new Vector3[size * VerticesPerStreak];
            int[] indices = new int[size * IndicesPerStreak];

            for (int i = 0; i < size; i++)
            {
                int v = i * VerticesPerStreak;
                int at = i * IndicesPerStreak;

                for (int strip = 0; strip < 2; strip++)
                {
                    int corner = v + strip * 4;
                    indices[at++] = corner;
                    indices[at++] = corner + 1;
                    indices[at++] = corner + 2;
                    indices[at++] = corner;
                    indices[at++] = corner + 2;
                    indices[at++] = corner + 3;
                }
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.triangles = indices;

            // Never further out than where they start, so never recalculated.
            mesh.bounds = new Bounds(point, Vector3.one * ((settings.reach + settings.length) * 2f));
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }
    }
}
