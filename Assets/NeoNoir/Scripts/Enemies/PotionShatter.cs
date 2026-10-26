using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A potion bottle shattering: thin shards of glass flung out, droplets
    /// of what was in it splashing up with them, and a quick flash of glass
    /// where it broke.
    /// </summary>
    /// <remarks>
    /// Purely visual; the damage is the potion's and the pool's.
    ///
    /// Like <see cref="HammerImpact"/>, nothing fades: the pieces shrink away
    /// as they fall, so every pixel stays full colour and the spot colour
    /// threshold never crushes a half-faded edge. Pieces come to rest on the
    /// floor height they were given rather than sinking through it.
    ///
    /// Spawned at runtime and builds its pieces from cubes, so it needs no
    /// prefab.
    /// </remarks>
    public sealed class PotionShatter : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Glass")]
            [Tooltip("Shards of glass flung out.")]
            [Min(0)] public int shardCount = 10;

            [Tooltip("Length of each shard. They are thin slivers of this.")]
            [Min(0.01f)] public float shardSize = 0.3f;

            [Tooltip("Colour of the glass. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color glassColour = new Color(1.6f, 1.6f, 1.8f, 1f);

            [Tooltip("Material for the glass and the flash. Empty uses NeoNoir/Flat in the colour above.")]
            public Material glassMaterial;

            [Header("Droplets")]
            [Tooltip("Droplets of potion splashed up. They take the pool's colour.")]
            [Min(0)] public int dropletCount = 8;

            [Tooltip("Size of each droplet.")]
            [Min(0.01f)] public float dropletSize = 0.18f;

            [Header("Motion")]
            [Tooltip("Slowest and fastest upward launch speed.")]
            public Vector2 upSpeed = new Vector2(3f, 7f);

            [Tooltip("Slowest and fastest outward launch speed.")]
            public Vector2 outSpeed = new Vector2(2f, 6f);

            [Tooltip("Downward pull on the pieces, in units per second squared.")]
            [Min(0f)] public float gravity = 25f;

            [Tooltip("Seconds each piece lives.")]
            [Min(0.01f)] public float lifetime = 0.6f;

            [Header("Flash")]
            [Tooltip("Size the flash bursts out to where the bottle broke. 0 for none.")]
            [Min(0f)] public float flashSize = 1.2f;

            [Tooltip("Seconds the flash takes to burst out and shrink away.")]
            [Min(0.01f)] public float flashTime = 0.12f;
        }

        private struct Piece
        {
            public Transform transform;
            public Vector3 velocity;
            public Vector3 spinAxis;
            public float spinSpeed;
            public Vector3 size;
        }

        private readonly List<Piece> pieces = new List<Piece>();

        private Settings settings;
        private float floorHeight;
        private float age;
        private Transform flash;
        private MaterialPropertyBlock block;

        /// <summary>
        /// Shatters at <paramref name="point"/>. Pieces settle at
        /// <paramref name="floor"/> height. Droplets are
        /// <paramref name="liquidColour"/>.
        /// </summary>
        public static PotionShatter Create(Vector3 point, float floor, Settings settings, Color liquidColour)
        {
            GameObject host = new GameObject("PotionShatter");
            host.transform.position = point;

            PotionShatter shatter = host.AddComponent<PotionShatter>();
            shatter.Begin(floor, settings, liquidColour);
            return shatter;
        }

        private void Begin(float floor, Settings shatterSettings, Color liquidColour)
        {
            settings = shatterSettings;
            floorHeight = floor;
            block = new MaterialPropertyBlock();

            Material glass = settings.glassMaterial != null ? settings.glassMaterial : FlatMaterial.Shared;
            Color glassColour = settings.glassMaterial != null && settings.glassMaterial.HasProperty(FlatMaterial.BaseColorId)
                ? settings.glassMaterial.GetColor(FlatMaterial.BaseColorId)
                : settings.glassColour;

            for (int i = 0; i < settings.shardCount; i++)
            {
                float length = settings.shardSize * UnityEngine.Random.Range(0.6f, 1.2f);
                Spawn("Shard", new Vector3(length * 0.35f, length * 0.08f, length), glass, glassColour);
            }

            for (int i = 0; i < settings.dropletCount; i++)
            {
                float size = settings.dropletSize * UnityEngine.Random.Range(0.7f, 1.3f);
                Spawn("Droplet", Vector3.one * size, FlatMaterial.Shared, liquidColour);
            }

            if (settings.flashSize > 0f)
            {
                flash = MakeCube("Flash", glass, glassColour).transform;
                flash.localScale = Vector3.zero;
            }
        }

        private GameObject MakeCube(string pieceName, Material material, Color colour)
        {
            GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(piece.GetComponent<Collider>());

            piece.name = pieceName;
            piece.transform.SetParent(transform, false);

            MeshRenderer pieceRenderer = piece.GetComponent<MeshRenderer>();
            pieceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (material != null)
            {
                pieceRenderer.sharedMaterial = material;
                FlatMaterial.Tint(pieceRenderer, colour, block);
            }

            return piece;
        }

        private void Spawn(string pieceName, Vector3 size, Material material, Color colour)
        {
            GameObject piece = MakeCube(pieceName, material, colour);
            piece.transform.localRotation = UnityEngine.Random.rotationUniform;
            piece.transform.localScale = size;

            Vector2 outward = UnityEngine.Random.insideUnitCircle.normalized;
            float rise = UnityEngine.Random.Range(settings.upSpeed.x, settings.upSpeed.y);
            float spread = UnityEngine.Random.Range(settings.outSpeed.x, settings.outSpeed.y);

            pieces.Add(new Piece
            {
                transform = piece.transform,
                velocity = new Vector3(outward.x * spread, rise, outward.y * spread),
                spinAxis = UnityEngine.Random.onUnitSphere,
                spinSpeed = UnityEngine.Random.Range(540f, 1440f),
                size = size,
            });
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;

            if (age >= Mathf.Max(settings.lifetime, settings.flashTime))
            {
                Destroy(gameObject);
                return;
            }

            if (flash != null)
            {
                // Bursts out over the first third, then shrinks away.
                float t = Mathf.Clamp01(age / settings.flashTime);
                float grow = t < 0.33f ? t / 0.33f : 1f - (t - 0.33f) / 0.67f;
                flash.localScale = Vector3.one * (settings.flashSize * Mathf.Max(0f, grow));
                flash.Rotate(Vector3.up, 720f * deltaTime, Space.World);
            }

            float life = Mathf.Clamp01(age / settings.lifetime);

            // Holds full size for the first half, then shrinks away.
            float scale = Mathf.Clamp01((1f - life) * 2f);

            for (int i = 0; i < pieces.Count; i++)
            {
                Piece piece = pieces[i];
                piece.velocity.y -= settings.gravity * deltaTime;

                Vector3 position = piece.transform.position + piece.velocity * deltaTime;
                Vector3 size = piece.size * scale;

                float floor = floorHeight + size.y * 0.5f;
                if (position.y < floor)
                {
                    position.y = floor;
                    piece.velocity = Vector3.zero;
                    piece.spinSpeed = 0f;
                }

                piece.transform.position = position;
                piece.transform.Rotate(piece.spinAxis, piece.spinSpeed * deltaTime, Space.World);
                piece.transform.localScale = size;
                pieces[i] = piece;
            }
        }
    }
}
