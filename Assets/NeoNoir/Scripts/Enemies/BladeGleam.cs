using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The gleam off a blade about to strike: a four-pointed star, like a
    /// plus sign pinched in at its middle, that flashes open on the blade,
    /// turns a little, and closes again.
    /// </summary>
    /// <remarks>
    /// Always faces the camera, and is drawn a little way toward it, so the
    /// blade it sits on never cuts through it. It rides along with whatever
    /// it is put on.
    ///
    /// Nothing fades: the star shrinks to nothing, so every pixel stays full
    /// colour, as with <see cref="HammerImpact"/>. Coloured through a property
    /// block on the shared runtime NeoNoir/Flat material, so it needs no
    /// material or prefab.
    /// </remarks>
    public sealed class BladeGleam : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Distance from the centre to the tip of each point, fully open.")]
            [Min(0f)] public float size = 1.6f;

            [Tooltip("How far the star's waist reaches between its points, as a fraction of its size. Lower is spikier.")]
            [Range(0.02f, 0.7f)] public float waist = 0.12f;

            [Tooltip("Seconds from appearing to gone.")]
            [Min(0.01f)] public float time = 0.5f;

            [Tooltip("Fraction of the time spent flashing open. It closes over the rest.")]
            [Range(0.01f, 0.99f)] public float openFraction = 0.25f;

            [Tooltip("Degrees the star turns over its life.")]
            public float spin = 45f;

            [Tooltip("Colour intensity. 1 is the colour as given; above 1 blooms.")]
            [Min(0f)] public float intensity = 4f;

            [Tooltip("How far toward the camera it is drawn, so the blade does not cut through it.")]
            [Min(0f)] public float towardCamera = 0.6f;
        }

        private const int Points = 4;

        private static readonly List<Vector3> Vertices = new List<Vector3>();
        private static readonly List<int> Triangles = new List<int>();

        private Settings settings;
        private Transform follow;
        private Vector3 offset;
        private float scale = 1f;
        private float age;
        private Mesh mesh;

        /// <summary>
        /// Flashes a gleam in <paramref name="colour"/> at
        /// <paramref name="localOffset"/> in <paramref name="on"/>'s space,
        /// following it as it moves. <paramref name="sizeScale"/> multiplies
        /// its size.
        /// </summary>
        public static BladeGleam Flash(Transform on, Vector3 localOffset, Color colour, Settings settings, float sizeScale = 1f)
        {
            GameObject host = new GameObject("BladeGleam");
            BladeGleam gleam = host.AddComponent<BladeGleam>();
            gleam.scale = sizeScale;
            gleam.Begin(on, localOffset, colour, settings);
            return gleam;
        }

        private void Begin(Transform on, Vector3 localOffset, Color colour, Settings gleamSettings)
        {
            settings = gleamSettings;
            follow = on;
            offset = localOffset;

            mesh = new Mesh { name = "Blade gleam" };
            BuildStar();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer meshRenderer = gameObject.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            meshRenderer.sharedMaterial = FlatMaterial.DoubleSided;

            Color bright = colour * settings.intensity;
            bright.a = colour.a;
            FlatMaterial.Tint(meshRenderer, bright, new MaterialPropertyBlock());

            Place();
        }

        /// <summary>
        /// A blade's tip in <paramref name="sword"/>'s own space, for where to flash a gleam: the far
        /// end, from the pivot, of the longest side of its meshes. Works
        /// whichever way the model was built. The pivot if it has no mesh.
        /// </summary>
        public static Vector3 TipOf(Transform sword)
        {
            bool found = false;
            Bounds local = default;

            foreach (MeshFilter part in sword.GetComponentsInChildren<MeshFilter>())
            {
                if (part.sharedMesh == null)
                {
                    continue;
                }

                Bounds mesh = part.sharedMesh.bounds;

                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mesh.center + Vector3.Scale(mesh.extents, new Vector3(
                        (i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                    Vector3 point = sword.InverseTransformPoint(part.transform.TransformPoint(corner));

                    if (found)
                    {
                        local.Encapsulate(point);
                    }
                    else
                    {
                        local = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                }
            }

            if (!found)
            {
                return Vector3.zero;
            }

            Vector3 size = local.size;
            int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;

            // The blade is the long way from the grip; the handle the short.
            Vector3 tip = local.center;
            tip[axis] = Mathf.Abs(local.max[axis]) >= Mathf.Abs(local.min[axis]) ? local.max[axis] : local.min[axis];
            return tip;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;

            if (age >= settings.time || follow == null)
            {
                Destroy(gameObject);
                return;
            }

            Place();
        }

        private void Place()
        {
            Camera view = Camera.main;
            Vector3 centre = follow.TransformPoint(offset);
            Quaternion facing = Quaternion.identity;

            if (view != null)
            {
                Vector3 toCamera = (view.transform.position - centre).normalized;
                centre += toCamera * settings.towardCamera;
                facing = Quaternion.LookRotation(-toCamera, view.transform.up);
            }

            float t = age / settings.time;
            float open = settings.openFraction;

            // Eased out both ways: it snaps open, then closes quickly at first
            // and lingers small.
            float opened = t < open
                ? 1f - Square(1f - t / open)
                : Square(1f - (t - open) / (1f - open));

            transform.SetPositionAndRotation(centre, facing * Quaternion.Euler(0f, 0f, settings.spin * t));
            transform.localScale = Vector3.one * (settings.size * scale * opened);
        }

        private static float Square(float value)
        {
            return value * value;
        }

        /// <summary>A unit star in the XY plane: a tip on each axis, the waist between.</summary>
        private void BuildStar()
        {
            Vertices.Clear();
            Triangles.Clear();

            Vertices.Add(Vector3.zero);

            for (int i = 0; i < Points * 2; i++)
            {
                float angle = i * Mathf.PI / Points;
                float radius = i % 2 == 0 ? 1f : settings.waist;
                Vertices.Add(new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f) * radius);
            }

            for (int i = 0; i < Points * 2; i++)
            {
                Triangles.Add(0);
                Triangles.Add(1 + i);
                Triangles.Add(1 + (i + 1) % (Points * 2));
            }

            mesh.SetVertices(Vertices);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            Destroy(mesh);
        }
    }
}
