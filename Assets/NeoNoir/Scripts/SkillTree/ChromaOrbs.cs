using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace LightAndShadow
{
    /// <summary>
    /// The player's <see cref="ChromaPoints"/>, shown as glowing orbs circling
    /// them: one orb per point. Afterworld only.
    /// </summary>
    /// <remarks>
    /// Keeps one orb per point, adding and removing them as the count
    /// changes, the ring easing round to keep them evenly spaced. Spending a
    /// point should go through <see cref="Absorb"/>, which flies orbs into
    /// whatever bought them instead of letting them vanish.
    ///
    /// Made on first use by <see cref="PurchasableBlade"/>, so it exists only
    /// in a scene with blades to buy, and never in a level. To restyle it,
    /// put this component on an object in the Afterworld and change the
    /// fields -- that one is used instead.
    /// </remarks>
    public sealed class ChromaOrbs : MonoBehaviour
    {
        [Header("Orbit")]
        [SerializeField]
        [Tooltip("Height of the ring relative to the player's position (the middle of their body). 0 is level with it.")]
        private float height;

        [SerializeField] [Min(0f)] private float radius = 1.1f;

        [SerializeField]
        [Tooltip("Degrees a second the ring turns.")]
        private float spinSpeed = 70f;

        [SerializeField]
        [Tooltip("How far each orb bobs up and down.")]
        [Min(0f)]
        private float bob = 0.12f;

        [SerializeField] [Min(0f)] private float bobSpeed = 2.2f;

        [SerializeField]
        [Tooltip("How quickly the ring catches up with the player. Higher follows more tightly.")]
        [Min(0.1f)]
        private float follow = 10f;

        [SerializeField]
        [Tooltip("How quickly orbs slide round to even spacing when one is added or taken.")]
        [Min(0.1f)]
        private float respace = 4f;

        [Header("Look")]
        [SerializeField] [Min(0.01f)] private float orbSize = 0.16f;

        [SerializeField]
        [Tooltip("HDR: above 1 blooms.")]
        [ColorUsage(false, true)]
        private Color colour = new Color(4f, 4f, 4f, 1f);

        [SerializeField]
        [Tooltip("Seconds of trail each orb leaves behind. 0 for none.")]
        [Min(0f)]
        private float trailTime = 0.18f;

        [SerializeField]
        [Tooltip("Seconds a new orb takes to swell in.")]
        [Min(0.01f)]
        private float appearTime = 0.35f;

        [Header("Absorbing")]
        [SerializeField]
        [Tooltip("Seconds an orb takes to fly into the blade.")]
        [Min(0.05f)]
        private float absorbTime = 0.8f;

        [SerializeField]
        [Tooltip("Seconds between orbs leaving, when more than one is spent.")]
        [Min(0f)]
        private float absorbStagger = 0.15f;

        [SerializeField]
        [Tooltip("How high above the straight path the orb arcs.")]
        private float absorbArc = 1.2f;

        [SerializeField]
        [Tooltip("The flash where each orb lands.")]
        private EnergyBurst.Settings absorbBurst = new EnergyBurst.Settings
        {
            flashSize = 0.9f,
            flashTime = 0.25f,
            sparkCount = 14,
            sparkSize = 0.3f,
            sparkSpeed = new Vector2(3f, 7f),
            sparkLife = 0.4f,
            colour = new Color(4f, 4f, 4f, 1f),
        };

        private sealed class Orb
        {
            public Transform transform;
            public float slot;
            public float age;
            public float phase;
        }

        private static ChromaOrbs instance;

        private readonly List<Orb> ring = new List<Orb>();
        private MaterialPropertyBlock block;
        private Transform player;
        private Vector3 centre;
        private bool placed;
        private float spin;

        /// <summary>The orbs in the scene, made on first use if there are none.</summary>
        public static ChromaOrbs Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<ChromaOrbs>(FindObjectsInactive.Include);
                }

                if (instance == null)
                {
                    instance = new GameObject("ChromaOrbs").AddComponent<ChromaOrbs>();
                }

                return instance;
            }
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            block = new MaterialPropertyBlock();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        /// <summary>
        /// Flies <paramref name="count"/> orbs off the ring into
        /// <paramref name="target"/>, each bursting as it lands.
        /// <paramref name="arrived"/> is called once the last one has. Call
        /// it the moment the points are spent, so the ring does not drop the
        /// orbs on its own first.
        /// </summary>
        public void Absorb(int count, Vector3 target, Action arrived)
        {
            List<Orb> leaving = new List<Orb>();

            for (int i = 0; i < count && ring.Count > 0; i++)
            {
                Orb orb = ring[ring.Count - 1];
                ring.RemoveAt(ring.Count - 1);
                leaving.Add(orb);
            }

            if (leaving.Count == 0)
            {
                arrived?.Invoke();
                return;
            }

            StartCoroutine(AbsorbAll(leaving, target, arrived));
        }

        private IEnumerator AbsorbAll(List<Orb> leaving, Vector3 target, Action arrived)
        {
            int landed = 0;

            for (int i = 0; i < leaving.Count; i++)
            {
                StartCoroutine(Fly(leaving[i], target, () => landed++));

                if (absorbStagger > 0f && i < leaving.Count - 1)
                {
                    yield return new WaitForSeconds(absorbStagger);
                }
            }

            while (landed < leaving.Count)
            {
                yield return null;
            }

            arrived?.Invoke();
        }

        private IEnumerator Fly(Orb orb, Vector3 target, Action landed)
        {
            Vector3 start = orb.transform.position;
            Vector3 control = (start + target) * 0.5f + Vector3.up * absorbArc;
            float startScale = orb.transform.localScale.x;
            float clock = 0f;

            while (clock < absorbTime)
            {
                clock += Time.deltaTime;
                float t = Mathf.Clamp01(clock / absorbTime);

                // Eases out of the ring, then speeds into the blade.
                float eased = t * t * (3f - 2f * t);
                eased = Mathf.Lerp(eased, t * t, t);

                float u = 1f - eased;
                orb.transform.position = u * u * start + 2f * u * eased * control + eased * eased * target;

                // Swells a little as it leaves, then shrinks into the blade.
                float scale = t < 0.2f ? Mathf.Lerp(1f, 1.4f, t / 0.2f) : Mathf.Lerp(1.4f, 0.3f, (t - 0.2f) / 0.8f);
                orb.transform.localScale = Vector3.one * (startScale * scale);
                yield return null;
            }

            EnergyBurst.Create(target, absorbBurst);
            Destroy(orb.transform.gameObject);
            landed();
        }

        private void LateUpdate()
        {
            // Testing: K gives a point.
            if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
            {
                ChromaPoints.Add(1);
            }

            if (!FindPlayer())
            {
                return;
            }

            Vector3 wanted = player.position + Vector3.up * height;

            if (!placed)
            {
                centre = wanted;
                placed = true;
            }
            else
            {
                centre = Vector3.Lerp(centre, wanted, 1f - Mathf.Exp(-follow * Time.deltaTime));
            }

            Match(ChromaPoints.Count);

            spin = Mathf.Repeat(spin + spinSpeed * Time.deltaTime, 360f);
            float ease = 1f - Mathf.Exp(-respace * Time.deltaTime);
            float step = ring.Count > 0 ? 360f / ring.Count : 0f;

            for (int i = 0; i < ring.Count; i++)
            {
                Orb orb = ring[i];
                orb.slot = Mathf.LerpAngle(orb.slot, i * step, ease);
                orb.age += Time.deltaTime;

                float angle = (spin + orb.slot) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                offset.y = Mathf.Sin(Time.time * bobSpeed + orb.phase) * bob;
                orb.transform.position = centre + offset;

                float grown = Mathf.Clamp01(orb.age / appearTime);
                float pop = 1f + Mathf.Sin(grown * Mathf.PI) * 0.35f;
                orb.transform.localScale = Vector3.one * (orbSize * grown * pop);
            }
        }

        /// <summary>Adds or removes orbs until there is one per point.</summary>
        private void Match(int count)
        {
            while (ring.Count > count)
            {
                Orb orb = ring[ring.Count - 1];
                ring.RemoveAt(ring.Count - 1);
                Destroy(orb.transform.gameObject);
            }

            while (ring.Count < count)
            {
                // A new orb starts at the slot just after the last one, so
                // the ring opens up round it rather than jumping.
                float slot = ring.Count > 0 ? ring[ring.Count - 1].slot + 360f / (ring.Count + 1) : 0f;
                ring.Add(new Orb { transform = MakeOrb(), slot = slot, phase = UnityEngine.Random.value * Mathf.PI * 2f });
            }
        }

        private Transform MakeOrb()
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(orb.GetComponent<Collider>());
            orb.name = "Chroma Orb";
            orb.transform.SetParent(transform, false);
            orb.transform.position = centre;
            orb.transform.localScale = Vector3.zero;

            MeshRenderer renderer = orb.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.Shared;
            FlatMaterial.Tint(renderer, colour, block);

            if (trailTime > 0f)
            {
                TrailRenderer trail = orb.AddComponent<TrailRenderer>();
                trail.time = trailTime;
                trail.minVertexDistance = 0.03f;
                trail.widthMultiplier = orbSize * 0.7f;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.sharedMaterial = FlatMaterial.DoubleSided;
                FlatMaterial.Tint(trail, colour, block);
            }

            return orb.transform;
        }

        private bool FindPlayer()
        {
            if (player == null)
            {
                PlayerMotor motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);
                player = motor != null ? motor.transform : null;
            }

            return player != null;
        }
    }
}
