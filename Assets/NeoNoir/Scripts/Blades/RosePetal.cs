using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// One rose petal shed along Rose's dash. Bursts off the line, drifts and
    /// tumbles for a while, then dissolves the same way the blades do.
    /// </summary>
    /// <remarks>
    /// Purely visual: petals never deal damage.
    ///
    /// The fade drives <c>_Fade</c> on the material through a property block,
    /// which on NeoNoir/Flat is the dithered dissolve the blades use when they
    /// rotate out. A material without <c>_Fade</c> shrinks the petal away
    /// instead.
    ///
    /// Spawned at runtime and builds its own mesh, so it needs no prefab. The
    /// mesh is double sided, since a tumbling petal shows both faces and the
    /// rose material culls back faces.
    /// </remarks>
    public sealed class RosePetal : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Distance between petals along the line they are shed from.")]
            [Min(0.05f)] public float spacing = 0.25f;

            [Tooltip("Smallest and largest petal length.")]
            public Vector2 size = new Vector2(0.12f, 0.22f);

            [Tooltip("How far from the dash line a petal may start.")]
            [Min(0f)] public float scatter = 0.35f;

            [Tooltip("Speed petals burst outward from the line at.")]
            [Min(0f)] public float burstSpeed = 1.5f;

            [Tooltip("Speed petals are carried on along the dash direction.")]
            public float carrySpeed = 2f;

            [Tooltip("How quickly the burst and carry die away. Higher settles sooner.")]
            [Min(0f)] public float drag = 4f;

            [Tooltip("Speed petals sink at once the burst has died away.")]
            [Min(0f)] public float fallSpeed = 0.25f;

            [Tooltip("Distance petals sway side to side as they drift.")]
            [Min(0f)] public float flutter = 0.15f;

            [Tooltip("Sways per second.")]
            [Min(0f)] public float flutterRate = 1.2f;

            [Tooltip("Most degrees per second a petal tumbles at. Each picks a rate up to this.")]
            [Min(0f)] public float spin = 240f;

            [Tooltip("Seconds a petal floats before it starts to fade.")]
            [Min(0f)] public float floatTime = 0.8f;

            [Tooltip("Random extra seconds added to each petal's float time, so they do not all go at once.")]
            [Min(0f)] public float floatJitter = 0.6f;

            [Tooltip("Seconds the fade takes.")]
            [Min(0.01f)] public float fadeTime = 0.8f;
        }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private static Mesh sharedMesh;

        private Settings settings;
        private MeshRenderer meshRenderer;
        private MaterialPropertyBlock fadeBlock;
        private bool hasFade;

        private Vector3 velocity;
        private Vector3 swayDirection;
        private Vector3 spinAxis;
        private float spinRate;
        private float swayPhase;
        private float baseScale;
        private float lifetime;
        private float age;

        /// <summary>
        /// Sheds a petal near <paramref name="origin"/>, carried on along
        /// <paramref name="forward"/>.
        /// </summary>
        public static RosePetal Spawn(Vector3 origin, Vector3 forward, Settings settings, Material material)
        {
            GameObject host = new GameObject("RosePetal");
            RosePetal petal = host.AddComponent<RosePetal>();
            petal.Shed(origin, forward, settings, material);
            return petal;
        }

        private void Shed(Vector3 origin, Vector3 forward, Settings shedSettings, Material material)
        {
            settings = shedSettings;

            // Flattened, so petals spread out to the sides of the line more
            // than above and below it.
            Vector3 offset = Random.insideUnitSphere * settings.scatter;
            offset.y *= 0.5f;

            Vector3 outward = offset.sqrMagnitude > 0.000001f ? offset.normalized : Random.onUnitSphere;

            velocity = outward * (settings.burstSpeed * Random.Range(0.5f, 1f))
                     + forward * (settings.carrySpeed * Random.Range(0.5f, 1f))
                     + Vector3.up * (settings.burstSpeed * Random.Range(0f, 0.5f));

            Vector3 side = Vector3.Cross(Vector3.up, Random.onUnitSphere);
            swayDirection = side.sqrMagnitude > 0.000001f ? side.normalized : Vector3.right;
            swayPhase = Random.Range(0f, Mathf.PI * 2f);

            spinAxis = Random.onUnitSphere;
            spinRate = settings.spin * Random.Range(0.3f, 1f);

            baseScale = Random.Range(settings.size.x, Mathf.Max(settings.size.x, settings.size.y));
            lifetime = settings.floatTime + Random.Range(0f, settings.floatJitter) + settings.fadeTime;

            transform.SetPositionAndRotation(origin + offset, Random.rotation);
            transform.localScale = Vector3.one * baseScale;

            gameObject.AddComponent<MeshFilter>().sharedMesh = PetalMesh();

            meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
                hasFade = material.HasProperty(FadeId);
            }

            fadeBlock = new MaterialPropertyBlock();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }

            // The burst eases into a slow sink rather than stopping dead.
            Vector3 settled = Vector3.down * settings.fallSpeed;
            velocity = Vector3.Lerp(velocity, settled, 1f - Mathf.Exp(-settings.drag * deltaTime));

            // Moved by the sway's rate of change, so its amplitude is exactly
            // the flutter distance.
            float rate = settings.flutterRate * Mathf.PI * 2f;
            Vector3 sway = swayDirection * (settings.flutter * rate * Mathf.Cos(swayPhase + rate * age));

            transform.position += (velocity + sway) * deltaTime;
            transform.rotation = Quaternion.AngleAxis(spinRate * deltaTime, spinAxis) * transform.rotation;

            float fadeStart = lifetime - settings.fadeTime;
            float visibility = 1f - Mathf.Clamp01((age - fadeStart) / settings.fadeTime);
            ApplyVisibility(visibility);
        }

        private void ApplyVisibility(float visibility)
        {
            if (!hasFade)
            {
                transform.localScale = Vector3.one * (baseScale * visibility);
                return;
            }

            // No block until the fade starts: a renderer with a property
            // block drops out of SRP batching.
            if (visibility >= 1f)
            {
                return;
            }

            fadeBlock.SetFloat(FadeId, visibility);
            meshRenderer.SetPropertyBlock(fadeBlock);
        }

        /// <summary>
        /// A unit-length petal along local Z, centred on the pivot: narrow at
        /// the base, broad and rounded toward the top, cupped upward across
        /// its width. Built once and shared by every petal.
        /// </summary>
        private static Mesh PetalMesh()
        {
            if (sharedMesh != null)
            {
                return sharedMesh;
            }

            const int rows = 8;
            const float cup = 0.12f;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            // Each face gets its own vertices, so the two sides keep separate
            // normals.
            for (int face = 0; face < 2; face++)
            {
                int first = vertices.Count;

                for (int row = 0; row < rows; row++)
                {
                    float t = row / (rows - 1f);
                    // Clamped: sin(PI) in floats comes out a hair below
                    // zero, and the square root of that is NaN.
                    float half = 0.32f * Mathf.Sqrt(Mathf.Max(0f, Mathf.Sin(Mathf.PI * t))) * (0.55f + 0.45f * t);
                    float z = t - 0.5f;

                    vertices.Add(new Vector3(-half, cup, z));
                    vertices.Add(new Vector3(0f, 0f, z));
                    vertices.Add(new Vector3(half, cup, z));
                }

                for (int row = 0; row < rows - 1; row++)
                {
                    for (int column = 0; column < 2; column++)
                    {
                        int a = first + row * 3 + column;
                        int b = a + 1;
                        int c = a + 3;
                        int d = a + 4;

                        if (face == 0)
                        {
                            triangles.AddRange(new[] { a, c, b, b, c, d });
                        }
                        else
                        {
                            triangles.AddRange(new[] { a, b, c, b, d, c });
                        }
                    }
                }
            }

            sharedMesh = new Mesh { name = "Rose petal" };
            sharedMesh.SetVertices(vertices);
            sharedMesh.SetTriangles(triangles, 0);
            sharedMesh.RecalculateNormals();
            sharedMesh.RecalculateBounds();
            return sharedMesh;
        }
    }
}
