using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// Blue's secondary: a ring drawn on the floor where it was cast. The
    /// player attacks faster while standing inside it.
    /// </summary>
    /// <remarks>
    /// The ring is a filled disc with a brighter rim round its edge. It
    /// starts small and snaps out to its full size with a slight
    /// overshoot, holds, then dissolves over its last moments. Thin lines of
    /// light rise off the ring the whole time, like mana.
    ///
    /// The walls. Just after the ring snaps out, its edge extends upward
    /// into a cylinder drawn as a sheet of music: the staff's lines run
    /// round it at even heights, with bar lines up it at even angles. Only
    /// the lines are drawn, so the walls read as see-through without a
    /// transparent shader. Notes copied from the banjo's note prefabs (see
    /// <see cref="NoteModels"/>) circle round inside the staff, each sitting
    /// on a line or in a space and now and then hopping to the next one up
    /// or down. They lie flat on the wall, facing out, unless set to face
    /// the camera.
    ///
    /// The attack speed buff is read, not pushed: every blade asks
    /// <see cref="AttackSpeedAt"/> for its owner's position each frame, so
    /// stepping out of the ring ends the buff at once and overlapping rings
    /// never stack -- the strongest wins. The buff covers the full radius for
    /// the whole lifetime, expansion and fade included.
    ///
    /// Spawned at runtime and builds its own meshes, so it needs no prefab.
    /// </remarks>
    public sealed class BluePerimeter : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Radius of the ring, and of the area that grants the buff.")]
            [Min(0.5f)] public float radius = 3.5f;

            [Tooltip("Seconds the ring lasts.")]
            [Min(0.1f)] public float duration = 3f;

            [Tooltip("What attacks per second are multiplied by while standing inside.")]
            [Min(1f)] public float attackSpeed = 2f;

            [Tooltip("Seconds the ring takes to expand to full size.")]
            [Min(0.01f)] public float expandTime = 0.2f;

            [Tooltip("Size the ring starts at, as a fraction of its full radius.")]
            [Range(0f, 1f)] public float startSize = 0.1f;

            [Tooltip("How hard the ring overshoots full size before settling back. 0 for none.")]
            [Range(0f, 1f)] public float overshoot = 0.4f;

            [Tooltip("Seconds at the end of its life the ring takes to dissolve.")]
            [Min(0.01f)] public float fadeTime = 0.3f;

            [Tooltip("Width of the ring's outline.")]
            [Min(0.01f)] public float thickness = 0.12f;

            [Tooltip("Height above the floor the ring is drawn at.")]
            [Range(0f, 1f)] public float floorOffset = 0.05f;

            [Tooltip("Colour intensity of the rim. 1 is the material's own colour; above 1 blooms.")]
            [Min(0f)] public float intensity = 2f;

            [Tooltip("Colour intensity of the filled inside. Keep it below the rim's so the edge still reads.")]
            [Min(0f)] public float fillIntensity = 0.6f;

            [Header("Staff walls")]
            [Tooltip("Height of the staff's lowest line above the ring.")]
            [Min(0f)] public float staffBottom = 0.5f;

            [Tooltip("Height of the staff's highest line above the ring.")]
            [Min(0f)] public float staffTop = 1.7f;

            [Tooltip("Lines in the staff. Sheet music has five.")]
            [Range(2, 9)] public int staffLines = 5;

            [Tooltip("Thickness of each staff line.")]
            [Min(0.005f)] public float staffLineWidth = 0.03f;

            [Tooltip("Vertical bar lines spaced evenly round the wall. 0 for none.")]
            [Range(0, 32)] public int barLines = 8;

            [Tooltip("Width of each bar line.")]
            [Min(0.005f)] public float barLineWidth = 0.03f;

            [Tooltip("Seconds after casting that the walls start to rise.")]
            [Min(0f)] public float wallDelay = 0.1f;

            [Tooltip("Seconds the walls take to rise to full height.")]
            [Min(0.01f)] public float wallRiseTime = 0.25f;

            [Tooltip("Colour intensity of the staff. 1 is the material's own colour; above 1 blooms.")]
            [Min(0f)] public float wallIntensity = 1.5f;

            [Header("Staff notes")]
            [Tooltip("Notes circling inside the staff.")]
            [Range(0, 24)] public int staffNotes = 8;

            [Tooltip("Uniform scale applied to each staff note's prefab.")]
            [Min(0.01f)] public float staffNoteScale = 0.35f;

            [Tooltip("Degrees per second the notes circle the wall at. Negative turns the other way.")]
            public float staffNoteSpeed = 40f;

            [Tooltip("Average seconds between a note hopping to the next line or space.")]
            [Min(0.05f)] public float staffNoteHop = 0.6f;

            [Tooltip("Seconds a hop takes.")]
            [Min(0.01f)] public float staffNoteHopTime = 0.12f;

            [Tooltip("Turn the notes to face the camera. Off lays them flat on the wall, facing out.")]
            public bool staffNotesFaceCamera;

            [Tooltip("Degrees to turn the note models by, if they do not face -Z (toward the viewer) as modelled.")]
            public Vector3 staffNoteRotation;

            [Header("Mana lines")]
            [Tooltip("Lines rising off the ring per second.")]
            [Min(0f)] public float linesPerSecond = 40f;

            [Tooltip("Speed lines rise at.")]
            [Min(0f)] public float riseSpeed = 2.5f;

            [Tooltip("Shortest and longest a line grows to.")]
            public Vector2 lineLength = new Vector2(0.3f, 0.8f);

            [Tooltip("Width of a line.")]
            [Min(0.005f)] public float lineWidth = 0.035f;

            [Tooltip("Seconds a line lives.")]
            [Min(0.05f)] public float lineLifetime = 0.7f;

            [Tooltip("How far inside or outside the ring a line may start.")]
            [Min(0f)] public float lineScatter = 0.15f;
        }

        private const int Segments = 64;
        private const int MaxLines = 256;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private static readonly List<BluePerimeter> active = new List<BluePerimeter>();

        private struct Line
        {
            public Vector3 Base;
            public float Length;
            public float Age;
            public float Lifetime;
        }

        private sealed class StaffNote
        {
            public Transform Transform;
            public Vector3 Scale;
            public float Yaw;
            public int Slot;
            public float Height;
            public float HopFrom;
            public float HopClock;
            public float NextHop;
        }

        private readonly List<StaffNote> staffNotes = new List<StaffNote>();

        private readonly List<Line> lines = new List<Line>();
        private readonly List<Vector3> lineVertices = new List<Vector3>();
        private readonly List<int> lineTriangles = new List<int>();

        private Settings settings;
        private float age;
        private float lineDebt;

        private GameObject ring;
        private Mesh ringMesh;
        private MeshRenderer ringRenderer;

        private GameObject fill;
        private Mesh fillMesh;
        private MeshRenderer fillRenderer;

        private GameObject wall;
        private Mesh wallMesh;
        private MeshRenderer wallRenderer;

        private GameObject lineObject;
        private Mesh lineMesh;
        private MeshRenderer lineRenderer;

        private MaterialPropertyBlock block;
        private Color baseColour = Color.white;
        private bool hasFade;

        /// <summary>
        /// The attack speed multiplier for someone standing at
        /// <paramref name="position"/>: the strongest of the rings it is
        /// inside, or 1 outside them all. Height is ignored.
        /// </summary>
        public static float AttackSpeedAt(Vector3 position)
        {
            float best = 1f;

            foreach (BluePerimeter perimeter in active)
            {
                Vector3 offset = position - perimeter.transform.position;
                offset.y = 0f;

                if (offset.sqrMagnitude <= perimeter.settings.radius * perimeter.settings.radius)
                {
                    best = Mathf.Max(best, perimeter.settings.attackSpeed);
                }
            }

            return best;
        }

        /// <summary>
        /// Draws a ring centred on <paramref name="floorPoint"/>, a point on
        /// the floor. Its staff notes are picked from <paramref name="notePrefabs"/>.
        /// </summary>
        public static BluePerimeter Create(Vector3 floorPoint, Settings settings, Material material,
                                           IReadOnlyList<GameObject> notePrefabs)
        {
            GameObject host = new GameObject("BluePerimeter");
            BluePerimeter perimeter = host.AddComponent<BluePerimeter>();
            perimeter.Begin(floorPoint, settings, material, notePrefabs);
            return perimeter;
        }

        private void Begin(Vector3 floorPoint, Settings perimeterSettings, Material material,
                           IReadOnlyList<GameObject> notePrefabs)
        {
            settings = perimeterSettings;
            transform.position = floorPoint + Vector3.up * settings.floorOffset;

            if (material != null)
            {
                hasFade = material.HasProperty(FadeId);

                if (material.HasProperty(BaseColorId))
                {
                    baseColour = material.GetColor(BaseColorId);
                }
            }

            block = new MaterialPropertyBlock();

            ring = new GameObject("Ring");
            ring.transform.SetParent(transform, false);

            ringMesh = BlueMark.AnnulusMesh(settings.radius - settings.thickness * 0.5f,
                                            settings.radius + settings.thickness * 0.5f, Segments);
            ring.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            ringRenderer = AddRenderer(ring, material);

            // Inside the rim and a hair below it, so the two never fight
            // over the same pixels.
            fill = new GameObject("Fill");
            fill.transform.SetParent(transform, false);
            fill.transform.localPosition = Vector3.down * 0.01f;

            fillMesh = BlueMark.AnnulusMesh(0f, settings.radius - settings.thickness * 0.5f, Segments);
            fill.AddComponent<MeshFilter>().sharedMesh = fillMesh;
            fillRenderer = AddRenderer(fill, material);

            // Built at full size and scaled: out with the ring, up as it rises.
            wall = new GameObject("StaffWall");
            wall.transform.SetParent(transform, false);

            wallMesh = StaffMesh();
            wall.AddComponent<MeshFilter>().sharedMesh = wallMesh;
            wallRenderer = AddRenderer(wall, material);

            SpawnStaffNotes(notePrefabs);

            // Its own unscaled object, so the ring's expansion never stretches
            // the lines. Vertices are written in this object's local space.
            lineObject = new GameObject("ManaLines");
            lineObject.transform.SetParent(transform, false);

            lineMesh = new Mesh { name = "Blue mana lines" };
            lineMesh.MarkDynamic();
            lineObject.AddComponent<MeshFilter>().sharedMesh = lineMesh;
            lineRenderer = AddRenderer(lineObject, material);

            active.Add(this);
            UpdateVisuals();
        }

        private static MeshRenderer AddRenderer(GameObject host, Material material)
        {
            MeshRenderer meshRenderer = host.AddComponent<MeshRenderer>();
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;

            if (material != null)
            {
                meshRenderer.sharedMaterial = material;
            }

            return meshRenderer;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (age >= settings.duration)
            {
                Destroy(gameObject);
                return;
            }

            SpawnLines(deltaTime);
            AgeLines(deltaTime);
            TickStaffNotes(deltaTime);
            UpdateVisuals();
        }

        /// <summary>The ring's size as a fraction of full: out from its start size with an overshoot, then settled.</summary>
        private float Size()
        {
            float t = Mathf.Clamp01(age / settings.expandTime);

            // Ease-out-back: fast out of the gate, past full, and back.
            float c = settings.overshoot * 4f;
            float u = t - 1f;
            float eased = 1f + (c + 1f) * u * u * u + c * u * u;

            return Mathf.LerpUnclamped(settings.startSize, 1f, eased);
        }

        /// <summary>How far the walls have risen, 0 flat to 1 full height. Eased out, so they shoot up then settle.</summary>
        private float Rise()
        {
            float t = Mathf.Clamp01((age - settings.wallDelay) / settings.wallRiseTime);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        /// <summary>1 until the last <see cref="Settings.fadeTime"/> seconds, then down to 0.</summary>
        private float Visibility()
        {
            return Mathf.Clamp01((settings.duration - age) / settings.fadeTime);
        }

        private void SpawnLines(float deltaTime)
        {
            // Lines only start once the ring has reached its edge, and stop
            // as it dissolves.
            if (age < settings.expandTime || Visibility() < 1f)
            {
                return;
            }

            lineDebt += settings.linesPerSecond * deltaTime;

            while (lineDebt >= 1f && lines.Count < MaxLines)
            {
                lineDebt -= 1f;

                float yaw = Random.Range(0f, Mathf.PI * 2f);
                float radius = settings.radius + Random.Range(-settings.lineScatter, settings.lineScatter);

                lines.Add(new Line
                {
                    Base = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)) * radius,
                    Length = Random.Range(settings.lineLength.x, Mathf.Max(settings.lineLength.x, settings.lineLength.y)),
                    Lifetime = settings.lineLifetime * Random.Range(0.7f, 1f),
                });
            }

            lineDebt = Mathf.Min(lineDebt, 1f);
        }

        private void AgeLines(float deltaTime)
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                Line line = lines[i];
                line.Age += deltaTime;

                if (line.Age >= line.Lifetime)
                {
                    lines.RemoveAt(i);
                    continue;
                }

                lines[i] = line;
            }
        }

        private void UpdateVisuals()
        {
            float size = Size();
            float visibility = Visibility();
            float ringScale = hasFade ? size : size * visibility;

            ring.transform.localScale = new Vector3(ringScale, 1f, ringScale);
            fill.transform.localScale = new Vector3(ringScale, 1f, ringScale);

            float rise = Rise();
            float wallHeight = hasFade ? rise : rise * visibility;

            // A wall flattened to nothing is hidden outright rather than
            // left as a degenerate sliver at the floor.
            wall.SetActive(wallHeight > 0.001f);
            wall.transform.localScale = new Vector3(size, Mathf.Max(0.001f, wallHeight), size);

            PlaceStaffNotes(size, rise, visibility);

            block.SetFloat(FadeId, visibility);

            block.SetColor(BaseColorId, Intensified(settings.fillIntensity));
            fillRenderer.SetPropertyBlock(block);

            block.SetColor(BaseColorId, Intensified(settings.wallIntensity));
            wallRenderer.SetPropertyBlock(block);

            block.SetColor(BaseColorId, Intensified(settings.intensity));
            ringRenderer.SetPropertyBlock(block);
            lineRenderer.SetPropertyBlock(block);

            BuildLines();
        }

        // ---- staff --------------------------------------------------------

        /// <summary>Lines and spaces in the staff, bottom line to top line.</summary>
        private int StaffSlots => settings.staffLines * 2 - 1;

        /// <summary>Height of a line or space, 0 the bottom line, counting up through lines and spaces alternately.</summary>
        private float SlotHeight(int slot)
        {
            return Mathf.Lerp(settings.staffBottom, settings.staffTop, slot / (float)(StaffSlots - 1));
        }

        /// <summary>
        /// The staff at full size: a band round the cylinder for each line,
        /// and an upright strip for each bar line, every face drawn both
        /// ways so the wall shows from inside and out.
        /// </summary>
        private Mesh StaffMesh()
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            float radius = settings.radius;

            for (int line = 0; line < settings.staffLines; line++)
            {
                float height = SlotHeight(line * 2);
                float bottom = height - settings.staffLineWidth * 0.5f;
                float top = height + settings.staffLineWidth * 0.5f;

                for (int i = 0; i < Segments; i++)
                {
                    Vector3 a = Around(i / (float)Segments) * radius;
                    Vector3 b = Around((i + 1) / (float)Segments) * radius;

                    AddDoubleQuad(vertices, triangles,
                                  a + Vector3.up * bottom, b + Vector3.up * bottom,
                                  a + Vector3.up * top, b + Vector3.up * top);
                }
            }

            for (int i = 0; i < settings.barLines; i++)
            {
                Vector3 outward = Around(i / (float)settings.barLines);
                Vector3 side = Vector3.Cross(Vector3.up, outward) * (settings.barLineWidth * 0.5f);
                Vector3 foot = outward * radius;

                AddDoubleQuad(vertices, triangles,
                              foot - side + Vector3.up * settings.staffBottom, foot + side + Vector3.up * settings.staffBottom,
                              foot - side + Vector3.up * settings.staffTop, foot + side + Vector3.up * settings.staffTop);
            }

            Mesh staff = new Mesh { name = "Blue staff" };
            staff.SetVertices(vertices);
            staff.SetTriangles(triangles, 0);
            staff.RecalculateNormals();
            staff.RecalculateBounds();
            return staff;
        }

        /// <summary>A unit direction round the circle, 0 to 1 a full turn from +Z.</summary>
        private static Vector3 Around(float turn)
        {
            float yaw = turn * Mathf.PI * 2f;
            return new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
        }

        /// <summary>A quad from its four corners, both windings, so it shows from either side.</summary>
        private static void AddDoubleQuad(List<Vector3> vertices, List<int> triangles,
                                          Vector3 bottomA, Vector3 bottomB, Vector3 topA, Vector3 topB)
        {
            int at = vertices.Count;
            vertices.Add(bottomA);
            vertices.Add(bottomB);
            vertices.Add(topA);
            vertices.Add(topB);

            triangles.AddRange(new[] { at, at + 2, at + 1, at + 1, at + 2, at + 3 });
            triangles.AddRange(new[] { at, at + 1, at + 2, at + 1, at + 3, at + 2 });
        }

        private void SpawnStaffNotes(IReadOnlyList<GameObject> notePrefabs)
        {
            if (notePrefabs == null || notePrefabs.Count == 0)
            {
                return;
            }

            float offset = Random.Range(0f, 360f);

            for (int i = 0; i < settings.staffNotes; i++)
            {
                GameObject prefab = notePrefabs[Random.Range(0, notePrefabs.Count)];

                if (prefab == null)
                {
                    continue;
                }

                GameObject made = NoteModels.Spawn(prefab);
                made.name = "StaffNote";
                made.transform.SetParent(transform, false);

                int slot = Random.Range(0, StaffSlots);

                staffNotes.Add(new StaffNote
                {
                    Transform = made.transform,
                    Scale = made.transform.localScale * settings.staffNoteScale,
                    Yaw = offset + 360f * i / settings.staffNotes + Random.Range(-10f, 10f),
                    Slot = slot,
                    Height = SlotHeight(slot),
                    HopFrom = SlotHeight(slot),
                    HopClock = settings.staffNoteHopTime,
                    NextHop = settings.staffNoteHop * Random.Range(0.5f, 1.5f),
                });
            }
        }

        private void TickStaffNotes(float deltaTime)
        {
            foreach (StaffNote note in staffNotes)
            {
                note.Yaw = Mathf.Repeat(note.Yaw + settings.staffNoteSpeed * deltaTime, 360f);

                note.NextHop -= deltaTime;

                if (note.NextHop <= 0f)
                {
                    // One step up or down, turned back at the top and bottom.
                    int step = Random.value < 0.5f ? -1 : 1;
                    if (note.Slot + step < 0 || note.Slot + step >= StaffSlots)
                    {
                        step = -step;
                    }

                    note.HopFrom = note.Height;
                    note.Slot = Mathf.Clamp(note.Slot + step, 0, StaffSlots - 1);
                    note.HopClock = 0f;
                    note.NextHop = settings.staffNoteHop * Random.Range(0.5f, 1.5f);
                }

                note.HopClock += deltaTime;
                float t = Mathf.Clamp01(note.HopClock / settings.staffNoteHopTime);
                note.Height = Mathf.Lerp(note.HopFrom, SlotHeight(note.Slot), t * t * (3f - 2f * t));
            }
        }

        /// <summary>
        /// Puts each note on the wall at its angle and height: out with the
        /// ring, up with the walls, and shrinking away as everything fades.
        /// </summary>
        private void PlaceStaffNotes(float size, float rise, float visibility)
        {
            if (staffNotes.Count == 0)
            {
                return;
            }

            Camera camera = Camera.main;
            float shown = rise * visibility;

            foreach (StaffNote note in staffNotes)
            {
                if (note.Transform == null)
                {
                    continue;
                }

                Vector3 outward = Around(note.Yaw / 360f);
                Vector3 position = transform.position
                                 + outward * (settings.radius * size)
                                 + Vector3.up * (note.Height * rise);

                Quaternion facing = settings.staffNotesFaceCamera && camera != null
                    ? camera.transform.rotation
                    : Quaternion.LookRotation(-outward, Vector3.up);

                note.Transform.SetPositionAndRotation(position, facing * Quaternion.Euler(settings.staffNoteRotation));
                note.Transform.localScale = note.Scale * shown;
                note.Transform.gameObject.SetActive(shown > 0.001f);
            }
        }

        private Color Intensified(float intensity)
        {
            Color colour = baseColour * intensity;
            colour.a = baseColour.a;
            return colour;
        }

        /// <summary>
        /// Every live line as an upright strip turned to face the camera:
        /// grows to full length as it leaves the ring, rises, and thins away
        /// at the end of its life.
        /// </summary>
        private void BuildLines()
        {
            lineVertices.Clear();
            lineTriangles.Clear();

            Camera camera = Camera.main;
            Vector3 side = Vector3.right;

            if (camera != null)
            {
                // Across the screen, flattened, so the strips stay upright.
                Vector3 flat = Vector3.ProjectOnPlane(camera.transform.right, Vector3.up);
                if (flat.sqrMagnitude > 0.0001f)
                {
                    side = transform.InverseTransformDirection(flat.normalized);
                }
            }

            foreach (Line line in lines)
            {
                float t = line.Age / line.Lifetime;
                float grow = Mathf.Clamp01(t / 0.25f);
                float thin = 1f - Mathf.Clamp01((t - 0.6f) / 0.4f);

                float half = settings.lineWidth * 0.5f * thin;
                Vector3 bottom = line.Base + Vector3.up * (settings.riseSpeed * line.Age);
                Vector3 top = bottom + Vector3.up * (line.Length * grow);

                int at = lineVertices.Count;
                lineVertices.Add(bottom - side * half);
                lineVertices.Add(bottom + side * half);
                lineVertices.Add(top - side * half);
                lineVertices.Add(top + side * half);

                // Both windings, so the strip shows whichever way it faces.
                lineTriangles.AddRange(new[] { at, at + 2, at + 1, at + 1, at + 2, at + 3 });
                lineTriangles.AddRange(new[] { at, at + 1, at + 2, at + 1, at + 3, at + 2 });
            }

            lineMesh.Clear();
            lineMesh.SetVertices(lineVertices);
            lineMesh.SetTriangles(lineTriangles, 0);
            lineMesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            active.Remove(this);

            if (ringMesh != null)
            {
                Destroy(ringMesh);
            }

            if (fillMesh != null)
            {
                Destroy(fillMesh);
            }

            if (wallMesh != null)
            {
                Destroy(wallMesh);
            }

            if (lineMesh != null)
            {
                Destroy(lineMesh);
            }
        }
    }
}
