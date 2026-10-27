using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace LightAndShadow.EditorTools
{
    /// <summary>
    /// Builds a wall round the outside edge of the selected platforms, so
    /// nothing can walk off them: Tools > NeoNoir > Platform Wall Builder.
    /// </summary>
    /// <remarks>
    /// The selected objects (and their children) are flattened into one
    /// footprint from the tops of their meshes, so platforms that touch or
    /// overlap -- a round floor with arms reaching out of it -- are walled as
    /// one shape. Holes in that footprint get walled too, round their inside.
    /// Seams narrower than Close Gaps between neighbouring platforms are
    /// treated as solid floor.
    ///
    /// The wall is left open wherever there is somewhere to walk to just
    /// outside the edge, within Max Step of the platform's height: the top of
    /// a staircase or ramp, or a floor at the same level. So only real drops
    /// get walled. This needs colliders on that neighbouring geometry; the
    /// selected platforms' own colliders are ignored.
    ///
    /// The wall is one mesh with a MeshCollider, saved under
    /// Assets/NeoNoir/Meshes/Walls. It is invisible unless Visible is on.
    /// Building is undoable; the saved mesh stays behind.
    /// </remarks>
    public sealed class PlatformWallBuilder : EditorWindow
    {
        private const string MenuPath = "Tools/NeoNoir/Platform Wall Builder";
        private const string MeshFolder = "Assets/NeoNoir/Meshes/Walls";
        private const string WallPrefix = "Wall around ";

        /// <summary>Steepest a surface can be and still count as floor: the up part of its normal.</summary>
        private const float MinFloorNormal = 0.5f;

        /// <summary>Loops of footprint smaller than this, in square metres, are specks, not edges.</summary>
        private const float MinLoopArea = 1f;

        /// <summary>Most cells the footprint is drawn into. Larger areas get coarser cells.</summary>
        private const int MaxCells = 6000000;

        [SerializeField] private float height = 3f;
        [SerializeField] private float thickness = 0.5f;
        [SerializeField] private float sink = 0.5f;
        [SerializeField] private float inset;
        [SerializeField] private bool visible;
        [SerializeField] private Material material;
        [SerializeField] private bool matchLayer = true;
        [SerializeField] private int layer;

        [SerializeField] private bool leaveOpenings = true;
        [SerializeField] private float maxStep = 1f;
        [SerializeField] private float openingProbe = 0.6f;
        [SerializeField] private float minOpening = 0.75f;

        [SerializeField] private float resolution = 0.1f;
        [SerializeField] private float closeGaps = 0.3f;
        [SerializeField] private float smoothness = 0.05f;

        [NonSerialized] private Outline preview;
        [NonSerialized] private Vector2 scroll;

        [MenuItem(MenuPath, priority = 21)]
        private static void Open()
        {
            GetWindow<PlatformWallBuilder>("Platform Walls");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnSelectionChange()
        {
            ClearPreview();
            Repaint();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.HelpBox(
                "Select the platforms to wall in, then Build Wall. Touching or overlapping platforms are walled as one " +
                "shape. Preview draws the wall in green and the openings left for stairs and ramps in red.",
                MessageType.None);

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Wall", EditorStyles.boldLabel);
            height = Mathf.Max(0.1f, EditorGUILayout.FloatField(
                new GUIContent("Height", "How far the wall stands above the platform."), height));
            thickness = Mathf.Max(0.05f, EditorGUILayout.FloatField(
                new GUIContent("Thickness", "How thick the wall is. Thick walls are harder for fast things to pass through."), thickness));
            sink = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Sink", "How far the wall reaches below the platform's surface, so nothing slips under it."), sink));
            inset = EditorGUILayout.FloatField(
                new GUIContent("Inset", "Moves the wall inward over the platform by this much. 0 stands its inner face on the edge; negative moves it out."), inset);

            visible = EditorGUILayout.Toggle(
                new GUIContent("Visible", "Off makes an invisible wall: a collider only."), visible);

            using (new EditorGUI.DisabledScope(!visible))
            {
                material = (Material)EditorGUILayout.ObjectField(
                    new GUIContent("Material", "Empty uses the first platform's material."), material, typeof(Material), false);
            }

            matchLayer = EditorGUILayout.Toggle(
                new GUIContent("Platform's Layer", "Put the wall on the same layer as the first selected platform."), matchLayer);

            using (new EditorGUI.DisabledScope(matchLayer))
            {
                layer = EditorGUILayout.LayerField("Layer", layer);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Openings", EditorStyles.boldLabel);
            leaveOpenings = EditorGUILayout.Toggle(
                new GUIContent("Leave Openings", "Leave the wall open wherever there is floor just outside the edge to walk on to: stairs, ramps, a floor at the same height."), leaveOpenings);

            using (new EditorGUI.DisabledScope(!leaveOpenings))
            {
                maxStep = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Max Step", "Floor outside the edge this much above or below the platform counts as somewhere to walk to."), maxStep));
                openingProbe = Mathf.Max(0.05f, EditorGUILayout.FloatField(
                    new GUIContent("Probe Distance", "How far beyond the wall's outer face to look for floor."), openingProbe));
                minOpening = Mathf.Max(0f, EditorGUILayout.FloatField(
                    new GUIContent("Min Opening", "Openings narrower than this are walled over, so stray props outside the edge do not punch holes."), minOpening));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Tracing", EditorStyles.boldLabel);
            resolution = Mathf.Max(0.02f, EditorGUILayout.FloatField(
                new GUIContent("Resolution", "Size of the cells the footprint is traced on, in metres. Smaller follows the edge more closely but is slower."), resolution));
            closeGaps = Mathf.Max(0f, EditorGUILayout.FloatField(
                new GUIContent("Close Gaps", "Seams between platforms up to this wide count as floor and are not walled."), closeGaps));
            smoothness = Mathf.Max(0.001f, EditorGUILayout.FloatField(
                new GUIContent("Tolerance", "How far the wall may stray from the traced edge to save corners. Smaller makes rounder curves with more faces."), smoothness));

            if (EditorGUI.EndChangeCheck())
            {
                ClearPreview();
            }

            EditorGUILayout.Space();

            int selected = SelectedPlatforms(out _).Count;
            EditorGUILayout.LabelField(selected == 0 ? "Nothing with a mesh is selected." : $"{selected} meshes selected.");

            using (new EditorGUI.DisabledScope(selected == 0 || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Preview"))
                {
                    preview = TraceSelection();
                    SceneView.RepaintAll();
                }

                if (GUILayout.Button("Build Wall"))
                {
                    Build();
                }

                EditorGUILayout.EndHorizontal();
            }

            if (preview != null)
            {
                EditorGUILayout.LabelField($"{preview.runs.Count} wall pieces, {preview.openings.Count} open points.");
            }

            EditorGUILayout.EndScrollView();
        }

        private void ClearPreview()
        {
            if (preview != null)
            {
                preview = null;
                SceneView.RepaintAll();
            }
        }

        private void OnSceneGUI(SceneView view)
        {
            if (preview == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Handles.zTest = CompareFunction.Always;

            foreach (Run run in preview.runs)
            {
                int count = run.points.Count + (run.closed ? 1 : 0);
                var bottom = new Vector3[count];
                var top = new Vector3[count];

                for (int i = 0; i < count; i++)
                {
                    Vector3 point = run.points[i % run.points.Count];
                    bottom[i] = point + Vector3.up * 0.05f;
                    top[i] = point + Vector3.up * height;
                }

                Handles.color = new Color(0.2f, 1f, 0.3f);
                Handles.DrawAAPolyLine(4f, bottom);
                Handles.color = new Color(0.2f, 1f, 0.3f, 0.4f);
                Handles.DrawAAPolyLine(2f, top);
            }

            Handles.color = new Color(1f, 0.25f, 0.2f);

            foreach (Vector3 point in preview.openings)
            {
                Handles.DrawSolidDisc(point + Vector3.up * 0.05f, Vector3.up, 0.12f);
            }
        }

        // ---------------------------------------------------------------- Selection

        /// <summary>The meshes in the selection and under it, and every collider there.</summary>
        private static List<MeshFilter> SelectedPlatforms(out HashSet<Collider> colliders)
        {
            var platforms = new List<MeshFilter>();
            var seen = new HashSet<MeshFilter>();
            colliders = new HashSet<Collider>();

            foreach (GameObject root in Selection.gameObjects)
            {
                if (!root.scene.IsValid())
                {
                    continue;
                }

                foreach (Collider collider in root.GetComponentsInChildren<Collider>())
                {
                    colliders.Add(collider);
                }

                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null
                        || filter.name.StartsWith(WallPrefix, StringComparison.Ordinal)
                        || (filter.TryGetComponent(out Collider own) && own.isTrigger)
                        || !seen.Add(filter))
                    {
                        continue;
                    }

                    platforms.Add(filter);
                }
            }

            return platforms;
        }

        private Outline TraceSelection()
        {
            List<MeshFilter> platforms = SelectedPlatforms(out HashSet<Collider> colliders);
            return platforms.Count == 0 ? null : Trace(platforms, colliders);
        }

        // ---------------------------------------------------------------- Build

        private void Build()
        {
            List<MeshFilter> platforms = SelectedPlatforms(out HashSet<Collider> colliders);
            Outline outline = Trace(platforms, colliders);

            if (outline == null || outline.runs.Count == 0)
            {
                EditorUtility.DisplayDialog("Platform Wall Builder",
                    outline == null
                        ? "The selection has no flat tops to wall in."
                        : "Nothing to wall: there is somewhere to walk to all the way round the edge.",
                    "OK");
                return;
            }

            Mesh mesh = Extrude(outline, out Vector3 pivot);

            GameObject first = platforms[0].gameObject;
            GameObject named = Selection.activeGameObject != null ? Selection.activeGameObject : first;
            string label = Selection.gameObjects.Length > 1
                ? $"{named.name} and {Selection.gameObjects.Length - 1} more"
                : named.name;

            mesh.name = $"Wall_{string.Join("_", label.Split(System.IO.Path.GetInvalidFileNameChars()))}_{mesh.vertexCount}v";
            EnsureFolder();
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath($"{MeshFolder}/{mesh.name}.asset"));
            AssetDatabase.SaveAssets();

            var wall = new GameObject(WallPrefix + label);
            SceneManagerMove(wall, first);
            wall.transform.position = pivot;
            wall.layer = matchLayer ? first.layer : layer;
            wall.isStatic = true;

            wall.AddComponent<MeshFilter>().sharedMesh = mesh;
            wall.AddComponent<MeshCollider>().sharedMesh = mesh;

            if (visible)
            {
                MeshRenderer renderer = wall.AddComponent<MeshRenderer>();
                Material shown = material;

                if (shown == null && first.TryGetComponent(out Renderer platformRenderer))
                {
                    shown = platformRenderer.sharedMaterial;
                }

                renderer.sharedMaterial = shown;
            }

            Undo.RegisterCreatedObjectUndo(wall, "Build Platform Wall");
            EditorSceneManager.MarkSceneDirty(wall.scene);

            Debug.Log($"[PlatformWallBuilder] Built {wall.name}: {outline.runs.Count} pieces, " +
                      $"{outline.openings.Count} open points, mesh saved to {AssetDatabase.GetAssetPath(mesh)}.", wall);

            Selection.activeGameObject = wall;
        }

        private static void SceneManagerMove(GameObject wall, GameObject platform)
        {
            if (wall.scene != platform.scene)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wall, platform.scene);
            }
        }

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(MeshFolder))
            {
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/NeoNoir/Meshes"))
            {
                AssetDatabase.CreateFolder("Assets/NeoNoir", "Meshes");
            }

            AssetDatabase.CreateFolder("Assets/NeoNoir/Meshes", "Walls");
        }

        // ---------------------------------------------------------------- Tracing

        /// <summary>One unbroken stretch of wall along the edge. Points sit on the edge at floor height.</summary>
        private sealed class Run
        {
            public readonly List<Vector3> points = new List<Vector3>();
            public bool closed;

            /// <summary>+1 if the outside is to the left walking along the points, -1 if to the right.</summary>
            public float side;
        }

        private sealed class Outline
        {
            public readonly List<Run> runs = new List<Run>();
            public readonly List<Vector3> openings = new List<Vector3>();
        }

        /// <summary>The footprint of the platforms' tops, drawn into square cells.</summary>
        private sealed class Footprint
        {
            public readonly int width;
            public readonly int depth;
            public readonly float cell;

            /// <summary>World XZ of the centre of cell (0, 0).</summary>
            public readonly Vector2 origin;

            /// <summary>Height of the floor in each cell; negative infinity where there is none.</summary>
            public float[] floor;

            public Footprint(int width, int depth, float cell, Vector2 origin)
            {
                this.width = width;
                this.depth = depth;
                this.cell = cell;
                this.origin = origin;
                floor = new float[width * depth];

                for (int i = 0; i < floor.Length; i++)
                {
                    floor[i] = float.NegativeInfinity;
                }
            }

            public bool Solid(int x, int z)
            {
                return x >= 0 && z >= 0 && x < width && z < depth && !float.IsNegativeInfinity(floor[x + z * width]);
            }

            public bool Solid(Vector2 point)
            {
                return Solid(Mathf.RoundToInt((point.x - origin.x) / cell), Mathf.RoundToInt((point.y - origin.y) / cell));
            }

            public Vector2 Centre(int x, int z)
            {
                return origin + new Vector2(x, z) * cell;
            }

            /// <summary>Floor height at the edge, read from just inside it.</summary>
            public float FloorNear(Vector2 point, Vector2 inward)
            {
                for (float step = 0.5f; step <= 4f; step += 0.5f)
                {
                    Vector2 inside = point + inward * (cell * step);
                    int x = Mathf.RoundToInt((inside.x - origin.x) / cell);
                    int z = Mathf.RoundToInt((inside.y - origin.y) / cell);

                    if (Solid(x, z))
                    {
                        return floor[x + z * width];
                    }
                }

                // Nearest solid cell round about.
                int cx = Mathf.RoundToInt((point.x - origin.x) / cell);
                int cz = Mathf.RoundToInt((point.y - origin.y) / cell);
                float best = float.MaxValue;
                float found = 0f;

                for (int dz = -4; dz <= 4; dz++)
                {
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        if (Solid(cx + dx, cz + dz) && dx * dx + dz * dz < best)
                        {
                            best = dx * dx + dz * dz;
                            found = floor[cx + dx + (cz + dz) * width];
                        }
                    }
                }

                return found;
            }

            /// <summary>Grows the footprint by one cell all round, taking the highest floor next to each new cell.</summary>
            public void Dilate()
            {
                var grown = new float[floor.Length];

                for (int z = 0; z < depth; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float highest = float.NegativeInfinity;

                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx;
                                int nz = z + dz;

                                if (nx >= 0 && nz >= 0 && nx < width && nz < depth)
                                {
                                    highest = Mathf.Max(highest, floor[nx + nz * width]);
                                }
                            }
                        }

                        grown[x + z * width] = highest;
                    }
                }

                floor = grown;
            }

            /// <summary>Shrinks the footprint by one cell all round.</summary>
            public void Erode()
            {
                var shrunk = new float[floor.Length];

                for (int z = 0; z < depth; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        bool keep = true;

                        for (int dz = -1; dz <= 1 && keep; dz++)
                        {
                            for (int dx = -1; dx <= 1 && keep; dx++)
                            {
                                keep = Solid(x + dx, z + dz);
                            }
                        }

                        shrunk[x + z * width] = keep ? floor[x + z * width] : float.NegativeInfinity;
                    }
                }

                floor = shrunk;
            }
        }

        private Outline Trace(List<MeshFilter> platforms, HashSet<Collider> own)
        {
            Footprint footprint = Draw(platforms);

            if (footprint == null)
            {
                return null;
            }

            var outline = new Outline();
            float spacing = Mathf.Max(footprint.cell, 0.25f);

            Physics.SyncTransforms();

            foreach (List<Vector2> loop in Contours(footprint))
            {
                if (Mathf.Abs(Area(loop)) < MinLoopArea)
                {
                    continue;
                }

                List<Vector2> points = Resample(Smooth(loop, 2), spacing);
                AddLoop(points, footprint, own, Mathf.CeilToInt(minOpening / spacing), outline);
            }

            return outline;
        }

        /// <summary>Draws the tops of the platforms' meshes into a footprint, then closes the seams between them.</summary>
        private Footprint Draw(List<MeshFilter> platforms)
        {
            var triangles = new List<Vector3>();
            var bounds = new Bounds();
            bool any = false;

            foreach (MeshFilter filter in platforms)
            {
                Mesh mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Matrix4x4 toWorld = filter.transform.localToWorldMatrix;

                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = toWorld.MultiplyPoint3x4(vertices[i]);
                }

                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles)
                    {
                        continue;
                    }

                    int[] indices = mesh.GetTriangles(sub);

                    for (int i = 0; i + 2 < indices.Length; i += 3)
                    {
                        Vector3 a = vertices[indices[i]];
                        Vector3 b = vertices[indices[i + 1]];
                        Vector3 c = vertices[indices[i + 2]];

                        // Only the flat faces: tops (and their undersides,
                        // which cover the same ground).
                        Vector3 normal = Vector3.Cross(b - a, c - a);
                        float size = normal.magnitude;

                        if (size < 1e-8f || Mathf.Abs(normal.y) < size * MinFloorNormal)
                        {
                            continue;
                        }

                        triangles.Add(a);
                        triangles.Add(b);
                        triangles.Add(c);

                        if (!any)
                        {
                            bounds = new Bounds(a, Vector3.zero);
                            any = true;
                        }

                        bounds.Encapsulate(a);
                        bounds.Encapsulate(b);
                        bounds.Encapsulate(c);
                    }
                }
            }

            if (!any)
            {
                return null;
            }

            float cell = resolution;
            Vector3 extent = bounds.size;

            if (extent.x / cell * (extent.z / cell) > MaxCells)
            {
                cell = Mathf.Sqrt(extent.x * extent.z / MaxCells);
            }

            int close = Mathf.RoundToInt(closeGaps * 0.5f / cell);
            int pad = close + 3;
            var footprint = new Footprint(
                Mathf.CeilToInt(extent.x / cell) + 1 + pad * 2,
                Mathf.CeilToInt(extent.z / cell) + 1 + pad * 2,
                cell,
                new Vector2(bounds.min.x - pad * cell, bounds.min.z - pad * cell));

            for (int i = 0; i < triangles.Count; i += 3)
            {
                Fill(footprint, triangles[i], triangles[i + 1], triangles[i + 2]);
            }

            for (int i = 0; i < close; i++)
            {
                footprint.Dilate();
            }

            for (int i = 0; i < close; i++)
            {
                footprint.Erode();
            }

            return footprint;
        }

        /// <summary>Marks every cell whose centre the triangle covers, keeping the highest floor.</summary>
        private static void Fill(Footprint footprint, Vector3 a, Vector3 b, Vector3 c)
        {
            float determinant = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);

            if (Mathf.Abs(determinant) < 1e-9f)
            {
                return;
            }

            float cell = footprint.cell;
            Vector2 origin = footprint.origin;

            int x0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.x, b.x, c.x) - origin.x) / cell - 0.001f));
            int x1 = Mathf.Min(footprint.width - 1, Mathf.FloorToInt((Mathf.Max(a.x, b.x, c.x) - origin.x) / cell + 0.001f));
            int z0 = Mathf.Max(0, Mathf.CeilToInt((Mathf.Min(a.z, b.z, c.z) - origin.y) / cell - 0.001f));
            int z1 = Mathf.Min(footprint.depth - 1, Mathf.FloorToInt((Mathf.Max(a.z, b.z, c.z) - origin.y) / cell + 0.001f));

            const float Slack = -1e-4f;

            for (int z = z0; z <= z1; z++)
            {
                float pz = origin.y + z * cell;

                for (int x = x0; x <= x1; x++)
                {
                    float px = origin.x + x * cell;

                    float wa = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / determinant;
                    float wb = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / determinant;
                    float wc = 1f - wa - wb;

                    if (wa < Slack || wb < Slack || wc < Slack)
                    {
                        continue;
                    }

                    float y = wa * a.y + wb * b.y + wc * c.y;
                    int index = x + z * footprint.width;

                    if (y > footprint.floor[index])
                    {
                        footprint.floor[index] = y;
                    }
                }
            }
        }

        /// <summary>
        /// The footprint's edges, as closed loops: marching squares between
        /// cell centres. Where two solid cells touch only at a corner, they are
        /// joined.
        /// </summary>
        private static List<List<Vector2>> Contours(Footprint footprint)
        {
            int width = footprint.width;
            var links = new Dictionary<long, long[]>();

            long Edge(int x, int z, bool vertical)
            {
                return ((long)z * (width + 1) + x) * 2 + (vertical ? 1 : 0);
            }

            void Link(long from, long to)
            {
                Attach(links, from, to);
                Attach(links, to, from);
            }

            for (int z = 0; z < footprint.depth - 1; z++)
            {
                for (int x = 0; x < width - 1; x++)
                {
                    bool s0 = footprint.Solid(x, z);
                    bool s1 = footprint.Solid(x + 1, z);
                    bool s2 = footprint.Solid(x + 1, z + 1);
                    bool s3 = footprint.Solid(x, z + 1);

                    if (s0 == s1 && s1 == s2 && s2 == s3)
                    {
                        continue;
                    }

                    long bottom = Edge(x, z, false);
                    long right = Edge(x + 1, z, true);
                    long top = Edge(x, z + 1, false);
                    long left = Edge(x, z, true);

                    if (s0 == s2 && s1 == s3)
                    {
                        // Diagonal: cut off the two empty corners.
                        if (s0)
                        {
                            Link(bottom, right);
                            Link(top, left);
                        }
                        else
                        {
                            Link(left, bottom);
                            Link(right, top);
                        }

                        continue;
                    }

                    long first = -1;
                    long second = -1;

                    foreach ((bool crosses, long edge) in new[] { (s0 != s1, bottom), (s1 != s2, right), (s2 != s3, top), (s3 != s0, left) })
                    {
                        if (!crosses)
                        {
                            continue;
                        }

                        if (first < 0)
                        {
                            first = edge;
                        }
                        else
                        {
                            second = edge;
                        }
                    }

                    Link(first, second);
                }
            }

            Vector2 Position(long key)
            {
                long index = key >> 1;
                int x = (int)(index % (width + 1));
                int z = (int)(index / (width + 1));
                Vector2 half = (key & 1) == 1 ? new Vector2(0f, footprint.cell * 0.5f) : new Vector2(footprint.cell * 0.5f, 0f);
                return footprint.Centre(x, z) + half;
            }

            var loops = new List<List<Vector2>>();
            var visited = new HashSet<long>();

            foreach (long start in links.Keys)
            {
                if (visited.Contains(start))
                {
                    continue;
                }

                var loop = new List<Vector2>();
                long previous = -1;
                long current = start;

                while (true)
                {
                    visited.Add(current);
                    loop.Add(Position(current));

                    long[] pair = links[current];
                    long next = pair[0] != previous ? pair[0] : pair[1];

                    if (next < 0 || next == start || visited.Contains(next))
                    {
                        break;
                    }

                    previous = current;
                    current = next;
                }

                if (loop.Count >= 3)
                {
                    loops.Add(loop);
                }
            }

            return loops;
        }

        private static void Attach(Dictionary<long, long[]> links, long from, long to)
        {
            if (!links.TryGetValue(from, out long[] pair))
            {
                pair = new long[] { -1, -1 };
                links.Add(from, pair);
            }

            if (pair[0] < 0)
            {
                pair[0] = to;
            }
            else
            {
                pair[1] = to;
            }
        }

        /// <summary>Splits one loop of edge into wall runs, leaving openings where there is floor outside.</summary>
        private void AddLoop(List<Vector2> points, Footprint footprint, HashSet<Collider> own, int shortestOpening, Outline outline)
        {
            int count = points.Count;

            if (count < 3)
            {
                return;
            }

            // Which side is outside: whichever side of the line the footprint is not on.
            var lefts = new Vector2[count];
            int solidOnLeft = 0;

            for (int i = 0; i < count; i++)
            {
                Vector2 along = points[(i + 1) % count] - points[(i - 1 + count) % count];
                lefts[i] = new Vector2(-along.y, along.x).normalized;

                if (footprint.Solid(points[i] + lefts[i] * (footprint.cell * 2f)))
                {
                    solidOnLeft++;
                }
            }

            float side = solidOnLeft * 2 > count ? -1f : 1f;

            var edge = new Vector3[count];
            var open = new bool[count];

            for (int i = 0; i < count; i++)
            {
                Vector2 outward = lefts[i] * side;
                edge[i] = new Vector3(points[i].x, footprint.FloorNear(points[i], -outward), points[i].y);
                open[i] = leaveOpenings && IsWalkway(edge[i], new Vector3(outward.x, 0f, outward.y), own);
            }

            WallOverNarrowOpenings(open, shortestOpening);

            int firstOpen = Array.IndexOf(open, true);

            if (firstOpen < 0)
            {
                var whole = new Run { closed = true, side = side };
                whole.points.AddRange(Simplify(new List<Vector3>(edge), true, smoothness));
                outline.runs.Add(whole);
                return;
            }

            var stretch = new List<Vector3>();

            for (int step = 1; step <= count; step++)
            {
                int i = (firstOpen + step) % count;

                if (!open[i])
                {
                    stretch.Add(edge[i]);
                    continue;
                }

                outline.openings.Add(edge[i]);
                EndRun(stretch, side, outline);
            }

            EndRun(stretch, side, outline);
        }

        private void EndRun(List<Vector3> stretch, float side, Outline outline)
        {
            if (stretch.Count >= 2)
            {
                var run = new Run { closed = false, side = side };
                run.points.AddRange(Simplify(stretch, false, smoothness));
                outline.runs.Add(run);
            }

            stretch.Clear();
        }

        /// <summary>Closes openings shorter than <paramref name="shortest"/> points, round the loop.</summary>
        private static void WallOverNarrowOpenings(bool[] open, int shortest)
        {
            int count = open.Length;
            int firstClosed = Array.IndexOf(open, false);

            if (firstClosed < 0 || shortest <= 1)
            {
                return;
            }

            int start = -1;

            for (int step = 1; step <= count; step++)
            {
                int i = (firstClosed + step) % count;

                if (open[i])
                {
                    if (start < 0)
                    {
                        start = step;
                    }

                    continue;
                }

                if (start >= 0 && step - start < shortest)
                {
                    for (int k = start; k < step; k++)
                    {
                        open[(firstClosed + k) % count] = false;
                    }
                }

                start = -1;
            }
        }

        /// <summary>Whether there is floor just outside this point of the edge to walk on to.</summary>
        private bool IsWalkway(Vector3 edge, Vector3 outward, HashSet<Collider> own)
        {
            Vector3 probe = edge + outward * (Mathf.Max(0f, thickness - inset) + openingProbe);
            probe.y = edge.y + maxStep + 0.25f;

            RaycastHit[] hits = Physics.RaycastAll(probe, Vector3.down, maxStep * 2f + 0.5f, ~0, QueryTriggerInteraction.Ignore);

            foreach (RaycastHit hit in hits)
            {
                if (own.Contains(hit.collider)
                    || hit.collider.name.StartsWith(WallPrefix, StringComparison.Ordinal)
                    || hit.normal.y < MinFloorNormal)
                {
                    continue;
                }

                if (Mathf.Abs(hit.point.y - edge.y) <= maxStep)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- Lines

        private static float Area(List<Vector2> loop)
        {
            float area = 0f;

            for (int i = 0; i < loop.Count; i++)
            {
                Vector2 a = loop[i];
                Vector2 b = loop[(i + 1) % loop.Count];
                area += a.x * b.y - b.x * a.y;
            }

            return area * 0.5f;
        }

        /// <summary>Rounds off the traced staircase: each point pulled toward its neighbours.</summary>
        private static List<Vector2> Smooth(List<Vector2> loop, int passes)
        {
            int count = loop.Count;
            var from = new List<Vector2>(loop);
            var to = new List<Vector2>(count);

            for (int pass = 0; pass < passes; pass++)
            {
                to.Clear();

                for (int i = 0; i < count; i++)
                {
                    to.Add((from[(i - 1 + count) % count] + from[i] * 2f + from[(i + 1) % count]) * 0.25f);
                }

                (from, to) = (to, from);
            }

            return from;
        }

        /// <summary>Evenly spaced points round a closed loop.</summary>
        private static List<Vector2> Resample(List<Vector2> loop, float spacing)
        {
            int count = loop.Count;
            float perimeter = 0f;

            for (int i = 0; i < count; i++)
            {
                perimeter += Vector2.Distance(loop[i], loop[(i + 1) % count]);
            }

            int samples = Mathf.Max(3, Mathf.RoundToInt(perimeter / spacing));
            float step = perimeter / samples;
            var result = new List<Vector2>(samples);

            int segment = 0;
            float segmentStart = 0f;
            float segmentLength = Vector2.Distance(loop[0], loop[1 % count]);

            for (int k = 0; k < samples; k++)
            {
                float distance = k * step;

                while (segmentStart + segmentLength < distance && segment < count - 1)
                {
                    segmentStart += segmentLength;
                    segment++;
                    segmentLength = Vector2.Distance(loop[segment], loop[(segment + 1) % count]);
                }

                float t = segmentLength > 0f ? Mathf.Clamp01((distance - segmentStart) / segmentLength) : 0f;
                result.Add(Vector2.Lerp(loop[segment], loop[(segment + 1) % count], t));
            }

            return result;
        }

        /// <summary>Drops points the line can do without, staying within <paramref name="tolerance"/> (Douglas-Peucker).</summary>
        private static List<Vector3> Simplify(List<Vector3> points, bool closed, float tolerance)
        {
            var line = new List<Vector3>(points);

            if (closed)
            {
                line.Add(points[0]);
            }

            if (line.Count < 3)
            {
                return points;
            }

            var keep = new bool[line.Count];
            keep[0] = true;
            keep[line.Count - 1] = true;

            var pending = new Stack<(int, int)>();
            pending.Push((0, line.Count - 1));

            while (pending.Count > 0)
            {
                (int from, int to) = pending.Pop();
                float farthest = 0f;
                int at = -1;

                for (int i = from + 1; i < to; i++)
                {
                    float distance = DistanceToSegment(line[i], line[from], line[to]);

                    if (distance > farthest)
                    {
                        farthest = distance;
                        at = i;
                    }
                }

                if (at >= 0 && farthest > tolerance)
                {
                    keep[at] = true;
                    pending.Push((from, at));
                    pending.Push((at, to));
                }
            }

            var result = new List<Vector3>();
            int last = closed ? line.Count - 1 : line.Count;

            for (int i = 0; i < last; i++)
            {
                if (keep[i])
                {
                    result.Add(line[i]);
                }
            }

            return result;
        }

        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;

            if (lengthSquared < 1e-10f)
            {
                return Vector3.Distance(point, a);
            }

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSquared);
            return Vector3.Distance(point, a + ab * t);
        }

        // ---------------------------------------------------------------- Mesh

        private sealed class MeshBuilder
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();

            /// <summary>A flat-shaded quad, wound to face <paramref name="facing"/>.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f)
                {
                    (b, d) = (d, b);
                }

                int start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                vertices.Add(d);

                Vector3 normal = facing.normalized;

                for (int i = 0; i < 4; i++)
                {
                    normals.Add(normal);
                }

                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }
        }

        /// <summary>Stands every run up into a wall, in one mesh centred on <paramref name="pivot"/>.</summary>
        private Mesh Extrude(Outline outline, out Vector3 pivot)
        {
            var builder = new MeshBuilder();

            foreach (Run run in outline.runs)
            {
                ExtrudeRun(run, builder);
            }

            var bounds = new Bounds(builder.vertices[0], Vector3.zero);

            foreach (Vector3 vertex in builder.vertices)
            {
                bounds.Encapsulate(vertex);
            }

            pivot = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            for (int i = 0; i < builder.vertices.Count; i++)
            {
                builder.vertices[i] -= pivot;
            }

            var mesh = new Mesh();

            if (builder.vertices.Count > 65000)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            mesh.SetVertices(builder.vertices);
            mesh.SetNormals(builder.normals);
            mesh.SetTriangles(builder.triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void ExtrudeRun(Run run, MeshBuilder builder)
        {
            List<Vector3> points = run.points;
            int count = points.Count;
            int segments = run.closed ? count : count - 1;

            if (segments < 1)
            {
                return;
            }

            var outward = new Vector3[segments];

            for (int s = 0; s < segments; s++)
            {
                Vector3 along = points[(s + 1) % count] - points[s];
                outward[s] = new Vector3(-along.z, 0f, along.x).normalized * run.side;
            }

            // Mitred, so the wall keeps its thickness round corners.
            var inner = new Vector3[count];
            var outer = new Vector3[count];

            for (int i = 0; i < count; i++)
            {
                Vector3 before = i > 0 ? outward[i - 1] : run.closed ? outward[segments - 1] : outward[0];
                Vector3 after = i < segments ? outward[i] : outward[segments - 1];
                Vector3 mitre = before + after;
                mitre = mitre.sqrMagnitude < 1e-6f ? after : mitre.normalized;
                mitre /= Mathf.Max(Vector3.Dot(mitre, after), 0.35f);

                inner[i] = points[i] - mitre * inset;
                outer[i] = points[i] + mitre * (thickness - inset);
            }

            Vector3 Low(Vector3[] side, int i) => side[i] + Vector3.down * sink;
            Vector3 High(Vector3[] side, int i) => side[i] + Vector3.up * height;

            for (int s = 0; s < segments; s++)
            {
                int i = s;
                int j = (s + 1) % count;

                builder.Quad(Low(inner, i), High(inner, i), High(inner, j), Low(inner, j), -outward[s]);
                builder.Quad(Low(outer, i), High(outer, i), High(outer, j), Low(outer, j), outward[s]);
                builder.Quad(High(inner, i), High(outer, i), High(outer, j), High(inner, j), Vector3.up);
                builder.Quad(Low(inner, i), Low(outer, i), Low(outer, j), Low(inner, j), Vector3.down);
            }

            if (!run.closed)
            {
                Vector3 startFacing = Flat(points[0] - points[1]);
                Vector3 endFacing = Flat(points[count - 1] - points[count - 2]);

                builder.Quad(Low(inner, 0), High(inner, 0), High(outer, 0), Low(outer, 0), startFacing);
                builder.Quad(Low(inner, count - 1), High(inner, count - 1), High(outer, count - 1), Low(outer, count - 1), endFacing);
            }
        }

        private static Vector3 Flat(Vector3 direction)
        {
            direction.y = 0f;
            return direction.normalized;
        }
    }
}
