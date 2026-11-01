using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Burn marks left where a laser touches the ground, each dissolving
    /// away a little while after it was made.
    /// </summary>
    /// <remarks>
    /// Whatever drives the laser calls <see cref="Mark"/> every frame with
    /// where it hits. A mark is laid every <see cref="Settings.spacing"/> the
    /// hit point travels, filling in the gap between frames, so a fast sweep
    /// still leaves an unbroken line -- up to <see cref="Settings.maxPerFrame"/>
    /// at a time, beyond which the line goes patchy rather than costly.
    ///
    /// Each mark is a flat disc lying on whatever it was laid on, floors and
    /// walls alike. It holds for <see cref="Settings.lifetime"/> seconds,
    /// dissolving through the NeoNoir/Flat dither over the last
    /// <see cref="Settings.fadeTime"/>, shrinking a little as it goes.
    ///
    /// Not parented to the laser, so marks stay put and finish fading after
    /// it is gone. Once <see cref="Stop"/> is called and the last mark has
    /// faded, it destroys itself. Harmless: the marks are only for show.
    ///
    /// Spawned at runtime and builds its own mesh, so it needs no prefab.
    /// </remarks>
    public sealed class LaserScorch : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Width of each mark.")]
            [Min(0.05f)] public float size = 0.9f;

            [Tooltip("Distance the hit point travels between one mark and the next.")]
            [Min(0.05f)] public float spacing = 0.3f;

            [Tooltip("Most marks laid in one frame. A sweep faster than this leaves gaps.")]
            [Range(1, 64)] public int maxPerFrame = 12;

            [Tooltip("Seconds a mark lasts, fade included.")]
            [Min(0.05f)] public float lifetime = 2.5f;

            [Tooltip("Seconds at the end of its life a mark spends dissolving.")]
            [Min(0.01f)] public float fadeTime = 1.2f;

            [Tooltip("Size a mark shrinks to as it finishes fading, as a fraction of its full size.")]
            [Range(0f, 1f)] public float endScale = 0.5f;

            [Tooltip("How far off the surface a mark sits, so it does not flicker into it.")]
            [Range(0f, 0.3f)] public float surfaceOffset = 0.1f;

            [Tooltip("Colour of the marks. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color colour = new Color(1.5f, 0.08f, 0.05f, 1f);

            [Tooltip("Width of the outline round the trail. 0 draws none.")]
            [Min(0f)] public float outlineWidth = 0.1f;

            [Tooltip("Colour of the outline. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color outlineColour = new Color(2f, 2f, 2f, 1f);
        }

        /// <summary>
        /// How far below the marks their outlines sit. Every outline under
        /// every mark, so where marks overlap the trail stays red inside and
        /// only its outer edge shows white.
        /// </summary>
        private const float OutlineDrop = 0.03f;

        private sealed class Burn
        {
            public Transform transform;
            public MeshRenderer renderer;
            public MeshRenderer outline;
            public float age;
        }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static Mesh discMesh;

        private readonly List<Burn> burns = new List<Burn>();

        private Settings settings;
        private MaterialPropertyBlock block;
        private Vector3 last;
        private bool hasLast;
        private bool stopped;

        /// <summary>Starts a new line of marks, laid as <see cref="Mark"/> is called.</summary>
        public static LaserScorch Create(Settings settings)
        {
            GameObject host = new GameObject("LaserScorch");
            LaserScorch scorch = host.AddComponent<LaserScorch>();
            scorch.settings = settings;
            scorch.block = new MaterialPropertyBlock();
            return scorch;
        }

        /// <summary>
        /// The laser touches <paramref name="point"/>, on a surface facing
        /// <paramref name="normal"/>, this frame. Lays marks from where it
        /// last touched.
        /// </summary>
        public void Mark(Vector3 point, Vector3 normal)
        {
            if (stopped)
            {
                return;
            }

            if (!hasLast)
            {
                hasLast = true;
                last = point;
                Lay(point, normal);
                return;
            }

            float distance = Vector3.Distance(last, point);

            if (distance < settings.spacing)
            {
                return;
            }

            // Evenly from the last mark up to here; the rest of the
            // distance carries over to the next frame.
            int count = Mathf.FloorToInt(distance / settings.spacing);
            Vector3 step = (point - last) / distance * settings.spacing;
            int laid = Mathf.Min(count, settings.maxPerFrame);

            // Too many to lay: spread the few allowed across the whole gap.
            if (laid < count)
            {
                step *= (float)count / laid;
            }

            for (int i = 0; i < laid; i++)
            {
                last += step;
                Lay(last, normal);
            }
        }

        /// <summary>
        /// The laser has left the surface, or the ground is lost: the next
        /// mark starts a fresh line rather than joining up to the last.
        /// </summary>
        public void Break()
        {
            hasLast = false;
        }

        /// <summary>No more marks. Those laid finish fading, then it is gone.</summary>
        public void Stop()
        {
            stopped = true;
        }

        private void Lay(Vector3 point, Vector3 normal)
        {
            GameObject piece = new GameObject("Burn");
            piece.transform.SetParent(transform, false);
            piece.transform.SetPositionAndRotation(point + normal * settings.surfaceOffset,
                                                   Quaternion.FromToRotation(Vector3.up, normal)
                                                   * Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
            piece.transform.localScale = Vector3.one * settings.size;

            piece.AddComponent<MeshFilter>().sharedMesh = DiscMesh();

            MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.Shared;

            block.Clear();
            block.SetColor(FlatMaterial.BaseColorId, settings.colour);
            block.SetFloat(FadeId, 1f);
            renderer.SetPropertyBlock(block);

            burns.Add(new Burn { transform = piece.transform, renderer = renderer, outline = LayOutline(piece.transform) });
        }

        /// <summary>
        /// A white disc a little wider than <paramref name="burn"/>, just
        /// under it, scaling and fading with it. Null if outlines are off.
        /// </summary>
        private MeshRenderer LayOutline(Transform burn)
        {
            if (settings.outlineWidth <= 0f)
            {
                return null;
            }

            GameObject piece = new GameObject("Outline");
            piece.transform.SetParent(burn, false);

            // In the burn's own units, which are its size across.
            float drop = Mathf.Min(OutlineDrop, settings.surfaceOffset * 0.5f);
            piece.transform.localPosition = Vector3.down * (drop / settings.size);
            piece.transform.localScale = Vector3.one * (1f + settings.outlineWidth * 2f / settings.size);

            piece.AddComponent<MeshFilter>().sharedMesh = DiscMesh();

            MeshRenderer renderer = piece.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.Shared;

            block.Clear();
            block.SetColor(FlatMaterial.BaseColorId, settings.outlineColour);
            block.SetFloat(FadeId, 1f);
            renderer.SetPropertyBlock(block);

            return renderer;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            float fadeStart = Mathf.Max(0f, settings.lifetime - settings.fadeTime);

            for (int i = burns.Count - 1; i >= 0; i--)
            {
                Burn burn = burns[i];
                burn.age += deltaTime;

                if (burn.age >= settings.lifetime)
                {
                    Destroy(burn.transform.gameObject);
                    burns.RemoveAt(i);
                    continue;
                }

                if (burn.age < fadeStart)
                {
                    continue;
                }

                float left = 1f - Mathf.Clamp01((burn.age - fadeStart) / settings.fadeTime);
                burn.transform.localScale = Vector3.one * (settings.size * Mathf.Lerp(settings.endScale, 1f, left));

                block.Clear();
                block.SetColor(FlatMaterial.BaseColorId, settings.colour);
                block.SetFloat(FadeId, left);
                burn.renderer.SetPropertyBlock(block);

                if (burn.outline != null)
                {
                    block.Clear();
                    block.SetColor(FlatMaterial.BaseColorId, settings.outlineColour);
                    block.SetFloat(FadeId, left);
                    burn.outline.SetPropertyBlock(block);
                }
            }

            if (stopped && burns.Count == 0)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>A flat disc one unit across, facing up, built once and shared.</summary>
        private static Mesh DiscMesh()
        {
            if (discMesh != null)
            {
                return discMesh;
            }

            const int sides = 12;
            Vector3[] vertices = new Vector3[sides + 1];
            int[] triangles = new int[sides * 3];

            vertices[0] = Vector3.zero;

            for (int i = 0; i < sides; i++)
            {
                float angle = Mathf.PI * 2f * i / sides;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.5f;

                // Wound clockwise seen from above, so it faces up.
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % sides + 1;
                triangles[i * 3 + 2] = i + 1;
            }

            discMesh = new Mesh { name = "Laser scorch" };
            discMesh.vertices = vertices;
            discMesh.triangles = triangles;
            discMesh.RecalculateNormals();
            discMesh.RecalculateBounds();
            return discMesh;
        }
    }
}
