using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// A glowing line traced around a cut, where a blade went through
    /// something: it flashes on at full width, holds, then thins away.
    /// </summary>
    /// <remarks>
    /// Drawn from the edges <see cref="MeshSlicer.Slice"/> hands back, as
    /// flat strips turned to face the camera, nudged toward it so the surface
    /// they lie on does not hide half of each. Nothing is ever
    /// half-transparent: the line thins to nothing rather than fading. Stays
    /// where it was drawn.
    /// </remarks>
    public sealed class CutOutline : MonoBehaviour
    {
        private readonly List<Vector3> edges = new List<Vector3>();
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<int> triangles = new List<int>();

        private Mesh mesh;
        private float width;
        private float holdTime;
        private float fadeTime;
        private float age;

        /// <summary>
        /// Traces <paramref name="cutEdges"/>, pairs of points, in
        /// <paramref name="colour"/>, <paramref name="lineWidth"/> wide, for
        /// <paramref name="hold"/> seconds, then thins it away over
        /// <paramref name="fade"/>. Null with no edges.
        /// </summary>
        public static CutOutline Create(List<Vector3> cutEdges, Color colour, float lineWidth, float hold, float fade)
        {
            Material material = FlatMaterial.DoubleSided;

            if (cutEdges == null || cutEdges.Count < 2 || material == null || lineWidth <= 0f)
            {
                return null;
            }

            GameObject host = new GameObject("Cut outline");
            CutOutline outline = host.AddComponent<CutOutline>();
            outline.edges.AddRange(cutEdges);
            outline.width = lineWidth;
            outline.holdTime = Mathf.Max(0f, hold);
            outline.fadeTime = Mathf.Max(0.01f, fade);

            outline.mesh = new Mesh { name = "Cut outline" };
            outline.mesh.MarkDynamic();
            host.AddComponent<MeshFilter>().sharedMesh = outline.mesh;

            MeshRenderer outlineRenderer = host.AddComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = material;
            outlineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            outlineRenderer.receiveShadows = false;
            FlatMaterial.Tint(outlineRenderer, colour, new MaterialPropertyBlock());

            outline.Build();
            return outline;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;

            if (age >= holdTime + fadeTime)
            {
                Destroy(gameObject);
                return;
            }

            // Rebuilt every frame, so it keeps facing a camera on the move.
            Build();
        }

        private void Build()
        {
            vertices.Clear();
            triangles.Clear();

            float thinning = Mathf.Clamp01((age - holdTime) / fadeTime);
            float half = width * 0.5f * (1f - thinning);

            Camera view = Camera.main;

            if (half > 0.0005f && view != null)
            {
                Vector3 cameraPosition = view.transform.position;

                for (int i = 0; i + 1 < edges.Count; i += 2)
                {
                    Vector3 a = edges[i];
                    Vector3 b = edges[i + 1];
                    Vector3 along = b - a;

                    if (along.sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }

                    along.Normalize();

                    // Stretched a little past both ends, so neighbouring
                    // strips overlap at their corners rather than leave gaps.
                    a -= along * half;
                    b += along * half;

                    Vector3 middle = (a + b) * 0.5f;
                    Vector3 toCamera = (cameraPosition - middle).normalized;
                    Vector3 side = Vector3.Cross(along, toCamera);

                    if (side.sqrMagnitude < 1e-8f)
                    {
                        continue;
                    }

                    side = side.normalized * half;
                    Vector3 lift = toCamera * width;
                    int at = vertices.Count;

                    vertices.Add(a - side + lift);
                    vertices.Add(a + side + lift);
                    vertices.Add(b + side + lift);
                    vertices.Add(b - side + lift);

                    triangles.Add(at);
                    triangles.Add(at + 1);
                    triangles.Add(at + 2);
                    triangles.Add(at);
                    triangles.Add(at + 2);
                    triangles.Add(at + 3);
                }
            }

            mesh.Clear();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
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
