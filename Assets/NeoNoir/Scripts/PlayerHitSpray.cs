using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Sprays white flecks off the player every time they take damage, like
    /// blood.
    /// </summary>
    /// <remarks>
    /// Hears every hit through <see cref="PlayerHealth.AnyDamaged"/>, so the
    /// player needs nothing added. When the damage says where it came from,
    /// the flecks fly away from that point in a cone; otherwise they burst
    /// out all round.
    ///
    /// Flecks are small cubes thrown out of the middle of the player's body.
    /// They fall, bounce to rest on the floor the player stands on and shrink
    /// away. Nothing fades, so the threshold keeps them pure white. Each
    /// burst is one object that removes itself when its flecks are gone.
    ///
    /// Installs itself in every scene that does not already have one, so it
    /// works with no setup. Place one by hand to change its settings.
    /// </remarks>
    public sealed class PlayerHitSpray : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Flecks per hit.")]
        [Min(1)]
        private int count = 12;

        [SerializeField]
        [Tooltip("Size of each fleck as it is thrown.")]
        [Min(0.01f)]
        private float size = 0.12f;

        [SerializeField]
        [Tooltip("Slowest and fastest launch speed.")]
        private Vector2 speed = new Vector2(3f, 7f);

        [SerializeField]
        [Tooltip("Extra upward launch speed, so the spray arcs up before falling.")]
        [Min(0f)]
        private float lift = 2.5f;

        [SerializeField]
        [Tooltip("Half-angle of the cone the flecks fly out in, away from the hit.")]
        [Range(0f, 180f)]
        private float coneAngle = 50f;

        [SerializeField]
        [Tooltip("Downward pull on the flecks, in units per second squared.")]
        [Min(0f)]
        private float gravity = 20f;

        [SerializeField]
        [Tooltip("Seconds each fleck lives.")]
        [Min(0.05f)]
        private float lifetime = 0.6f;

        [SerializeField]
        [Tooltip("Material for the flecks. Empty uses NeoNoir/Flat, coloured below.")]
        private Material material;

        [SerializeField]
        [Tooltip("Colour of the flecks. Above 1 blooms.")]
        [ColorUsage(false, true)]
        private Color colour = new Color(2f, 2f, 2f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => EnsureOne();
            EnsureOne();
        }

        private static void EnsureOne()
        {
            if (FindFirstObjectByType<PlayerHitSpray>(FindObjectsInactive.Include) == null)
            {
                new GameObject("PlayerHitSpray").AddComponent<PlayerHitSpray>();
            }
        }

        private void OnEnable()
        {
            PlayerHealth.AnyDamaged += OnDamaged;
        }

        private void OnDisable()
        {
            PlayerHealth.AnyDamaged -= OnDamaged;
        }

        private void OnDamaged(PlayerHealth health, float amount, Vector3? source)
        {
            Collider body = health.GetComponent<Collider>();
            Vector3 origin = body != null ? body.bounds.center : health.transform.position + Vector3.up;
            float floor = body != null ? body.bounds.min.y : health.transform.position.y;

            Vector3 away = Vector3.zero;

            if (source.HasValue)
            {
                away = origin - source.Value;
                away.y = 0f;
            }

            GameObject host = new GameObject("HitSpray");
            host.transform.position = origin;
            host.AddComponent<Burst>().Play(this, away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.zero, floor);
        }

        /// <summary>One hit's worth of flecks.</summary>
        private sealed class Burst : MonoBehaviour
        {
            private struct Fleck
            {
                public Transform transform;
                public Vector3 velocity;
                public Vector3 spinAxis;
                public float spinSpeed;
            }

            private readonly List<Fleck> flecks = new List<Fleck>();
            private PlayerHitSpray settings;
            private float floor;
            private float age;

            public void Play(PlayerHitSpray spray, Vector3 away, float floorHeight)
            {
                settings = spray;
                floor = floorHeight;

                Material fleckMaterial = settings.material != null ? settings.material : FlatMaterial.Shared;
                MaterialPropertyBlock block = new MaterialPropertyBlock();

                for (int i = 0; i < settings.count; i++)
                {
                    GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(piece.GetComponent<Collider>());

                    piece.transform.SetParent(transform, false);
                    piece.transform.localRotation = Random.rotationUniform;
                    piece.transform.localScale = Vector3.one * settings.size;

                    MeshRenderer pieceRenderer = piece.GetComponent<MeshRenderer>();
                    pieceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

                    if (fleckMaterial != null)
                    {
                        pieceRenderer.sharedMaterial = fleckMaterial;

                        if (settings.material == null)
                        {
                            FlatMaterial.Tint(pieceRenderer, settings.colour, block);
                        }
                    }

                    flecks.Add(new Fleck
                    {
                        transform = piece.transform,
                        velocity = LaunchDirection(away) * Random.Range(settings.speed.x, settings.speed.y)
                                   + Vector3.up * settings.lift,
                        spinAxis = Random.onUnitSphere,
                        spinSpeed = Random.Range(360f, 1080f),
                    });
                }
            }

            /// <summary>Somewhere in the cone round <paramref name="away"/>, or anywhere round the body if there is no away.</summary>
            private Vector3 LaunchDirection(Vector3 away)
            {
                if (away == Vector3.zero)
                {
                    Vector2 flat = Random.insideUnitCircle.normalized;
                    return new Vector3(flat.x, Random.Range(0f, 0.6f), flat.y).normalized;
                }

                float yaw = Random.Range(-settings.coneAngle, settings.coneAngle);
                float pitch = Random.Range(0f, settings.coneAngle * 0.6f);
                Vector3 side = Vector3.Cross(Vector3.up, away);

                return Quaternion.AngleAxis(-pitch, side) * (Quaternion.AngleAxis(yaw, Vector3.up) * away);
            }

            private void Update()
            {
                float deltaTime = Time.deltaTime;
                age += deltaTime;

                if (age >= settings.lifetime)
                {
                    Destroy(gameObject);
                    return;
                }

                // Holds full size for the first half, then shrinks away.
                float scale = settings.size * Mathf.Clamp01((1f - age / settings.lifetime) * 2f);

                for (int i = 0; i < flecks.Count; i++)
                {
                    Fleck fleck = flecks[i];
                    fleck.velocity.y -= settings.gravity * deltaTime;

                    Vector3 position = fleck.transform.position + fleck.velocity * deltaTime;
                    float rest = floor + scale * 0.5f;

                    if (position.y < rest)
                    {
                        position.y = rest;
                        fleck.velocity = Vector3.zero;
                    }

                    fleck.transform.position = position;
                    fleck.transform.Rotate(fleck.spinAxis, fleck.spinSpeed * deltaTime, Space.World);
                    fleck.transform.localScale = Vector3.one * scale;
                    flecks[i] = fleck;
                }
            }
        }
    }
}
