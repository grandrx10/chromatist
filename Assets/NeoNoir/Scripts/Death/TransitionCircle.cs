using LightAndShadow.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// The glowing white circle on the floor under the player as
    /// <see cref="LevelExit"/> carries them to the next level. Kept lit
    /// through <see cref="NeoNoirDarkness"/>, so it shows on the black.
    /// </summary>
    /// <remarks>
    /// Follows a player, on the floor straight under them, at the
    /// <see cref="Radius"/> it is given. Built from a mesh made in code, so it
    /// needs no prefab.
    /// </remarks>
    public sealed class TransitionCircle : MonoBehaviour
    {
        private const int Segments = 72;
        private const float Lift = 0.03f;
        private const float RimWidth = 0.06f;

        private static readonly Color DiscColour = new Color(2.5f, 2.5f, 2.5f, 1f);
        private static readonly Color RimColour = new Color(6f, 6f, 6f, 1f);

        private Transform disc;
        private Transform rim;
        private Mesh discMesh;
        private Mesh rimMesh;

        /// <summary>The player it sits under.</summary>
        public Transform Player { get; set; }

        /// <summary>Its radius on the floor.</summary>
        public float Radius { get; set; }

        public static TransitionCircle Create(Transform parent)
        {
            GameObject host = new GameObject("Transition Circle");
            host.transform.SetParent(parent, false);

            TransitionCircle circle = host.AddComponent<TransitionCircle>();
            circle.Build();
            return circle;
        }

        private void LateUpdate()
        {
            if (Player != null)
            {
                transform.position = Floor(Player) + Vector3.up * Lift;
            }

            float radius = Mathf.Max(0f, Radius);
            disc.gameObject.SetActive(radius > 0.001f);
            rim.gameObject.SetActive(radius > 0.001f);
            disc.localScale = new Vector3(radius, 1f, radius);
            rim.localScale = disc.localScale;
        }

        /// <summary>The floor straight under <paramref name="player"/>, or a little under them with none found.</summary>
        private static Vector3 Floor(Transform player)
        {
            Vector3 from = player.position + Vector3.up * 0.5f;
            RaycastHit[] hits = Physics.RaycastAll(from, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            Vector3 floor = player.position + Vector3.down;

            foreach (RaycastHit hit in hits)
            {
                if (hit.transform.IsChildOf(player) || hit.distance >= best)
                {
                    continue;
                }

                best = hit.distance;
                floor = hit.point;
            }

            return floor;
        }

        private void Build()
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();

            // A unit disc, scaled to the radius.
            Vector3[] vertices = new Vector3[Segments + 1];
            int[] triangles = new int[Segments * 3];
            vertices[0] = Vector3.zero;

            for (int i = 0; i < Segments; i++)
            {
                float angle = i / (float)Segments * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % Segments + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            discMesh = new Mesh { name = "Transition Disc", vertices = vertices, triangles = triangles };
            discMesh.RecalculateNormals();
            discMesh.RecalculateBounds();
            disc = MakePart("Disc", discMesh, DiscColour, block);

            // A thin ring round the edge, a touch above the disc, brighter.
            float inner = 1f - RimWidth;
            Vector3[] ring = new Vector3[Segments * 2];
            int[] ringTriangles = new int[Segments * 6];

            for (int i = 0; i < Segments; i++)
            {
                float angle = i / (float)Segments * Mathf.PI * 2f;
                Vector3 way = new Vector3(Mathf.Cos(angle), 0.005f, Mathf.Sin(angle));
                ring[i * 2] = new Vector3(way.x, way.y, way.z);
                ring[i * 2 + 1] = new Vector3(way.x * inner, way.y, way.z * inner);

                int at = i * 2;
                int next = (i + 1) % Segments * 2;
                int t = i * 6;
                ringTriangles[t] = at; ringTriangles[t + 1] = next; ringTriangles[t + 2] = at + 1;
                ringTriangles[t + 3] = at + 1; ringTriangles[t + 4] = next; ringTriangles[t + 5] = next + 1;
            }

            rimMesh = new Mesh { name = "Transition Rim", vertices = ring, triangles = ringTriangles };
            rimMesh.RecalculateNormals();
            rimMesh.RecalculateBounds();
            rim = MakePart("Rim", rimMesh, RimColour, block);

            disc.gameObject.SetActive(false);
            rim.gameObject.SetActive(false);
        }

        private Transform MakePart(string partName, Mesh mesh, Color colour, MaterialPropertyBlock block)
        {
            GameObject part = new GameObject(partName);
            part.transform.SetParent(transform, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.DoubleSided;
            FlatMaterial.Tint(renderer, colour, block);
            renderer.renderingLayerMask |= NeoNoirDarkness.RenderingLayer;
            return part.transform;
        }

        private void OnDestroy()
        {
            if (discMesh != null)
            {
                Destroy(discMesh);
            }

            if (rimMesh != null)
            {
                Destroy(rimMesh);
            }
        }
    }
}
