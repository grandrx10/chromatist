using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// A burst of white sparks thrown off a parried projectile: a handful of
    /// thin streaks that spray away from the barrier, slow, and thin out.
    /// </summary>
    /// <remarks>
    /// Purely visual. Built as one mesh of camera-facing quads rewritten every
    /// frame, so it needs no prefab and no particle system to tune.
    ///
    /// The streaks never fade through grey, which the threshold would crush
    /// unpredictably. Each one only gets narrower until it is gone, so every
    /// pixel drawn is full colour. Meant for NeoNoir/Flat with culling off
    /// (NN_Spark): the material decides the colour, and above 1.0 it blooms.
    /// </remarks>
    public sealed class ParrySpark : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Sparks thrown per parried projectile.")]
            [Range(1, 32)] public int count = 8;

            [Tooltip("Degrees the sparks spread out around the direction the projectile glanced off in.")]
            [Range(0f, 180f)] public float spread = 60f;

            [Tooltip("Slowest and fastest launch speed.")]
            public Vector2 speed = new Vector2(5f, 11f);

            [Tooltip("Shortest and longest life of a spark, in seconds.")]
            public Vector2 lifetime = new Vector2(0.12f, 0.28f);

            [Tooltip("How quickly the sparks bleed speed. Higher stops them sooner.")]
            [Min(0f)] public float drag = 6f;

            [Tooltip("Downward pull on the sparks.")]
            [Min(0f)] public float gravity = 6f;

            [Tooltip("Seconds of velocity each streak trails behind it. Longer is a longer streak.")]
            [Min(0f)] public float streak = 0.03f;

            [Tooltip("Width of a streak when it is thrown. It narrows to nothing over its life.")]
            [Min(0.001f)] public float width = 0.05f;
        }

        private Settings settings;
        private Mesh mesh;

        private Vector3[] positions;
        private Vector3[] velocities;
        private float[] lifetimes;
        private Vector3[] vertices;
        private float age;

        /// <summary>Throws a burst of sparks from <paramref name="point"/>, centred on <paramref name="direction"/>.</summary>
        public static ParrySpark Spawn(Vector3 point, Vector3 direction, Settings settings, Material material)
        {
            GameObject host = new GameObject("ParrySpark");
            ParrySpark spark = host.AddComponent<ParrySpark>();
            spark.Throw(point, direction, settings, material);
            return spark;
        }

        private void Throw(Vector3 point, Vector3 direction, Settings throwSettings, Material material)
        {
            settings = throwSettings;

            int count = Mathf.Max(1, settings.count);
            positions = new Vector3[count];
            velocities = new Vector3[count];
            lifetimes = new float[count];
            vertices = new Vector3[count * 4];

            Vector3 forward = direction.sqrMagnitude > 0.000001f ? direction.normalized : Vector3.up;
            float halfSpread = settings.spread * 0.5f;

            for (int i = 0; i < count; i++)
            {
                // Uniform within the cone, not clumped at its centre.
                Vector3 axis = Vector3.Cross(forward, Random.onUnitSphere);
                axis = axis.sqrMagnitude > 0.000001f ? axis.normalized : Vector3.up;
                Vector3 heading = Quaternion.AngleAxis(Random.Range(-halfSpread, halfSpread), axis) * forward;

                positions[i] = point;
                velocities[i] = heading * Random.Range(settings.speed.x, Mathf.Max(settings.speed.x, settings.speed.y));
                lifetimes[i] = Random.Range(settings.lifetime.x, Mathf.Max(settings.lifetime.x, settings.lifetime.y));
            }

            int[] triangles = new int[count * 6];

            for (int i = 0; i < count; i++)
            {
                int v = i * 4;
                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }

            // The vertices are written in world space, so the object itself
            // stays at the origin with no rotation or scale.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            mesh = new Mesh { name = "ParrySpark" };
            mesh.MarkDynamic();
            mesh.vertices = vertices;
            mesh.triangles = triangles;

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            Rebuild();
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            bool alive = false;
            float decay = Mathf.Exp(-settings.drag * deltaTime);

            for (int i = 0; i < positions.Length; i++)
            {
                if (age >= lifetimes[i])
                {
                    continue;
                }

                alive = true;
                velocities[i] = velocities[i] * decay + Vector3.down * (settings.gravity * deltaTime);
                positions[i] += velocities[i] * deltaTime;
            }

            if (!alive)
            {
                Destroy(gameObject);
                return;
            }

            Rebuild();
        }

        private void Rebuild()
        {
            Camera camera = Camera.main;
            Vector3 viewForward = camera != null ? camera.transform.forward : Vector3.down;

            for (int i = 0; i < positions.Length; i++)
            {
                int v = i * 4;
                float life = Mathf.Clamp01(age / Mathf.Max(0.0001f, lifetimes[i]));

                // A spent spark collapses to nothing rather than being removed,
                // so the triangle list never has to change.
                if (life >= 1f)
                {
                    vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = positions[i];
                    continue;
                }

                Vector3 head = positions[i];
                Vector3 tail = head - velocities[i] * settings.streak;
                Vector3 along = head - tail;

                // Turned to face the camera about its own length, so a streak
                // never shows edge-on.
                Vector3 side = Vector3.Cross(along, viewForward);
                side = side.sqrMagnitude > 0.0000001f ? side.normalized : Vector3.right;
                side *= settings.width * 0.5f * (1f - life);

                vertices[v] = tail - side;
                vertices[v + 1] = tail + side;
                vertices[v + 2] = head - side;
                vertices[v + 3] = head + side;
            }

            mesh.vertices = vertices;
            mesh.RecalculateBounds();
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
