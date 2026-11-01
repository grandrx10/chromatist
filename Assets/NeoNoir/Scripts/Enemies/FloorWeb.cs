using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A floor cut along a spider web of light, which then collapses: the
    /// floor of <see cref="SpearHallIntro"/>'s finale room.
    /// </summary>
    /// <remarks>
    /// The web is centred on a point: <see cref="Settings.spokes"/> straight
    /// cuts running out from it, crossed by rings of straight cuts,
    /// <see cref="Settings.ringSpacing"/> apart, each a little uneven.
    ///
    /// Built ahead of time with <see cref="Build"/>: the floor -- every mesh
    /// renderer under the given transform -- is cut along the web into
    /// pieces, held switched off. Then:
    ///   <see cref="ShowLines"/>  swaps the pieces in for the floor's looks
    ///                            and lights the cuts, spreading out from the
    ///                            centre.
    ///   <see cref="RaiseLight"/> raises fuzzy walls of light along the cuts
    ///                            near the centre, tallest there and falling
    ///                            off outward, waving.
    ///   <see cref="SetColour"/>  recolours and brightens the lot.
    ///   <see cref="Break"/>      switches off the floor's colliders, so
    ///                            whatever stands on it falls, and drops the
    ///                            pieces in a ripple out from the centre,
    ///                            tumbling, their light dissolving.
    ///
    /// Nothing here is see-through: the game's two-tone threshold would crush
    /// it. The walls' fuzz is NeoNoir/Flat's dithered fade, stronger in each
    /// band up the wall.
    ///
    /// The floor is expected to be flat and built Y up. The floor keeps its
    /// colliders until the break; the pieces have none.
    /// </remarks>
    public sealed class FloorWeb : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Web")]
            [Tooltip("Cuts running straight out from the centre.")]
            [Min(3)]
            public int spokes = 14;

            [Tooltip("How unevenly the spokes are spaced round, as a part of the even gap between them.")]
            [Range(0f, 0.45f)]
            public float spokeJitter = 0.25f;

            [Tooltip("Distance from the centre to the first ring.")]
            [Min(0.2f)]
            public float firstRing = 1.5f;

            [Tooltip("Distance between rings.")]
            [Min(0.3f)]
            public float ringSpacing = 2.2f;

            [Tooltip("How unevenly each ring meets each spoke, as a part of the ring spacing.")]
            [Range(0f, 0.45f)]
            public float ringJitter = 0.25f;

            [Header("Lines")]
            [Tooltip("Width of the glowing cuts on the floor.")]
            [Min(0.01f)]
            public float lineWidth = 0.12f;

            [Tooltip("Speed the cuts light up, spreading out from the centre.")]
            [Min(0.1f)]
            public float lineSpreadSpeed = 30f;

            [Tooltip("Seconds each cut takes to fade in once the spread reaches it.")]
            [Min(0.01f)]
            public float lineFadeTime = 0.4f;

            [Tooltip("Width of the outline round each cut, either side. 0 for none.")]
            [Min(0f)]
            public float lineOutline = 0.05f;

            [Tooltip("Colour of the outline round each cut.")]
            [ColorUsage(false, true)]
            public Color lineOutlineColour = Color.white;

            [Header("Walls of light")]
            [Tooltip("How far from the centre the walls of light reach. They are tallest at the centre and fall away to nothing here.")]
            [Min(0.1f)]
            public float lightRadius = 12f;

            [Tooltip("Tallest a wall of light gets, at the centre.")]
            [Min(0.01f)]
            public float lightHeight = 4f;

            [Tooltip("How uneven the walls' tops are: 0 is level, 1 dips some places to nothing.")]
            [Range(0f, 1f)]
            public float heightJitter = 0.6f;

            [Tooltip("Size of the bumps along the walls' tops.")]
            [Min(0.1f)]
            public float bumpSize = 1.5f;

            [Tooltip("Bands up each wall, each more dissolved than the one below, so the walls fade out upward.")]
            [Range(1, 8)]
            public int fuzzBands = 4;

            [Tooltip("How dissolved the top band is: 0 not at all, 1 gone.")]
            [Range(0f, 1f)]
            public float topFuzz = 0.75f;

            [Tooltip("Seconds each wall takes to rise.")]
            [Min(0.01f)]
            public float lightRiseTime = 0.35f;

            [Tooltip("Speed the walls spread out from the centre, rising as it reaches them.")]
            [Min(0.1f)]
            public float lightSpreadSpeed = 12f;

            [Tooltip("How much the walls bob up and down once risen, as a part of their height.")]
            [Range(0f, 1f)]
            public float waveAmount = 0.25f;

            [Tooltip("How fast the walls bob.")]
            [Min(0f)]
            public float waveSpeed = 5f;

            [Tooltip("Distance from one crest of the bobbing to the next, outward from the centre.")]
            [Min(0.1f)]
            public float waveLength = 6f;

            [Tooltip("Seconds the light on a piece takes to dissolve once it drops.")]
            [Min(0.01f)]
            public float lightFadeTime = 0.5f;

            [Header("Collapse")]
            [Tooltip("Thinnest a piece is, for a floor that is only a flat plane.")]
            [Min(0.05f)]
            public float minThickness = 0.4f;

            [Tooltip("Speed the collapse spreads out from the centre. The further a piece is, the later it drops.")]
            [Min(0.1f)]
            public float rippleSpeed = 35f;

            [Tooltip("How far out from the centre the floor is driven in hard. Pieces further out just drop.")]
            [Min(0.1f)]
            public float caveRadius = 14f;

            [Tooltip("Speed the pieces at the very centre are driven down at, falling off to nothing at the Cave Radius.")]
            [Min(0f)]
            public float caveForce = 25f;

            [Tooltip("Speed the pieces near the centre are pulled in toward it as they go down.")]
            [Min(0f)]
            public float caveInward = 4f;

            [Tooltip("Degrees a second the pieces near the centre tip in toward it, their inner edges first.")]
            [Min(0f)]
            public float caveTip = 200f;

            [Tooltip("Up to this many seconds' random delay added to each piece, so the ripple is ragged.")]
            [Min(0f)]
            public float delayJitter = 0.08f;

            [Tooltip("Downward pull on the falling pieces.")]
            [Min(0f)]
            public float gravity = 25f;

            [Tooltip("Slowest and fastest a piece tumbles, in degrees a second.")]
            public Vector2 spin = new Vector2(30f, 160f);

            [Tooltip("Fastest a piece drifts sideways as it drops.")]
            [Min(0f)]
            public float scatter = 1f;

            [Tooltip("Seconds a piece falls before it is switched off.")]
            [Min(0.1f)]
            public float fallTime = 4f;
        }

        /// <summary>One floor renderer: its outline, flat, wound anticlockwise, and how it looks.</summary>
        private sealed class Slab
        {
            public Vector2[] outline;
            public float top;
            public float thickness;
            public Material material;
        }

        private sealed class Piece
        {
            public Transform body;

            public MeshRenderer lines;
            public MeshRenderer outline;
            public float lineDelay;

            // The walls: a holder scaled up as they rise, and a renderer per band.
            public Transform light;
            public MeshRenderer[] bands;
            public float lightDelay;
            public float distance;

            public float dropDelay;
            public Vector3 velocity;
            public Vector3 spinAxis;
            public float spinSpeed;
        }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private Settings settings;
        private Transform floor;
        private Vector3 centre;
        private MaterialPropertyBlock block;

        private readonly List<Slab> slabs = new List<Slab>();
        private readonly List<Piece> pieces = new List<Piece>();

        // Each cut is lit on only one of the two pieces either side of it.
        private readonly HashSet<(Vector2Int, Vector2Int)> litEdges = new HashSet<(Vector2Int, Vector2Int)>();

        private Color lineColour = new Color(0.1f, 2.2f, 3f, 1f);
        private Color wallColour = new Color(0.1f, 2.2f, 3f, 1f);
        private float brightness = 1f;

        private bool linesShown;
        private bool lightRaised;
        private bool broken;
        private float lineClock;
        private float lightClock;
        private float dropClock;
        private float lastLight;

        /// <summary>The web's centre, on the floor's top.</summary>
        public Vector3 Centre => centre;

        /// <summary>Seconds from <see cref="RaiseLight"/> until every wall of light has risen.</summary>
        public float LightTime => lastLight + settings.lightRiseTime;

        /// <summary>
        /// Plans a web centred on <paramref name="webCentre"/> across the floor
        /// under <paramref name="floorRoot"/>, cutting the floor into its
        /// pieces. Null, with a warning, if there is no floor to cut.
        /// </summary>
        public static FloorWeb Build(Transform floorRoot, Vector3 webCentre, Settings settings)
        {
            GameObject host = new GameObject("FloorWeb");
            host.transform.SetParent(floorRoot.parent, false);

            FloorWeb web = host.AddComponent<FloorWeb>();
            web.settings = settings;
            web.floor = floorRoot;
            web.block = new MaterialPropertyBlock();

            foreach (MeshRenderer part in floorRoot.GetComponentsInChildren<MeshRenderer>())
            {
                web.AddSlab(part);
            }

            if (web.slabs.Count == 0)
            {
                Debug.LogWarning($"[FloorWeb] {floorRoot.name} has no mesh renderers to cut.", floorRoot);
                Destroy(host);
                return null;
            }

            web.centre = new Vector3(webCentre.x, web.TopAt(Flat(webCentre)), webCentre.z);
            web.Plan();
            return web;
        }

        /// <summary>
        /// Swaps the pieces in for the floor's looks -- nothing changes to
        /// see -- and lights the cuts, spreading out from the centre. The
        /// floor still holds up whatever is on it.
        /// </summary>
        public void ShowLines()
        {
            if (linesShown)
            {
                return;
            }

            linesShown = true;

            foreach (Renderer part in floor.GetComponentsInChildren<Renderer>())
            {
                part.enabled = false;
            }

            foreach (Piece piece in pieces)
            {
                piece.body.gameObject.SetActive(true);
            }
        }

        /// <summary>Raises the walls of light along the cuts near the centre, spreading outward.</summary>
        public void RaiseLight()
        {
            ShowLines();
            lightRaised = true;
        }

        /// <summary>
        /// Recolours the cuts <paramref name="lines"/> and the walls of light
        /// <paramref name="walls"/>, <paramref name="brightnessScale"/> times
        /// as bright.
        /// </summary>
        public void SetColour(Color lines, Color walls, float brightnessScale = 1f)
        {
            lineColour = lines;
            wallColour = walls;
            brightness = brightnessScale;
        }

        /// <summary>Seconds after the break that the floor at <paramref name="point"/> drops.</summary>
        public float DelayAt(Vector3 point)
        {
            return Vector2.Distance(Flat(point), Flat(centre)) / settings.rippleSpeed;
        }

        /// <summary>
        /// Switches off the floor's colliders and drops the pieces in a ripple
        /// out from the centre, their light dissolving.
        /// </summary>
        public void Break()
        {
            if (broken)
            {
                return;
            }

            ShowLines();
            broken = true;

            foreach (Collider part in floor.GetComponentsInChildren<Collider>())
            {
                part.enabled = false;
            }

            foreach (Piece piece in pieces)
            {
                Vector2 drift = UnityEngine.Random.insideUnitCircle * settings.scatter;
                Vector3 outward = piece.body.position - centre;
                outward.y = 0f;
                float distance = outward.magnitude;
                outward = distance > 0.01f ? outward / distance : Vector3.forward;

                // Driven down and in hardest at the centre, tipping inner edge first.
                float cave = 1f - Smooth(distance / settings.caveRadius);
                float tumble = UnityEngine.Random.Range(Mathf.Min(settings.spin.x, settings.spin.y), Mathf.Max(settings.spin.x, settings.spin.y));
                Vector3 spin = UnityEngine.Random.onUnitSphere * tumble + Vector3.Cross(outward, Vector3.up) * (settings.caveTip * cave);

                piece.dropDelay = DelayAt(piece.body.position) + UnityEngine.Random.Range(0f, settings.delayJitter);
                piece.velocity = new Vector3(drift.x, 0f, drift.y)
                               + Vector3.down * (settings.caveForce * cave)
                               - outward * (settings.caveInward * cave);
                piece.spinAxis = spin.sqrMagnitude > 0.0001f ? spin.normalized : Vector3.up;
                piece.spinSpeed = spin.magnitude;
            }
        }

        private void Update()
        {
            if (!linesShown)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            lineClock += deltaTime;

            if (lightRaised)
            {
                lightClock += deltaTime;
            }

            if (broken)
            {
                dropClock += deltaTime;
            }

            Color lines = lineColour * brightness;
            Color walls = wallColour * brightness;
            Color outline = settings.lineOutlineColour;
            lines.a = 1f;
            walls.a = 1f;
            outline.a = 1f;

            for (int i = pieces.Count - 1; i >= 0; i--)
            {
                Piece piece = pieces[i];

                if (piece.body == null || (broken && dropClock > piece.dropDelay + settings.fallTime))
                {
                    if (piece.body != null)
                    {
                        piece.body.gameObject.SetActive(false);
                    }

                    pieces.RemoveAt(i);
                    continue;
                }

                float fade = broken ? Mathf.Clamp01(1f - (dropClock - piece.dropDelay) / settings.lightFadeTime) : 1f;

                if (piece.lines != null)
                {
                    // Eased in once the spread reaches it, out as it drops.
                    float shown = Smooth((lineClock - piece.lineDelay) / settings.lineFadeTime) * fade;
                    piece.lines.enabled = shown > 0f;

                    if (shown > 0f)
                    {
                        Paint(piece.lines, lines, shown);
                    }

                    if (piece.outline != null)
                    {
                        piece.outline.enabled = shown > 0f;

                        if (shown > 0f)
                        {
                            Paint(piece.outline, outline, shown);
                        }
                    }
                }

                if (piece.light != null)
                {
                    float rise = lightRaised ? Smooth((lightClock - piece.lightDelay) / settings.lightRiseTime) : 0f;
                    float wave = 1f + settings.waveAmount * Mathf.Sin(lightClock * settings.waveSpeed - piece.distance * Mathf.PI * 2f / settings.waveLength);
                    bool showing = rise > 0f && fade > 0f;

                    piece.light.localScale = new Vector3(1f, Mathf.Max(0.0001f, rise * wave), 1f);

                    for (int b = 0; b < piece.bands.Length; b++)
                    {
                        piece.bands[b].enabled = showing;

                        if (showing)
                        {
                            Paint(piece.bands[b], walls, fade * BandFade(b));
                        }
                    }
                }

                if (broken && dropClock >= piece.dropDelay)
                {
                    piece.velocity += Vector3.down * (settings.gravity * deltaTime);
                    piece.body.position += piece.velocity * deltaTime;
                    piece.body.Rotate(piece.spinAxis, piece.spinSpeed * deltaTime, Space.World);
                }
            }
        }

        private void Paint(Renderer target, Color shown, float fade)
        {
            target.GetPropertyBlock(block);
            block.SetColor(FlatMaterial.BaseColorId, shown);
            block.SetFloat(FadeId, fade);
            target.SetPropertyBlock(block);
        }

        /// <summary>How solid band <paramref name="band"/> up a wall is: 1 at the bottom, thinning upward.</summary>
        private float BandFade(int band)
        {
            int count = Mathf.Max(1, settings.fuzzBands);
            return count <= 1 ? 1f : 1f - settings.topFuzz * band / (count - 1);
        }

        private void AddSlab(MeshRenderer part)
        {
            Transform from = part.transform;
            Bounds bounds = part.localBounds;

            if (bounds.size.x <= 0f || bounds.size.z <= 0f)
            {
                return;
            }

            Vector2[] outline = new Vector2[4];

            for (int i = 0; i < 4; i++)
            {
                Vector3 corner = new Vector3(i == 1 || i == 2 ? bounds.max.x : bounds.min.x,
                                             bounds.max.y,
                                             i >= 2 ? bounds.max.z : bounds.min.z);
                outline[i] = Flat(from.TransformPoint(corner));
            }

            if (Area(outline) < 0f)
            {
                Array.Reverse(outline);
            }

            slabs.Add(new Slab
            {
                outline = outline,
                top = from.TransformPoint(bounds.center + Vector3.up * bounds.extents.y).y,
                thickness = Mathf.Max(settings.minThickness, bounds.size.y * Mathf.Abs(from.lossyScale.y)),
                material = part.sharedMaterial,
            });
        }

        /// <summary>Lays out the spokes and rings, and cuts the floor into the pieces between.</summary>
        private void Plan()
        {
            Vector2 middle = Flat(centre);
            float reach = 0f;

            foreach (Slab slab in slabs)
            {
                foreach (Vector2 corner in slab.outline)
                {
                    reach = Mathf.Max(reach, Vector2.Distance(middle, corner));
                }
            }

            reach += settings.ringSpacing;

            int spokes = settings.spokes;
            float step = Mathf.PI * 2f / spokes;
            float turn = UnityEngine.Random.Range(0f, step);
            Vector2[] directions = new Vector2[spokes];

            for (int i = 0; i < spokes; i++)
            {
                float angle = turn + (i + UnityEngine.Random.Range(-settings.spokeJitter, settings.spokeJitter)) * step;
                directions[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }

            // Ring 0 is the centre itself; each later ring meets each spoke
            // a little nearer or further than its even spot.
            int rings = Mathf.Max(1, Mathf.CeilToInt((reach - settings.firstRing) / settings.ringSpacing) + 1);
            Vector2[,] knots = new Vector2[spokes, rings + 1];
            float[,] radii = new float[spokes, rings + 1];

            for (int i = 0; i < spokes; i++)
            {
                knots[i, 0] = middle;

                for (int k = 1; k <= rings; k++)
                {
                    float even = settings.firstRing + (k - 1) * settings.ringSpacing;
                    float wobble = k == 1 ? 0f : UnityEngine.Random.Range(-settings.ringJitter, settings.ringJitter) * settings.ringSpacing;
                    radii[i, k] = Mathf.Max(radii[i, k - 1] + 0.2f, even + wobble);
                    knots[i, k] = middle + directions[i] * radii[i, k];
                }
            }

            // From the centre out, so each cut is lit on the piece nearer the
            // centre, and lights up as soon as it can.
            for (int k = 0; k < rings; k++)
            {
                for (int i = 0; i < spokes; i++)
                {
                    int j = (i + 1) % spokes;
                    Vector2[] cell = k == 0
                        ? new[] { middle, knots[i, 1], knots[j, 1] }
                        : new[] { knots[i, k], knots[i, k + 1], knots[j, k + 1], knots[j, k] };

                    foreach (Slab slab in slabs)
                    {
                        MakePiece(Clip(cell, slab.outline), slab);
                    }
                }
            }
        }

        /// <summary>
        /// A piece of <paramref name="slab"/> shaped <paramref name="outline"/>,
        /// held switched off, lighting each of its cuts not already lit by a
        /// piece nearer the centre -- all but the edges that are the slab's own.
        /// </summary>
        private void MakePiece(List<Vector2> outline, Slab slab)
        {
            if (outline.Count < 3 || Mathf.Abs(Area(outline)) < 0.01f)
            {
                return;
            }

            Vector2 middle = Vector2.zero;

            foreach (Vector2 corner in outline)
            {
                middle += corner;
            }

            middle /= outline.Count;

            float half = slab.thickness * 0.5f;

            GameObject body = new GameObject("WebPiece");
            body.SetActive(false);
            body.transform.SetParent(transform, false);
            body.transform.position = new Vector3(middle.x, slab.top - half, middle.y);

            body.AddComponent<MeshFilter>().sharedMesh = Prism(outline, middle, half);
            body.AddComponent<MeshRenderer>().sharedMaterial = slab.material;

            // Every cut of this piece's to light, as world-flat pairs.
            List<Vector2> cuts = new List<Vector2>();
            float nearest = float.MaxValue;
            Vector2 webMiddle = Flat(centre);

            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i];
                Vector2 b = outline[(i + 1) % outline.Count];

                if (OnEdge(a, b, slab.outline) || !litEdges.Add(EdgeKey(a, b)))
                {
                    continue;
                }

                cuts.Add(a);
                cuts.Add(b);
                nearest = Mathf.Min(nearest, DistanceToSegment(webMiddle, a, b));
            }

            Piece piece = new Piece { body = body.transform, distance = nearest };

            if (cuts.Count > 0)
            {
                // The cut sits on its outline, a wider strip just under it.
                piece.lines = MakeRenderer(body.transform, "Lines", Vector3.up * half, Lines(cuts, middle, settings.lineWidth, 0.03f), FlatMaterial.Shared);

                if (settings.lineOutline > 0f)
                {
                    piece.outline = MakeRenderer(body.transform, "LineOutline", Vector3.up * half,
                                                 Lines(cuts, middle, settings.lineWidth + settings.lineOutline * 2f, 0.015f), FlatMaterial.Shared);
                }
                piece.lineDelay = nearest / settings.lineSpreadSpeed;

                if (nearest < settings.lightRadius)
                {
                    GameObject light = new GameObject("Light");
                    light.transform.SetParent(body.transform, false);
                    light.transform.localPosition = Vector3.up * half;

                    int bandCount = Mathf.Max(1, settings.fuzzBands);
                    piece.light = light.transform;
                    piece.bands = new MeshRenderer[bandCount];

                    for (int band = 0; band < bandCount; band++)
                    {
                        Mesh mesh = Walls(cuts, middle, (float)band / bandCount, (float)(band + 1) / bandCount);
                        piece.bands[band] = MakeRenderer(light.transform, "Band", Vector3.zero, mesh, FlatMaterial.DoubleSided);
                    }

                    piece.lightDelay = nearest / settings.lightSpreadSpeed;
                    lastLight = Mathf.Max(lastLight, piece.lightDelay);
                }
            }

            pieces.Add(piece);
        }

        private static MeshRenderer MakeRenderer(Transform parent, string objectName, Vector3 at, Mesh mesh, Material material)
        {
            GameObject host = new GameObject(objectName);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = at;
            host.AddComponent<MeshFilter>().sharedMesh = mesh;

            MeshRenderer renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enabled = false;
            return renderer;
        }

        /// <summary>The wall of light's height above the flat point <paramref name="point"/>: falling off from the centre, bumpy along the top.</summary>
        private float HeightAt(Vector2 point)
        {
            float out01 = Vector2.Distance(point, Flat(centre)) / settings.lightRadius;
            float falloff = 1f - Smooth(out01);

            // From the world position, so neighbouring pieces' walls meet.
            float bump = Mathf.PerlinNoise(point.x / settings.bumpSize + 31.7f, point.y / settings.bumpSize + 11.3f);
            return settings.lightHeight * falloff * (1f - settings.heightJitter * bump);
        }

        /// <summary>
        /// The band from <paramref name="from"/> to <paramref name="to"/> of
        /// the way up the walls of light on each cut in <paramref name="cuts"/>,
        /// around <paramref name="middle"/>.
        /// </summary>
        private Mesh Walls(List<Vector2> cuts, Vector2 middle, float from, float to)
        {
            const float Step = 0.5f;

            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int c = 0; c < cuts.Count; c += 2)
            {
                Vector2 a = cuts[c];
                Vector2 b = cuts[c + 1];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / Step));

                for (int s = 0; s < steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, (float)s / steps);
                    Vector2 q = Vector2.Lerp(a, b, (float)(s + 1) / steps);
                    float hp = HeightAt(p);
                    float hq = HeightAt(q);

                    int first = vertices.Count;
                    vertices.Add(new Vector3(p.x - middle.x, hp * from, p.y - middle.y));
                    vertices.Add(new Vector3(q.x - middle.x, hq * from, q.y - middle.y));
                    vertices.Add(new Vector3(q.x - middle.x, hq * to, q.y - middle.y));
                    vertices.Add(new Vector3(p.x - middle.x, hp * to, p.y - middle.y));

                    triangles.Add(first);
                    triangles.Add(first + 2);
                    triangles.Add(first + 1);
                    triangles.Add(first);
                    triangles.Add(first + 3);
                    triangles.Add(first + 2);
                }
            }

            Mesh mesh = new Mesh { name = "WebLight" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Flat strips <paramref name="width"/> wide, facing up,
        /// <paramref name="lift"/> above the top, along each cut in
        /// <paramref name="cuts"/>, around <paramref name="middle"/>.
        /// </summary>
        private static Mesh Lines(List<Vector2> cuts, Vector2 middle, float width, float lift)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();

            for (int c = 0; c < cuts.Count; c += 2)
            {
                Vector2 a = cuts[c] - middle;
                Vector2 b = cuts[c + 1] - middle;
                Vector2 along = (b - a).normalized;

                // Run on past each end by half a width, so corners close.
                a -= along * (width * 0.5f);
                b += along * (width * 0.5f);
                Vector2 side = new Vector2(-along.y, along.x) * (width * 0.5f);

                int first = vertices.Count;
                vertices.Add(new Vector3(a.x - side.x, lift, a.y - side.y));
                vertices.Add(new Vector3(a.x + side.x, lift, a.y + side.y));
                vertices.Add(new Vector3(b.x + side.x, lift, b.y + side.y));
                vertices.Add(new Vector3(b.x - side.x, lift, b.y - side.y));

                // Facing up.
                bool flip = Vector3.Cross(vertices[first + 1] - vertices[first], vertices[first + 2] - vertices[first]).y < 0f;
                triangles.AddRange(flip
                    ? new[] { first, first + 2, first + 1, first, first + 3, first + 2 }
                    : new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
            }

            Mesh mesh = new Mesh { name = "WebLines" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A slab of floor shaped <paramref name="outline"/>, its top at 0 + <paramref name="half"/>, around <paramref name="middle"/>.</summary>
        private static Mesh Prism(List<Vector2> outline, Vector2 middle, float half)
        {
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            int count = outline.Count;

            Vector3 Top(int i) => new Vector3(outline[i].x - middle.x, half, outline[i].y - middle.y);
            Vector3 Bottom(int i) => new Vector3(outline[i].x - middle.x, -half, outline[i].y - middle.y);

            // Faces turned to face away from the piece's middle.
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
                {
                    (b, c) = (c, b);
                }

                triangles.Add(vertices.Count);
                vertices.Add(a);
                triangles.Add(vertices.Count);
                vertices.Add(b);
                triangles.Add(vertices.Count);
                vertices.Add(c);
            }

            for (int i = 1; i < count - 1; i++)
            {
                Face(Top(0), Top(i), Top(i + 1), Vector3.up);
                Face(Bottom(0), Bottom(i), Bottom(i + 1), Vector3.down);
            }

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                Vector3 outward = (Top(i) + Top(j)) * 0.5f;
                outward.y = 0f;

                Face(Top(i), Top(j), Bottom(j), outward);
                Face(Top(i), Bottom(j), Bottom(i), outward);
            }

            Mesh mesh = new Mesh { name = "WebPiece" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 along = b - a;
            float t = along.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(point - a, along) / along.sqrMagnitude) : 0f;
            return Vector2.Distance(point, a + along * t);
        }

        /// <summary>The same for an edge whichever way round it is given, to a centimetre.</summary>
        private static (Vector2Int, Vector2Int) EdgeKey(Vector2 a, Vector2 b)
        {
            Vector2Int from = Vector2Int.RoundToInt(a * 100f);
            Vector2Int to = Vector2Int.RoundToInt(b * 100f);
            bool swap = from.x > to.x || (from.x == to.x && from.y > to.y);
            return swap ? (to, from) : (from, to);
        }

        /// <summary>The floor's top under <paramref name="flat"/>, or the highest top if it is off the floor.</summary>
        private float TopAt(Vector2 flat)
        {
            float highest = float.MinValue;

            foreach (Slab slab in slabs)
            {
                if (Inside(flat, slab.outline))
                {
                    return slab.top;
                }

                highest = Mathf.Max(highest, slab.top);
            }

            return highest;
        }

        /// <summary><paramref name="subject"/> cut down to what lies inside the anticlockwise convex <paramref name="clip"/>.</summary>
        private static List<Vector2> Clip(Vector2[] subject, Vector2[] clip)
        {
            List<Vector2> output = new List<Vector2>(subject);

            for (int e = 0; e < clip.Length && output.Count > 0; e++)
            {
                Vector2 a = clip[e];
                Vector2 b = clip[(e + 1) % clip.Length];
                List<Vector2> input = output;
                output = new List<Vector2>();

                for (int i = 0; i < input.Count; i++)
                {
                    Vector2 current = input[i];
                    Vector2 previous = input[(i + input.Count - 1) % input.Count];
                    float here = Cross(b - a, current - a);
                    float before = Cross(b - a, previous - a);

                    if (here >= 0f)
                    {
                        if (before < 0f)
                        {
                            output.Add(Vector2.Lerp(previous, current, before / (before - here)));
                        }

                        output.Add(current);
                    }
                    else if (before >= 0f)
                    {
                        output.Add(Vector2.Lerp(previous, current, before / (before - here)));
                    }
                }
            }

            return output;
        }

        /// <summary>True if the edge from <paramref name="a"/> to <paramref name="b"/> lies along an edge of <paramref name="outline"/>.</summary>
        private static bool OnEdge(Vector2 a, Vector2 b, Vector2[] outline)
        {
            const float Tolerance = 0.01f;

            for (int e = 0; e < outline.Length; e++)
            {
                Vector2 from = outline[e];
                Vector2 to = outline[(e + 1) % outline.Length];
                Vector2 along = (to - from).normalized;

                if (Mathf.Abs(Cross(along, a - from)) < Tolerance && Mathf.Abs(Cross(along, b - from)) < Tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Inside(Vector2 point, Vector2[] outline)
        {
            for (int e = 0; e < outline.Length; e++)
            {
                if (Cross(outline[(e + 1) % outline.Length] - outline[e], point - outline[e]) < 0f)
                {
                    return false;
                }
            }

            return true;
        }

        private static float Area(IReadOnlyList<Vector2> outline)
        {
            float area = 0f;

            for (int i = 0; i < outline.Count; i++)
            {
                area += Cross(outline[i], outline[(i + 1) % outline.Count]);
            }

            return area * 0.5f;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static Vector2 Flat(Vector3 point)
        {
            return new Vector2(point.x, point.z);
        }
    }
}
