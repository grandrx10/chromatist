using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Scarlett's hit effect: a burst of blood spewed out of the far side of
    /// whatever her claw cut, away from the player, in place of the straight
    /// slash streak other blades use.
    /// </summary>
    /// <remarks>
    /// Each droplet is thrown in a fan around the spray direction and flies
    /// under drag, so it bursts out fast and slows. It is drawn as a streak
    /// from where it was a moment ago to where it is now, so fast droplets
    /// stretch into spikes and settle into beads as they slow, then shrink
    /// away over their life.
    ///
    /// Drawn with the claw's own material: each droplet through NeoNoir/Tear's
    /// solid branch (uv.x = 2) in the blood colour, with a white outline like
    /// the tear's. Lifted toward the camera the same way the claw is, and
    /// sorted after it, so it draws over the claw and the enemy both.
    ///
    /// Spawned at runtime and builds its own mesh, so it needs no prefab.
    /// </remarks>
    public sealed class ScarlettBlood : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Droplets thrown per hit. 0 turns the blood off.")]
            [Range(0, 40)] public int droplets = 12;

            [Tooltip("Degrees the spray fans across.")]
            [Range(0f, 180f)] public float spread = 50f;

            [Tooltip("Slowest a droplet leaves at, in units per second.")]
            [Min(0f)] public float minSpeed = 5f;

            [Tooltip("Fastest a droplet leaves at, in units per second.")]
            [Min(0f)] public float maxSpeed = 14f;

            [Tooltip("How quickly droplets slow down. Higher stops them sooner.")]
            [Min(0f)] public float drag = 7f;

            [Tooltip("Seconds a droplet lasts before it has shrunk away.")]
            [Min(0.05f)] public float lifetime = 0.45f;

            [Tooltip("Thickness of a droplet at its largest.")]
            [Min(0.01f)] public float size = 0.14f;

            [Tooltip("Seconds of travel each droplet's streak trails behind it. Longer stretches them into spikes.")]
            [Min(0f)] public float stretch = 0.05f;

            [Tooltip("Width of the outline round each droplet. 0 for none.")]
            [Min(0f)] public float outlineWidth = 0.04f;

            [Tooltip("Distance the blood is lifted toward the camera, so it draws over the enemy. Does not move it on screen.")]
            [Min(0f)] public float lift = 4f;
        }

        private struct Droplet
        {
            public Vector3 Direction;
            public float Speed;
            public float Size;
        }

        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        private readonly List<Vector3> loop = new List<Vector3>();

        private Settings settings;
        private Droplet[] droplets;
        private Vector3 origin;
        private float age;
        private Mesh mesh;

        /// <summary>
        /// Spews blood out of <paramref name="wound"/>, fanning around
        /// <paramref name="away"/> -- normally from the player through the
        /// enemy. Only the flat part of the direction is used.
        /// </summary>
        public static ScarlettBlood Spawn(Vector3 wound, Vector3 away, Settings settings, Material material)
        {
            if (settings == null || settings.droplets <= 0)
            {
                return null;
            }

            GameObject host = new GameObject("ScarlettBlood");
            ScarlettBlood blood = host.AddComponent<ScarlettBlood>();
            blood.Launch(wound, away, settings, material);
            return blood;
        }

        private void Launch(Vector3 wound, Vector3 away, Settings launchSettings, Material material)
        {
            settings = launchSettings;
            origin = wound;

            away.y = 0f;
            Vector3 spray = away.sqrMagnitude > 0.000001f ? away.normalized : Vector3.forward;

            droplets = new Droplet[settings.droplets];

            for (int i = 0; i < droplets.Length; i++)
            {
                float angle = UnityEngine.Random.Range(-0.5f, 0.5f) * settings.spread;

                droplets[i] = new Droplet
                {
                    Direction = Quaternion.AngleAxis(angle, Vector3.up) * spray,
                    Speed = UnityEngine.Random.Range(settings.minSpeed, Mathf.Max(settings.minSpeed, settings.maxSpeed)),

                    // A few big gouts among many small drops.
                    Size = settings.size * Mathf.Lerp(0.4f, 1.3f, UnityEngine.Random.value * UnityEngine.Random.value),
                };
            }

            mesh = new Mesh { name = "ScarlettBlood" };
            mesh.MarkDynamic();

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            // Always after the claw. Both are transparent and the claw draws
            // solid over everything, so left to distance sorting the tear can
            // land second and paint every droplet out.
            meshRenderer.sortingOrder = 1;

            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            Rebuild();
        }

        private void Update()
        {
            age += Time.deltaTime;

            if (age >= settings.lifetime)
            {
                Destroy(gameObject);
                return;
            }

            Rebuild();
        }

        /// <summary>Distance travelled after <paramref name="time"/> seconds under linear drag.</summary>
        private float Travelled(float speed, float time)
        {
            time = Mathf.Max(0f, time);

            return settings.drag > 0.0001f
                ? speed * (1f - Mathf.Exp(-settings.drag * time)) / settings.drag
                : speed * time;
        }

        private void Rebuild()
        {
            vertices.Clear();
            normals.Clear();
            uvs.Clear();
            triangles.Clear();

            // Shrinks away over its life, slowly at first.
            float remaining = 1f - age / settings.lifetime;
            float shrink = Mathf.Sqrt(Mathf.Max(0f, remaining));

            foreach (Droplet droplet in droplets)
            {
                float width = droplet.Size * shrink;

                if (width <= 0.0001f)
                {
                    continue;
                }

                Vector3 head = origin + droplet.Direction * Travelled(droplet.Speed, age);
                Vector3 tail = origin + droplet.Direction * Travelled(droplet.Speed, age - settings.stretch);

                // Never shorter than it is wide, so a droplet that has all
                // but stopped still reads as a bead rather than vanishing.
                if (Vector3.Distance(head, tail) < width)
                {
                    tail = head - droplet.Direction * width;
                }

                AddDroplet(tail, head, droplet.Direction, width, shrink);
            }

            ScarlettClaw.LiftTowardCamera(vertices, origin, settings.lift);

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// A kite: a fine point trailing back to the tail, widest near the
        /// front, and a short blunt nose ahead of the head. Outlined, with
        /// the outline thinning as the droplet shrinks.
        /// </summary>
        private void AddDroplet(Vector3 tail, Vector3 head, Vector3 direction, float width, float shrink)
        {
            Vector3 across = Vector3.Cross(Vector3.up, direction) * (width * 0.5f);
            Vector3 shoulder = Vector3.Lerp(tail, head, 0.8f);
            Vector3 nose = head + direction * (width * 0.35f);

            int index = vertices.Count;

            AddVertex(tail);
            AddVertex(shoulder + across);
            AddVertex(nose);
            AddVertex(shoulder - across);

            // Both windings; the material culls one of each pair.
            int a = index, b = index + 1, c = index + 2, d = index + 3;
            triangles.Add(a); triangles.Add(b); triangles.Add(c);
            triangles.Add(a); triangles.Add(c); triangles.Add(d);
            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(a); triangles.Add(d); triangles.Add(c);

            loop.Clear();
            loop.Add(tail);
            loop.Add(shoulder + across);
            loop.Add(nose);
            loop.Add(shoulder - across);
            ScarlettClaw.AppendOutline(loop, settings.outlineWidth * shrink, vertices, normals, null, uvs, triangles);
        }

        private void AddVertex(Vector3 position)
        {
            vertices.Add(position);
            normals.Add(Vector3.up);

            // u = 2 is NeoNoir/Tear's solid branch: drawn flat in the blood
            // colour, not as a window.
            uvs.Add(new Vector2(2f, 0f));
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
