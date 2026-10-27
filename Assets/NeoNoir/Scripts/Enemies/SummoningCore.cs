using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The centre object a <see cref="SummoningAltar"/> feeds, which comes
    /// alive as altars are lit: its rings spin up one by one, it spews runes
    /// that drift off and dissolve, and it gives off an aura.
    /// </summary>
    /// <remarks>
    /// Put it on the centre object and set it as the summoning altar's
    /// centre; the arcs from the altars land on this object's pivot. Each
    /// time an arc arrives the summoning altar raises the power, which is the
    /// fraction of altars lit:
    ///
    ///   Rings   One more ring starts spinning per altar, in list order. Each
    ///           tumbles about a line across its own face -- spinning about
    ///           its own axis would look like standing still -- while that
    ///           line slowly swings round the vertical, like a gyroscope. All
    ///           of it is about this object's pivot, so a ring set off-centre
    ///           orbits it. The ring's own axis is taken as the thinnest side
    ///           of its mesh, so any torus works whichever way it was modelled.
    ///   Runes   Spewed from within at a rate that grows with the power,
    ///           written as they leave, then slowing and dissolving away.
    ///   Aura    A halo round the object that breathes, and rings of light
    ///           rippling out from it, more often and brighter with power.
    ///   Body    The object itself shifts from its own colour to
    ///           <see cref="poweredColour"/>, reaching it fully once every
    ///           altar is lit.
    ///
    /// <see cref="Surge"/> drives the runes, ripples and rings harder, for
    /// the summoning. <see cref="Consume"/> dissolves the object, its rings
    /// and everything it gives off, for good.
    ///
    /// Before the first altar it is still and dark. The runes and aura face
    /// the camera. Everything fades by dithered dissolve, which this game's
    /// two-tone look needs in place of transparency.
    /// </remarks>
    public sealed class SummoningCore : MonoBehaviour
    {
        [Header("Look")]
        [SerializeField]
        [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
        [ColorUsage(false, true)]
        private Color colour = new Color(4f, 0.35f, 0.3f);

        [SerializeField]
        [Tooltip("Material for the runes and aura. Empty makes a NeoNoir/Flat one. Needs a _Fade property to dissolve.")]
        private Material material;

        [Header("Body")]
        [SerializeField]
        [Tooltip("What the centre object itself turns as it wakes, reached fully once every altar is lit. HDR: above 1 blooms.")]
        [ColorUsage(false, true)]
        private Color poweredColour = new Color(4f, 0.35f, 0.3f);

        [SerializeField]
        [Tooltip("Renderers that change colour as it wakes. Empty uses every renderer on this object except the rings.")]
        private Renderer[] bodyParts = new Renderer[0];

        [Header("Rings")]
        [SerializeField]
        [Tooltip("Rings that spin about this object, one more starting per altar lit, in this order.")]
        private List<Transform> rings = new List<Transform>();

        [SerializeField]
        [Tooltip("Degrees per second each ring tumbles at full speed. Rings alternate direction and vary a little.")]
        private float ringSpinSpeed = 120f;

        [SerializeField]
        [Tooltip("Degrees per second each ring's tumble swings round the vertical.")]
        private float ringPrecession = 25f;

        [SerializeField]
        [Tooltip("Seconds a ring takes to reach full speed once started.")]
        [Min(0.01f)]
        private float ringSpinUpTime = 1.5f;

        [Header("Spewed runes")]
        [SerializeField]
        [Tooltip("Runes per second with every altar lit. Fewer altars spew proportionally fewer.")]
        [Min(0f)]
        private float spewRate = 10f;

        [SerializeField]
        [Tooltip("Slowest and fastest a rune leaves, in metres per second.")]
        private Vector2 spewSpeed = new Vector2(2f, 4.5f);

        [SerializeField]
        [Tooltip("How quickly the runes slow once out. Higher stops them sooner.")]
        [Min(0f)]
        private float spewDrag = 1.5f;

        [SerializeField]
        [Tooltip("Shortest and longest seconds a rune lasts.")]
        private Vector2 spewLife = new Vector2(1.2f, 2f);

        [SerializeField]
        [Tooltip("When a rune starts to dissolve, as a fraction of its life.")]
        [Range(0f, 1f)]
        private float spewFadeFrom = 0.4f;

        [SerializeField]
        [Tooltip("How far from the pivot the runes appear, in metres.")]
        [Min(0f)]
        private float spewFrom = 0.3f;

        [SerializeField]
        [Tooltip("Height of each spewed rune, in metres.")]
        [Min(0.05f)]
        private float spewRuneSize = 0.5f;

        [SerializeField]
        [Tooltip("Seconds each rune takes to be written as it leaves.")]
        [Min(0.01f)]
        private float spewWriteTime = 0.15f;

        [Header("Aura")]
        [SerializeField]
        [Tooltip("Radius of the halo round the object, in metres.")]
        [Min(0.1f)]
        private float haloRadius = 1.5f;

        [SerializeField]
        [Tooltip("Ripples per second with every altar lit.")]
        [Min(0f)]
        private float rippleRate = 1.5f;

        [SerializeField]
        [Tooltip("Radius a ripple has spread to as it vanishes, in metres.")]
        [Min(0.1f)]
        private float rippleReach = 4f;

        [SerializeField]
        [Tooltip("Seconds a ripple lasts.")]
        [Min(0.05f)]
        private float rippleLife = 1.2f;

        [SerializeField]
        [Tooltip("Width of the aura's lines, in metres. Ripples thicken as they spread.")]
        [Min(0.01f)]
        private float auraWidth = 0.05f;

        [SerializeField]
        [Tooltip("Width of the spewed runes' lines, in metres.")]
        [Min(0.01f)]
        private float runeWidth = 0.05f;

        private sealed class Ring
        {
            public Transform transform;
            public Vector3 axis;
            public float speed;
            public float fullSpeed;
            public bool spinning;
        }

        private sealed class Mote
        {
            public Transform transform;
            public RuneDrawing drawing;
            public Vector3 velocity;
            public float age;
            public float life;
            public float roll;
            public float scale;
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private readonly List<Ring> spinners = new List<Ring>();
        private readonly List<Renderer> body = new List<Renderer>();
        private readonly List<Color> bodyColours = new List<Color>();
        private MaterialPropertyBlock bodyBlock;
        private readonly List<Mote> runes = new List<Mote>();
        private readonly List<Mote> ripples = new List<Mote>();

        // The halo, runes and ripples live under their own root, apart from
        // this object, so they take none of its scale or rotation.
        private Transform effects;
        private Transform halo;
        private RuneDrawing haloDrawing;

        private float power;
        private float shownPower;
        private float spewOwed;
        private float rippleOwed;
        private int runeIndex;

        private readonly List<Renderer> ringRenderers = new List<Renderer>();
        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        // How hard it is driven: multiplies the spew, ripples and ring speed.
        private float surge = 1f;
        private float surgeTarget = 1f;
        private float surgeRate;

        // 1 whole, falling to 0 as it is consumed.
        private float visible = 1f;
        private float consumeTime = -1f;

        /// <summary>Where the arcs land and the runes leave from.</summary>
        public Vector3 Centre => transform.position;

        /// <summary>How awake the core is, 0 to 1: the fraction of altars lit.</summary>
        public float Power => power;

        private void Awake()
        {
            for (int i = 0; i < rings.Count; i++)
            {
                if (rings[i] == null)
                {
                    continue;
                }

                // Alternate directions, and vary the speeds a little, so the
                // rings do not move in lockstep.
                float direction = i % 2 == 0 ? 1f : -1f;
                float variety = 1f + 0.2f * (i % 3);

                spinners.Add(new Ring { transform = rings[i], fullSpeed = ringSpinSpeed * direction * variety });
                ringRenderers.AddRange(rings[i].GetComponentsInChildren<Renderer>());
            }

            FindBody();

            effects = new GameObject($"{name} effects").transform;

            GameObject haloHost = new GameObject("Halo");
            haloHost.transform.SetParent(effects, false);
            halo = haloHost.transform;
            haloDrawing = new RuneDrawing(haloHost, material);
            haloDrawing.AddRune(Circle(), Vector3.zero, Vector3.forward, haloRadius * 2f, auraWidth, 0f, 1f);
            haloDrawing.Progress = 0f;
        }

        /// <summary>The body parts and the colours they start as, so they can be blended from.</summary>
        private void FindBody()
        {
            bodyBlock = new MaterialPropertyBlock();
            Renderer[] parts = bodyParts.Length > 0 ? bodyParts : GetComponentsInChildren<Renderer>();

            foreach (Renderer part in parts)
            {
                if (part == null || IsRing(part.transform))
                {
                    continue;
                }

                Material shared = part.sharedMaterial;
                Color start = Color.white;

                if (shared != null && shared.HasProperty(BaseColorId))
                {
                    start = shared.GetColor(BaseColorId);
                }
                else if (shared != null && shared.HasProperty(ColorId))
                {
                    start = shared.GetColor(ColorId);
                }

                body.Add(part);
                bodyColours.Add(start);
            }
        }

        private bool IsRing(Transform part)
        {
            foreach (Transform ring in rings)
            {
                if (ring != null && part.IsChildOf(ring))
                {
                    return true;
                }
            }

            return false;
        }

        private void ColourBody()
        {
            for (int i = 0; i < body.Count; i++)
            {
                if (body[i] == null)
                {
                    continue;
                }

                Color blended = Color.Lerp(bodyColours[i], poweredColour, shownPower);

                body[i].GetPropertyBlock(bodyBlock);
                bodyBlock.SetColor(BaseColorId, blended);
                bodyBlock.SetColor(ColorId, blended);

                if (visible < 1f)
                {
                    bodyBlock.SetFloat(FadeId, visible);
                }

                body[i].SetPropertyBlock(bodyBlock);
            }

            if (visible < 1f)
            {
                foreach (Renderer ring in ringRenderers)
                {
                    if (ring == null)
                    {
                        continue;
                    }

                    ring.GetPropertyBlock(bodyBlock);
                    bodyBlock.SetFloat(FadeId, visible);
                    ring.SetPropertyBlock(bodyBlock);
                }
            }
        }

        /// <summary>
        /// Drives the core <paramref name="multiplier"/> times harder --
        /// spewing, rippling and spinning -- easing there over
        /// <paramref name="rampTime"/> seconds. 1 is normal.
        /// </summary>
        public void Surge(float multiplier, float rampTime)
        {
            surgeTarget = Mathf.Max(0f, multiplier);
            surgeRate = Mathf.Abs(surgeTarget - surge) / Mathf.Max(0.01f, rampTime);
        }

        /// <summary>
        /// Dissolves the object, its rings and everything it gives off over
        /// <paramref name="duration"/> seconds, then hides them for good.
        /// The object itself stays in the scene, switched off in all but name.
        /// </summary>
        public void Consume(float duration)
        {
            if (consumeTime < 0f)
            {
                consumeTime = Mathf.Max(0.01f, duration);
            }
        }

        private void FinishConsuming()
        {
            foreach (Renderer part in body)
            {
                if (part != null)
                {
                    part.enabled = false;
                }
            }

            foreach (Renderer ring in ringRenderers)
            {
                if (ring != null)
                {
                    ring.enabled = false;
                }
            }

            OnDestroy();
            runes.Clear();
            ripples.Clear();
            haloDrawing = null;
            enabled = false;
        }

        /// <summary>
        /// Wakes the core to <paramref name="lit"/> of <paramref name="total"/>
        /// altars: that many rings spin, and the runes and aura grow to match.
        /// </summary>
        public void SetPower(int lit, int total)
        {
            power = total > 0 ? Mathf.Clamp01((float)lit / total) : 0f;

            for (int i = 0; i < spinners.Count; i++)
            {
                if (i < lit && !spinners[i].spinning)
                {
                    StartSpinning(spinners[i], i);
                }
            }
        }

        private void StartSpinning(Ring ring, int index)
        {
            ring.spinning = true;

            // A line across the ring's face, picked by the golden angle so
            // each ring tumbles a different way.
            Vector3 own = SymmetryAxis(ring.transform);
            Vector3 across = Vector3.Cross(own, Mathf.Abs(Vector3.Dot(own, Vector3.up)) < 0.9f ? Vector3.up : Vector3.right);
            ring.axis = Quaternion.AngleAxis(index * 137.5f, own) * across.normalized;
        }

        /// <summary>The ring's own axis, in world space: its mesh's thinnest side.</summary>
        private static Vector3 SymmetryAxis(Transform ring)
        {
            MeshFilter filter = ring.GetComponentInChildren<MeshFilter>();

            if (filter == null || filter.sharedMesh == null)
            {
                return ring.up;
            }

            Transform meshTransform = filter.transform;
            Vector3 size = Vector3.Scale(filter.sharedMesh.bounds.size, meshTransform.lossyScale);

            if (size.x <= size.y && size.x <= size.z)
            {
                return meshTransform.right;
            }

            return size.z < size.y ? meshTransform.forward : meshTransform.up;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            Vector3 centre = Centre;

            // Eases toward the power, so each altar swells it rather than
            // switching it.
            shownPower = Mathf.MoveTowards(shownPower, power, deltaTime / 1.5f);
            surge = Mathf.MoveTowards(surge, surgeTarget, surgeRate * deltaTime);

            if (consumeTime > 0f)
            {
                visible -= deltaTime / consumeTime;

                if (visible <= 0f)
                {
                    FinishConsuming();
                    return;
                }
            }

            SpinRings(centre, deltaTime);
            ColourBody();

            Quaternion facing = RuneDrawing.FacingCamera();

            UpdateHalo(facing);
            Spew(centre, deltaTime);
            Ripple(deltaTime);
            UpdateRunes(facing, deltaTime);
            UpdateRipples(facing, deltaTime);
        }

        private void SpinRings(Vector3 centre, float deltaTime)
        {
            Quaternion swing = Quaternion.AngleAxis(ringPrecession * deltaTime, Vector3.up);

            foreach (Ring ring in spinners)
            {
                if (!ring.spinning || ring.transform == null)
                {
                    continue;
                }

                ring.speed = Mathf.MoveTowards(ring.speed, ring.fullSpeed,
                                               Mathf.Abs(ring.fullSpeed) / ringSpinUpTime * deltaTime);

                ring.transform.RotateAround(centre, ring.axis, ring.speed * surge * deltaTime);
                ring.transform.RotateAround(centre, Vector3.up, ringPrecession * deltaTime);
                ring.axis = swing * ring.axis;
            }
        }

        private void UpdateHalo(Quaternion facing)
        {
            haloDrawing.Progress = shownPower > 0f ? 1f : 0f;
            halo.SetPositionAndRotation(Centre, facing);

            float breath = 0.8f + 0.2f * Mathf.Sin(Time.time * Mathf.PI * 1.2f);
            haloDrawing.SetColour(colour * (breath * Mathf.Lerp(0.5f, 1.2f, shownPower)));
            haloDrawing.SetFade(Mathf.Lerp(0.3f, 1f, shownPower) * visible);
        }

        private void Spew(Vector3 centre, float deltaTime)
        {
            // Nothing new once it is being consumed.
            spewOwed += consumeTime > 0f ? 0f : spewRate * shownPower * surge * deltaTime;

            while (spewOwed >= 1f)
            {
                spewOwed -= 1f;

                // Mostly outward and up, out of the top of the object.
                Vector3 direction = (Random.onUnitSphere + Vector3.up * 0.6f).normalized;

                Mote rune = MakeMote("Spewed rune", centre + direction * spewFrom);
                rune.drawing.AddRune(RuneGlyphs.Rune(runeIndex++), Vector3.zero, Vector3.forward, spewRuneSize,
                                     runeWidth, 0f, 1f);
                rune.drawing.Progress = 0f;
                rune.velocity = direction * Random.Range(spewSpeed.x, spewSpeed.y);
                rune.life = Random.Range(spewLife.x, spewLife.y);
                rune.roll = Random.Range(-30f, 30f);
                runes.Add(rune);
            }
        }

        private void Ripple(float deltaTime)
        {
            rippleOwed += consumeTime > 0f ? 0f : rippleRate * shownPower * surge * deltaTime;

            while (rippleOwed >= 1f)
            {
                rippleOwed -= 1f;

                // Drawn at the halo's size and scaled out from there.
                Mote ripple = MakeMote("Ripple", Centre);
                ripple.drawing.AddRune(Circle(), Vector3.zero, Vector3.forward, haloRadius * 2f, auraWidth, 0f, 1f);
                ripple.drawing.Progress = 1f;
                ripple.life = rippleLife;
                ripples.Add(ripple);
            }
        }

        private void UpdateRunes(Quaternion facing, float deltaTime)
        {
            float drag = Mathf.Exp(-spewDrag * deltaTime);

            for (int i = runes.Count - 1; i >= 0; i--)
            {
                Mote rune = runes[i];
                rune.age += deltaTime;

                if (rune.age >= rune.life)
                {
                    Discard(runes, i);
                    continue;
                }

                rune.velocity *= drag;
                rune.transform.position += rune.velocity * deltaTime;

                // The drawing faces the camera along its local up, so a
                // roll about that tilts it on screen.
                rune.transform.rotation = facing * Quaternion.AngleAxis(rune.roll, Vector3.up);

                rune.drawing.Progress = rune.age / spewWriteTime;
                rune.drawing.SetColour(colour);
                rune.drawing.SetFade((1f - Mathf.InverseLerp(spewFadeFrom * rune.life, rune.life, rune.age)) * visible);
            }
        }

        private void UpdateRipples(Quaternion facing, float deltaTime)
        {
            float reach = rippleReach / haloRadius;

            for (int i = ripples.Count - 1; i >= 0; i--)
            {
                Mote ripple = ripples[i];
                ripple.age += deltaTime;

                if (ripple.age >= ripple.life)
                {
                    Discard(ripples, i);
                    continue;
                }

                float t = ripple.age / ripple.life;

                // Fast out of the halo, slowing as it spreads.
                float eased = 1f - (1f - t) * (1f - t);

                ripple.transform.position = Centre;
                ripple.transform.rotation = facing;
                ripple.transform.localScale = Vector3.one * Mathf.Lerp(1f, reach, eased);
                ripple.drawing.SetColour(colour * Mathf.Lerp(0.6f, 1.2f, shownPower));
                ripple.drawing.SetFade((1f - t) * visible);
            }
        }

        private Mote MakeMote(string label, Vector3 position)
        {
            GameObject host = new GameObject(label);
            host.transform.SetParent(effects, false);
            host.transform.position = position;

            return new Mote { transform = host.transform, drawing = new RuneDrawing(host, material) };
        }

        private static void Discard(List<Mote> motes, int index)
        {
            Mote mote = motes[index];
            motes.RemoveAt(index);
            mote.drawing.Release();
            Destroy(mote.transform.gameObject);
        }

        private static List<List<Vector2>> Circle()
        {
            return new List<List<Vector2>> { RuneGlyphs.Arc(Vector2.zero, 1f, 90f, 450f, 64) };
        }

        private void OnDestroy()
        {
            haloDrawing?.Release();

            foreach (Mote mote in runes)
            {
                mote.drawing.Release();
            }

            foreach (Mote mote in ripples)
            {
                mote.drawing.Release();
            }

            if (effects != null)
            {
                Destroy(effects.gameObject);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, haloRadius);
        }
    }
}
