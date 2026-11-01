using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// The Katana's cut: a few quick slashes flash across an object, its real
    /// geometry is split along every one of them, and the pieces scatter.
    /// </summary>
    /// <remarks>
    /// <see cref="Cut"/> does the whole thing to any object built from mesh
    /// renderers (skinned ones are cut as they stand that frame). The object
    /// itself is hidden and its colliders switched off; whatever owns it
    /// decides whether to destroy it.
    ///
    /// In order:
    ///   1. Every mesh on the object is gathered into one, in world space,
    ///      and cut through by <see cref="Settings.slashCount"/> slashes:
    ///      planes at random angles through the middle, give or take
    ///      <see cref="Settings.slashSpread"/>. Each slash runs right through
    ///      the object, cutting every piece it meets, as a blade would. Each
    ///      is turned to face the camera, give or take
    ///      <see cref="Settings.slashTilt"/>, so it reads as a line across the
    ///      screen. Cut faces get <see cref="Settings.cutFaceColour"/>.
    ///   2. The pieces stand together, still the object's shape, while the
    ///      slashes flash across it one after another,
    ///      <see cref="Settings.slashInterval"/> apart.
    ///   3. <see cref="Settings.splitDelay"/> after the last slash, the pieces
    ///      come apart: flung outward from the middle and away from whoever
    ///      struck, spinning, under gravity, keeping some of the object's own
    ///      speed if it had a Rigidbody.
    ///   4. After <see cref="Settings.pieceLife"/> they dissolve and shrink
    ///      away over <see cref="Settings.fadeTime"/>, and are gone.
    ///
    /// Meshes must have Read/Write enabled in their import settings to be cut;
    /// ProBuilder meshes always can be. One that cannot is left out, with a
    /// warning.
    ///
    /// The pieces are real rigid bodies with box colliders, so they tumble
    /// across the floor. They never touch the player or each other.
    /// </remarks>
    public sealed class KatanaCut : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Slashes")]
            [Tooltip("Cuts made through the object. Each can split every piece it passes through, so a few make many pieces.")]
            [Min(1)] public int slashCount = 4;

            [Tooltip("How far from the middle a slash can pass, as a share of the object's size. 0 cuts every one dead centre.")]
            [Range(0f, 1f)] public float slashSpread = 0.35f;

            [Tooltip("Degrees a slash can lean out of facing the camera. 0 shows every slash as a clean line across the screen; 90 is any angle at all.")]
            [Range(0f, 90f)] public float slashTilt = 30f;

            [Tooltip("Most pieces the object is cut into. Slashes stop cutting once there are this many.")]
            [Min(2)] public int maxPieces = 24;

            [Tooltip("Pieces smaller than this across are not cut again.")]
            [Min(0f)] public float minPieceSize = 0.3f;

            [Header("Cut faces")]
            [Tooltip("Material for the faces a cut opens up. Empty uses NeoNoir/Flat in the colour below.")]
            public Material cutFaceMaterial;

            [Tooltip("Colour of the cut faces, when no material is set. Above 1 it blooms.")]
            [ColorUsage(false, true)]
            public Color cutFaceColour = new Color(2f, 2f, 2f, 1f);

            [Tooltip("Scale of the cut faces' flat UVs, for a textured cut face material.")]
            [Min(0f)] public float cutFaceUvScale = 1f;

            [Header("Slash flash")]
            [Tooltip("Colour of the line each slash flashes across the object. Above 1 it blooms.")]
            [ColorUsage(false, true)]
            public Color flashColour = new Color(4f, 4f, 4f, 1f);

            [Tooltip("Thickness of a slash line as it is drawn.")]
            [Min(0.01f)] public float flashWidth = 0.18f;

            [Tooltip("Length of a slash line, as a multiple of the object's size.")]
            [Min(0.1f)] public float flashLength = 1.4f;

            [Tooltip("Seconds a slash line takes to draw across.")]
            [Min(0.01f)] public float flashDrawTime = 0.06f;

            [Tooltip("Seconds a drawn slash line takes to thin away.")]
            [Min(0.01f)] public float flashTime = 0.25f;

            [Tooltip("Seconds between one slash and the next.")]
            [Min(0f)] public float slashInterval = 0.05f;

            [Tooltip("Seconds after the last slash before the pieces come apart.")]
            [Min(0f)] public float splitDelay = 0.12f;

            [Header("Scatter")]
            [Tooltip("Slowest and fastest a piece flies out from the middle.")]
            public Vector2 scatterSpeed = new Vector2(3f, 8f);

            [Tooltip("Slowest and fastest a piece is thrown upward.")]
            public Vector2 upSpeed = new Vector2(2f, 6f);

            [Tooltip("Extra speed away from whoever struck the blow.")]
            [Min(0f)] public float pushSpeed = 3f;

            [Tooltip("Share of the object's own speed the pieces keep, if it had a Rigidbody.")]
            [Range(0f, 1f)] public float inheritVelocity = 0.3f;

            [Tooltip("Fastest a piece spins, in degrees per second.")]
            [Min(0f)] public float spin = 540f;

            [Tooltip("Mass per unit of a piece's box. Heavier pieces shove each other less on the floor.")]
            [Min(0.001f)] public float density = 1f;

            [Header("Afterwards")]
            [Tooltip("Seconds the pieces lie about after coming apart.")]
            [Min(0f)] public float pieceLife = 2f;

            [Tooltip("Seconds the pieces take to dissolve and shrink away.")]
            [Min(0.01f)] public float fadeTime = 0.5f;

            [Tooltip("How far the camera shakes as the pieces come apart. 0 for none.")]
            [Min(0f)] public float shakeStrength = 0.4f;

            [Min(0.01f)] public float shakeDuration = 0.25f;
        }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private struct Slash
        {
            public Vector3 centre;
            public Vector3 direction;
            public Vector3 across;
            public float length;
            public float startTime;
        }

        private sealed class CutPiece
        {
            public GameObject gameObject;
            public MeshRenderer renderer;
            public Rigidbody body;
            public Vector3 baseScale;
        }

        private Settings settings;
        private readonly List<CutPiece> pieces = new List<CutPiece>();
        private readonly List<Slash> slashes = new List<Slash>();
        private Vector3 middle;
        private Vector3 away;
        private Vector3 inherited;
        private MaterialPropertyBlock block;

        private Mesh flashMesh;
        private readonly List<Vector3> flashVertices = new List<Vector3>();
        private readonly List<int> flashTriangles = new List<int>();

        private float clock;
        private float splitTime;
        private bool split;
        private float endTime;

        /// <summary>
        /// Cuts <paramref name="target"/> to pieces as a blow from
        /// <paramref name="from"/>, a world point, and hides it. Returns the
        /// running cut, or null if there was nothing on it that could be cut.
        /// </summary>
        public static KatanaCut Cut(GameObject target, Settings settings, Vector3 from)
        {
            Rigidbody targetBody = target != null ? target.GetComponent<Rigidbody>() : null;
            Vector3 velocity = targetBody != null && !targetBody.isKinematic ? targetBody.linearVelocity : Vector3.zero;
            return Cut(target, settings, from, velocity);
        }

        /// <summary>
        /// As <see cref="Cut(GameObject, Settings, Vector3)"/>, for something
        /// moved by hand rather than by physics: the pieces carry on with
        /// their share of <paramref name="velocity"/>.
        /// </summary>
        public static KatanaCut Cut(GameObject target, Settings settings, Vector3 from, Vector3 velocity)
        {
            if (target == null || settings == null)
            {
                return null;
            }

            List<Material> materials = new List<Material>();
            MeshSlicer.Piece whole = Gather(target, materials);

            if (whole == null || whole.IsEmpty)
            {
                return null;
            }

            Hide(target);

            GameObject host = new GameObject($"{target.name} (cut)");
            KatanaCut cut = host.AddComponent<KatanaCut>();
            cut.Begin(whole, materials, settings, from, velocity);
            return cut;
        }

        /// <summary>Every readable mesh under <paramref name="target"/>, in world space, one submesh per material plus one for cut faces.</summary>
        public static MeshSlicer.Piece Gather(GameObject target, List<Material> materials)
        {
            Dictionary<Material, int> slots = new Dictionary<Material, int>();
            List<List<int>> triangles = new List<List<int>>();
            MeshSlicer.Piece gathered = null;
            List<Vector3> positions = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();

            foreach (Renderer part in target.GetComponentsInChildren<Renderer>())
            {
                if (!part.enabled)
                {
                    continue;
                }

                Mesh mesh;
                Matrix4x4 matrix;
                bool baked = false;

                if (part is SkinnedMeshRenderer skinned)
                {
                    if (skinned.sharedMesh == null)
                    {
                        continue;
                    }

                    mesh = new Mesh();
                    skinned.BakeMesh(mesh, true);
                    matrix = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
                    baked = true;
                }
                else if (part is MeshRenderer)
                {
                    MeshFilter filter = part.GetComponent<MeshFilter>();

                    if (filter == null || filter.sharedMesh == null)
                    {
                        continue;
                    }

                    mesh = filter.sharedMesh;
                    matrix = part.transform.localToWorldMatrix;

                    if (!mesh.isReadable)
                    {
                        Debug.LogWarning($"[KatanaCut] {mesh.name} on {part.name} can't be cut: turn on Read/Write in its import settings.", part);
                        continue;
                    }
                }
                else
                {
                    continue;
                }

                Matrix4x4 normalMatrix = matrix.inverse.transpose;
                bool mirrored = matrix.determinant < 0f;

                int start = positions.Count;
                Vector3[] meshPositions = mesh.vertices;
                Vector3[] meshNormals = mesh.normals;
                Vector2[] meshUvs = mesh.uv;

                for (int i = 0; i < meshPositions.Length; i++)
                {
                    positions.Add(matrix.MultiplyPoint3x4(meshPositions[i]));
                    normals.Add(i < meshNormals.Length ? normalMatrix.MultiplyVector(meshNormals[i]).normalized : Vector3.up);
                    uvs.Add(i < meshUvs.Length ? meshUvs[i] : Vector2.zero);
                }

                Material[] partMaterials = part.sharedMaterials;

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material material = partMaterials.Length > 0 ? partMaterials[Mathf.Min(s, partMaterials.Length - 1)] : null;

                    if (material == null)
                    {
                        material = FlatMaterial.Shared;
                    }

                    if (!slots.TryGetValue(material, out int slot))
                    {
                        slot = materials.Count;
                        slots[material] = slot;
                        materials.Add(material);
                        triangles.Add(new List<int>());
                    }

                    int[] meshTriangles = mesh.GetTriangles(s);

                    for (int t = 0; t + 2 < meshTriangles.Length; t += 3)
                    {
                        triangles[slot].Add(start + meshTriangles[t]);

                        // A mirrored transform turns faces inside out unless rewound.
                        if (mirrored)
                        {
                            triangles[slot].Add(start + meshTriangles[t + 2]);
                            triangles[slot].Add(start + meshTriangles[t + 1]);
                        }
                        else
                        {
                            triangles[slot].Add(start + meshTriangles[t + 1]);
                            triangles[slot].Add(start + meshTriangles[t + 2]);
                        }
                    }
                }

                if (baked)
                {
                    Destroy(mesh);
                }
            }

            if (materials.Count == 0)
            {
                return null;
            }

            // One more list on the end for the cut faces.
            gathered = new MeshSlicer.Piece(materials.Count + 1);
            gathered.positions.AddRange(positions);
            gathered.normals.AddRange(normals);
            gathered.uvs.AddRange(uvs);

            for (int i = 0; i < triangles.Count; i++)
            {
                gathered.submeshes[i].AddRange(triangles[i]);
            }

            return gathered;
        }

        /// <summary>Hides <paramref name="target"/> and takes it out of play, leaving its fate to its owner.</summary>
        public static void Hide(GameObject target)
        {
            foreach (Renderer part in target.GetComponentsInChildren<Renderer>())
            {
                part.enabled = false;
            }

            foreach (Collider part in target.GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
            }
        }

        private void Begin(MeshSlicer.Piece whole, List<Material> materials, Settings cutSettings, Vector3 from, Vector3 velocity)
        {
            settings = cutSettings;
            block = new MaterialPropertyBlock();
            inherited = velocity * settings.inheritVelocity;

            Bounds bounds = whole.GetBounds();
            middle = bounds.center;

            away = middle - from;
            away.y = 0f;
            away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.zero;

            Camera view = Camera.main;
            Vector3 viewForward = view != null ? view.transform.forward : Vector3.down;

            List<MeshSlicer.Piece> parts = new List<MeshSlicer.Piece> { whole };

            for (int i = 0; i < settings.slashCount; i++)
            {
                Plane plane = RandomSlash(bounds, viewForward);
                CutAll(parts, plane);
                slashes.Add(MakeSlash(plane, bounds, viewForward, i * settings.slashInterval));
            }

            foreach (MeshSlicer.Piece part in parts)
            {
                pieces.Add(MakePiece(part, materials));
            }

            IgnoreEachOther();
            BuildFlash();

            float lastSlash = (settings.slashCount - 1) * settings.slashInterval;
            splitTime = lastSlash + settings.splitDelay;
            endTime = Mathf.Max(splitTime + settings.pieceLife + settings.fadeTime,
                                lastSlash + settings.flashDrawTime + settings.flashTime);

            UpdateFlash();
        }

        /// <summary>
        /// A plane through the middle of <paramref name="bounds"/>, give or
        /// take the spread, turned to face the camera give or take the tilt,
        /// so its cut shows as a line across the screen.
        /// </summary>
        private Plane RandomSlash(Bounds bounds, Vector3 viewForward)
        {
            // Any direction across the screen...
            Vector3 screenRight = Vector3.Cross(Vector3.up, viewForward);

            if (screenRight.sqrMagnitude < 0.0001f)
            {
                screenRight = Vector3.right;
            }

            screenRight.Normalize();
            Vector3 screenUp = Vector3.Cross(viewForward, screenRight).normalized;

            float angle = Random.Range(0f, Mathf.PI * 2f);
            Vector3 normal = screenRight * Mathf.Cos(angle) + screenUp * Mathf.Sin(angle);

            // ...leaning a little toward or away from the camera.
            float tilt = Random.Range(-settings.slashTilt, settings.slashTilt);
            Vector3 axis = Vector3.Cross(normal, viewForward);
            normal = Quaternion.AngleAxis(tilt, axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up) * normal;

            Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f))) * settings.slashSpread;

            return new Plane(normal.normalized, point);
        }

        /// <summary>Cuts every piece <paramref name="plane"/> runs through, as one stroke would.</summary>
        private void CutAll(List<MeshSlicer.Piece> parts, Plane plane)
        {
            int count = parts.Count;

            for (int i = 0; i < count && parts.Count < settings.maxPieces; i++)
            {
                MeshSlicer.Piece part = parts[i];
                Vector3 size = part.GetBounds().size;

                if (Mathf.Max(size.x, size.y, size.z) < settings.minPieceSize)
                {
                    continue;
                }

                if (MeshSlicer.Slice(part, plane, settings.cutFaceUvScale, out MeshSlicer.Piece above, out MeshSlicer.Piece below))
                {
                    parts[i] = above;
                    parts.Add(below);
                }
            }
        }

        private Slash MakeSlash(Plane plane, Bounds bounds, Vector3 viewForward, float startTime)
        {
            // Where the plane meets the screen: a line across it.
            Vector3 direction = Vector3.Cross(plane.normal, viewForward);
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;

            // Drawn in front of the object, so it is not hidden inside it.
            Vector3 centre = plane.ClosestPointOnPlane(bounds.center) - viewForward * bounds.extents.magnitude;

            return new Slash
            {
                centre = centre,
                direction = Random.value < 0.5f ? direction : -direction,
                across = Vector3.Cross(direction, viewForward).normalized,
                length = bounds.size.magnitude * settings.flashLength,
                startTime = startTime,
            };
        }

        /// <summary>
        /// A new object showing <paramref name="part"/>, its pivot at the
        /// middle of its bounds, drawn in <paramref name="materials"/> with
        /// the cut faces in the Katana's cut face colour. Its mesh goes with it.
        /// </summary>
        public static GameObject BuildPiece(MeshSlicer.Piece part, List<Material> materials, Settings settings, string pieceName)
        {
            Bounds bounds = part.GetBounds();

            GameObject piece = new GameObject(pieceName);
            piece.transform.position = bounds.center;

            Mesh mesh = part.ToMesh(bounds.center);
            piece.AddComponent<MeshFilter>().sharedMesh = mesh;
            piece.AddComponent<OwnedMesh>().Mesh = mesh;

            Material[] pieceMaterials = new Material[materials.Count + 1];
            materials.CopyTo(pieceMaterials);
            pieceMaterials[materials.Count] = settings.cutFaceMaterial != null ? settings.cutFaceMaterial : FlatMaterial.Shared;

            MeshRenderer pieceRenderer = piece.AddComponent<MeshRenderer>();
            pieceRenderer.sharedMaterials = pieceMaterials;
            pieceRenderer.shadowCastingMode = ShadowCastingMode.Off;

            if (settings.cutFaceMaterial == null)
            {
                int capSlot = pieceMaterials.Length - 1;
                MaterialPropertyBlock capBlock = new MaterialPropertyBlock();
                pieceRenderer.GetPropertyBlock(capBlock, capSlot);
                capBlock.SetColor(FlatMaterial.BaseColorId, settings.cutFaceColour);
                pieceRenderer.SetPropertyBlock(capBlock, capSlot);
            }

            return piece;
        }

        private CutPiece MakePiece(MeshSlicer.Piece part, List<Material> materials)
        {
            Bounds bounds = part.GetBounds();
            GameObject piece = BuildPiece(part, materials, settings, "Cut piece");
            piece.transform.SetParent(transform, true);

            BoxCollider box = piece.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = Vector3.Max(bounds.size, Vector3.one * 0.05f);

            Rigidbody body = piece.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.mass = Mathf.Max(0.05f, box.size.x * box.size.y * box.size.z * settings.density);

            return new CutPiece
            {
                gameObject = piece,
                renderer = piece.GetComponent<MeshRenderer>(),
                body = body,
                baseScale = piece.transform.localScale,
            };
        }

        /// <summary>Pieces start packed together, and must neither shove each other apart nor push the player around.</summary>
        private void IgnoreEachOther()
        {
            List<Collider> colliders = new List<Collider>(pieces.Count);

            foreach (CutPiece piece in pieces)
            {
                colliders.Add(piece.gameObject.GetComponent<Collider>());
            }

            for (int i = 0; i < colliders.Count; i++)
            {
                for (int j = i + 1; j < colliders.Count; j++)
                {
                    Physics.IgnoreCollision(colliders[i], colliders[j]);
                }
            }

            PlayerMotor player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

            if (player == null)
            {
                return;
            }

            foreach (Collider playerPart in player.GetComponentsInChildren<Collider>())
            {
                foreach (Collider piece in colliders)
                {
                    Physics.IgnoreCollision(piece, playerPart);
                }
            }
        }

        private void Update()
        {
            clock += Time.deltaTime;

            if (!split && clock >= splitTime)
            {
                Release();
            }

            if (split)
            {
                float fading = clock - splitTime - settings.pieceLife;

                if (fading > 0f)
                {
                    Fade(1f - Mathf.Clamp01(fading / settings.fadeTime));
                }
            }

            UpdateFlash();

            if (clock >= endTime)
            {
                Destroy(gameObject);
            }
        }

        private void Release()
        {
            split = true;

            foreach (CutPiece piece in pieces)
            {
                Vector3 outward = piece.gameObject.transform.position - middle;
                outward = outward.sqrMagnitude > 0.0001f ? outward.normalized : Random.onUnitSphere;

                piece.body.isKinematic = false;
                piece.body.linearVelocity = inherited
                    + outward * Random.Range(settings.scatterSpeed.x, settings.scatterSpeed.y)
                    + away * settings.pushSpeed
                    + Vector3.up * Random.Range(settings.upSpeed.x, settings.upSpeed.y);
                piece.body.angularVelocity = Random.onUnitSphere * (settings.spin * Mathf.Deg2Rad * Random.Range(0.3f, 1f));
            }

            if (settings.shakeStrength > 0f)
            {
                TopDownCamera.Shake(settings.shakeStrength, settings.shakeDuration);
            }
        }

        /// <summary>Dissolves and shrinks every piece; 1 whole, 0 gone.</summary>
        private void Fade(float visibility)
        {
            foreach (CutPiece piece in pieces)
            {
                int slots = piece.renderer.sharedMaterials.Length;

                for (int i = 0; i < slots; i++)
                {
                    piece.renderer.GetPropertyBlock(block, i);
                    block.SetFloat(FadeId, visibility);
                    piece.renderer.SetPropertyBlock(block, i);
                }

                piece.gameObject.transform.localScale = piece.baseScale * Mathf.Lerp(0.2f, 1f, visibility);
            }
        }

        private void BuildFlash()
        {
            GameObject flash = new GameObject("Slashes");
            flash.transform.SetParent(transform, false);

            flashMesh = new Mesh { name = "Katana slashes" };
            flashMesh.MarkDynamic();
            flash.AddComponent<MeshFilter>().sharedMesh = flashMesh;

            MeshRenderer flashRenderer = flash.AddComponent<MeshRenderer>();
            flashRenderer.sharedMaterial = FlatMaterial.DoubleSided;
            flashRenderer.shadowCastingMode = ShadowCastingMode.Off;
            flashRenderer.receiveShadows = false;

            flashRenderer.GetPropertyBlock(block);
            block.SetColor(FlatMaterial.BaseColorId, settings.flashColour);
            flashRenderer.SetPropertyBlock(block);
        }

        /// <summary>Each slash draws across from one end to the other, then thins away to nothing.</summary>
        private void UpdateFlash()
        {
            if (flashMesh == null)
            {
                return;
            }

            flashVertices.Clear();
            flashTriangles.Clear();

            foreach (Slash slash in slashes)
            {
                float age = clock - slash.startTime;

                if (age < 0f || age > settings.flashDrawTime + settings.flashTime)
                {
                    continue;
                }

                float drawn = Mathf.Clamp01(age / settings.flashDrawTime);
                float thinning = Mathf.Clamp01((age - settings.flashDrawTime) / settings.flashTime);
                float width = settings.flashWidth * (1f - thinning);

                if (width <= 0.001f)
                {
                    continue;
                }

                Vector3 start = slash.centre - slash.direction * (slash.length * 0.5f);
                Vector3 end = start + slash.direction * (slash.length * drawn);

                // Tapered to points at both ends, like a blade's stroke.
                Vector3 side = slash.across * (width * 0.5f);
                Vector3 mid = (start + end) * 0.5f;
                int at = flashVertices.Count;

                flashVertices.Add(start);
                flashVertices.Add(mid + side);
                flashVertices.Add(end);
                flashVertices.Add(mid - side);

                flashTriangles.Add(at);
                flashTriangles.Add(at + 1);
                flashTriangles.Add(at + 2);
                flashTriangles.Add(at);
                flashTriangles.Add(at + 2);
                flashTriangles.Add(at + 3);
            }

            flashMesh.Clear();
            flashMesh.SetVertices(flashVertices);
            flashMesh.SetTriangles(flashTriangles, 0);
            flashMesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (flashMesh != null)
            {
                Destroy(flashMesh);
            }
        }
    }
}
