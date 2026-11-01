using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LightAndShadow
{
    /// <summary>
    /// Luna's rainbow: the floor round her splits into bands of colour, her
    /// blade glistens in some of those colours one after another, and then
    /// blades rain down on those bands in the same order, each exploding in
    /// its colour. The outermost band explodes every time.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. Bands are laid round where she stands as the attack starts, one
    ///      per entry in <see cref="colours"/>, innermost first, each
    ///      <see cref="bandWidth"/> wide: the first a full disc, the last
    ///      covering everything further out, drawn out to
    ///      <see cref="arenaRadius"/>. They fade in over
    ///      <see cref="appearTime"/> and stay faintly shown, at
    ///      <see cref="faintness"/>, the whole attack.
    ///   2. Then each of <see cref="rounds"/> in turn. A round picks its
    ///      Colours at random from every band but the outermost, repeats
    ///      allowed, and her blade glistens in each,
    ///      <see cref="glistenInterval"/> apart. <see cref="strikeDelay"/>
    ///      after the last glisten began, the picked bands strike in the same
    ///      order, Strike Spacing apart.
    ///   3. Each strike: blades of the band's colour, as
    ///      <see cref="HallSpear"/>s, land scattered over the band and over
    ///      the outermost band, about <see cref="bladeSpacing"/> apart, at
    ///      most <see cref="maxBladesPerBand"/> per band. Both bands flash
    ///      bright in their own colours, the camera jolts, and a player in
    ///      either takes <see cref="damage"/>.
    ///   4. <see cref="roundGap"/> after a round's last strike, the next round
    ///      begins. Once the last has struck the bands fade away.
    ///
    /// She stands where she is throughout. Each glisten is one
    /// <see cref="BladeGleam"/> in the band's colour, lasting
    /// <see cref="glistenTime"/>. Set the sword model as the Spear Prefab, standing
    /// upright, tip down, with NeoNoir/Flat materials so it takes the colour;
    /// its Damage, Landing Burst and shake are ignored. Without one, the
    /// bands still flash and hurt. If she dies, the bands vanish.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Luna/Luna Rainbow", fileName = "LunaRainbow")]
    public sealed class LunaRainbowPattern : AttackPattern
    {
        private static readonly RaycastHit[] FloorHits = new RaycastHit[16];

        // Made ready a few a frame before the first round, so no frame builds many.
        private const int SpearsWarmedPerFrame = 12;

        /// <summary>One round: how many colours glisten, and the seconds between their strikes.</summary>
        [Serializable]
        public struct Round
        {
            [Tooltip("Colours the blade glistens in, then strikes, in this round.")]
            [Min(1)] public int colours;

            [Tooltip("Seconds between one strike and the next in this round.")]
            [Min(0f)] public float strikeSpacing;
        }

        [Header("Bands")]
        [SerializeField]
        [Tooltip("One band per colour, innermost first. The last is the outermost, covering the rest of the arena, and explodes on every strike.")]
        private Color[] colours =
        {
            new Color(1f, 0.05f, 0.05f, 1f),
            new Color(1f, 0.22f, 0f, 1f),
            new Color(1f, 0.95f, 0f, 1f),
            new Color(0.1f, 1f, 0.15f, 1f),
            new Color(0.1f, 0.35f, 1f, 1f),
            new Color(0.6f, 0.1f, 1f, 1f),
        };

        [SerializeField]
        [Tooltip("Width of each band but the outermost.")]
        [Min(0.1f)]
        private float bandWidth = 8f;

        [SerializeField]
        [Tooltip("How far out the outermost band is drawn and rained on. It hurts at any distance past its inner edge.")]
        [Min(0f)]
        private float arenaRadius = 80f;

        [SerializeField]
        [Tooltip("How strongly the bands show between strikes, 0 to 1.")]
        [Range(0f, 1f)]
        private float faintness = 1f;

        [SerializeField]
        [Tooltip("Seconds the bands take to fade in at the start, and out at the end.")]
        [Min(0f)]
        private float appearTime = 0.3f;

        [SerializeField]
        [Tooltip("Height above the floor the bands are drawn at.")]
        [Min(0f)]
        private float bandHeight = 0.03f;

        [SerializeField]
        [Tooltip("Layers the floor under her is looked for on. The bands are laid at its height; without it, at her pivot.")]
        private LayerMask floorMask = 1;

        [Header("Rounds")]
        [SerializeField]
        private Round[] rounds =
        {
            new Round { colours = 1, strikeSpacing = 0f },
            new Round { colours = 2, strikeSpacing = 0.5f },
            new Round { colours = 3, strikeSpacing = 0.2f },
            new Round { colours = 5, strikeSpacing = 0.2f },
        };

        [SerializeField]
        [Tooltip("Seconds after the bands appear before the first glisten.")]
        [Min(0f)]
        private float leadIn = 0.5f;

        [SerializeField]
        [Tooltip("Seconds between one glisten and the next within a round.")]
        [Min(0f)]
        private float glistenInterval = 0.5f;

        [SerializeField]
        [Tooltip("Seconds from a round's last glisten beginning to its first strike.")]
        [Min(0f)]
        private float strikeDelay = 1f;

        [SerializeField]
        [Tooltip("Seconds from a round's last strike to the next round's first glisten.")]
        [Min(0f)]
        private float roundGap = 1f;

        [Header("Glisten")]
        [SerializeField]
        [Tooltip("Seconds each glisten lasts, from appearing to gone. A glisten still showing when the next begins is cut off, so only one is ever seen.")]
        [Min(0.01f)]
        private float glistenTime = 0.45f;

        [SerializeField]
        [Tooltip("The one gleam each glisten is. Its Time is ignored for the Glisten Time above; its colour is the band's.")]
        private BladeGleam.Settings gleam = new BladeGleam.Settings();

        [SerializeField]
        [Tooltip("Where on her blade the gleam shows, in her own space.")]
        private Vector3 gleamOffset;

        [Header("Strike")]
        [SerializeField]
        [Tooltip("Damage to a player in a striking band, or in the outermost band, as it strikes.")]
        [Min(0f)]
        private float damage = 20f;

        [SerializeField]
        [Tooltip("How bright a band flashes as it strikes, as a multiple of its colour. Above 1 blooms.")]
        [Min(0f)]
        private float flashIntensity = 3f;

        [SerializeField]
        [Tooltip("Seconds a band's flash takes to ease back to faint.")]
        [Min(0.01f)]
        private float flashTime = 0.6f;

        [SerializeField]
        [Tooltip("How far the camera jolts each strike. 0 for none.")]
        [Min(0f)]
        private float shakeStrength = 0.5f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.25f;

        [Header("Blades")]
        [SerializeField]
        [Tooltip("Rough distance between the blades landing in a band. Smaller is more blades.")]
        [Min(0.5f)]
        private float bladeSpacing = 7f;

        [SerializeField]
        [Tooltip("Most blades landing in one band per strike.")]
        [Min(0)]
        private int maxBladesPerBand = 40;

        [SerializeField]
        [Tooltip("Brightness of the blades' colours. Above 1 blooms.")]
        [Min(0f)]
        private float bladeIntensity = 2.5f;

        [SerializeField]
        [Tooltip("The blades, as in the hall run. Set the Spear Prefab. Their Damage, Landing Burst and shake are ignored.")]
        private HallSpear.Settings spear = new HallSpear.Settings { lingerTime = 0.6f, leanAngle = 10f };

        /// <summary>One moment in the attack: a glisten or a strike, in one band.</summary>
        private struct Beat
        {
            public float time;
            public int band;
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            if (colours == null || colours.Length < 2)
            {
                Debug.LogWarning($"[{name}] needs at least two colours.", this);
                yield break;
            }

            int outermost = colours.Length - 1;
            Vector3 centre = enemy.transform.position;
            centre.y = FindFloor(centre, out float floorHeight) ? floorHeight : centre.y;
            PlayerHealth player = enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null;

            List<Beat> glistens = new List<Beat>();
            List<Beat> strikes = new List<Beat>();
            float end = Plan(outermost, glistens, strikes);

            RainbowBand[] bands = new RainbowBand[colours.Length];

            for (int i = 0; i < bands.Length; i++)
            {
                float inner = i * bandWidth;
                float outer = i == outermost ? Mathf.Max(arenaRadius, inner + 0.1f) : inner + bandWidth;

                bands[i] = RainbowBand.Create(centre + Vector3.up * bandHeight, inner, outer, colours[i], faintness, appearTime);
                bands[i].DestroyWhen(() => enemy == null || enemy.Health.IsDead);
            }

            // The gleam lasting the whole glisten, a copy so the asset is untouched.
            BladeGleam.Settings shade = JsonUtility.FromJson<BladeGleam.Settings>(JsonUtility.ToJson(gleam));
            shade.time = glistenTime;

            foreach (object frame in Warm(strikes, outermost))
            {
                yield return frame;
            }

            BladeGleam shine = null;
            int nextGlisten = 0;
            int nextThrow = 0;
            int nextStrike = 0;

            for (float clock = 0f; clock < end; )
            {
                while (nextGlisten < glistens.Count && clock >= glistens[nextGlisten].time)
                {
                    // Only ever one glisten showing.
                    StopGleam(shine);
                    shine = BladeGleam.Flash(enemy.transform, gleamOffset, colours[glistens[nextGlisten].band], shade);
                    nextGlisten++;
                }

                // Thrown early enough to land as the band strikes.
                while (nextThrow < strikes.Count && clock >= strikes[nextThrow].time - spear.flightTime)
                {
                    RainBlades(centre, strikes[nextThrow].band, outermost);
                    RainBlades(centre, outermost, outermost);
                    nextThrow++;
                }

                while (nextStrike < strikes.Count && clock >= strikes[nextStrike].time)
                {
                    Strike(bands, centre, strikes[nextStrike].band, outermost, player);
                    nextStrike++;
                }

                yield return null;
                clock += Time.deltaTime;

                if (enemy.Health.IsDead)
                {
                    StopGleam(shine);
                    yield break;
                }
            }

            foreach (RainbowBand band in bands)
            {
                if (band != null)
                {
                    band.FadeOut(appearTime);
                }
            }
        }

        private static void StopGleam(BladeGleam shine)
        {
            if (shine != null)
            {
                Destroy(shine.gameObject);
            }
        }

        /// <summary>
        /// Lays out every glisten and strike, each round picking its bands
        /// from all but the outermost. Returns when the last strike's flash
        /// has eased back.
        /// </summary>
        private float Plan(int outermost, List<Beat> glistens, List<Beat> strikes)
        {
            float time = appearTime + leadIn;
            float lastStrike = time;

            foreach (Round round in rounds)
            {
                int count = Mathf.Max(1, round.colours);
                float firstStrike = time + (count - 1) * glistenInterval + strikeDelay;

                for (int k = 0; k < count; k++)
                {
                    int band = Random.Range(0, outermost);
                    glistens.Add(new Beat { time = time + k * glistenInterval, band = band });
                    strikes.Add(new Beat { time = firstStrike + k * round.strikeSpacing, band = band });
                }

                lastStrike = firstStrike + (count - 1) * round.strikeSpacing;
                time = lastStrike + roundGap;
            }

            return lastStrike + flashTime;
        }

        /// <summary>One band strikes, and the outermost with it: both flash, the camera jolts, and a player in either is hurt.</summary>
        private void Strike(RainbowBand[] bands, Vector3 centre, int band, int outermost, PlayerHealth player)
        {
            if (bands[band] != null)
            {
                bands[band].Flash(flashIntensity, flashTime);
            }

            if (bands[outermost] != null)
            {
                bands[outermost].Flash(flashIntensity, flashTime);
            }

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }

            if (player == null || player.IsDead || damage <= 0f)
            {
                return;
            }

            Vector3 offset = player.transform.position - centre;
            offset.y = 0f;
            int standing = Mathf.Min(Mathf.FloorToInt(offset.magnitude / bandWidth), outermost);

            if (standing == band || standing == outermost)
            {
                player.TakeDamage(damage, centre);
            }
        }

        /// <summary>Throws a band's blades in, scattered evenly over it, to land in its colour.</summary>
        private void RainBlades(Vector3 centre, int band, int outermost)
        {
            if (spear.spearPrefab == null)
            {
                return;
            }

            float inner = band * bandWidth;
            float outer = band == outermost ? Mathf.Max(arenaRadius, inner) : inner + bandWidth;
            int count = BladesIn(band, outermost);

            Color tint = colours[band] * bladeIntensity;
            tint.a = 1f;

            for (int i = 0; i < count; i++)
            {
                // Even over the area, not bunched toward the middle.
                float distance = Mathf.Sqrt(Mathf.Lerp(inner * inner, outer * outer, Random.value));
                Vector3 outward = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) * Vector3.forward;
                Vector3 spot = centre + outward * distance;

                // Hurting, bursting and shaking are the band's job.
                HallSpear.Throw(spot, -outward, spear, null, quiet: true, tint: tint);
            }
        }

        /// <summary>How many blades land in a band each strike: one per Blade Spacing square of it, up to the most.</summary>
        private int BladesIn(int band, int outermost)
        {
            float inner = band * bandWidth;
            float outer = band == outermost ? Mathf.Max(arenaRadius, inner) : inner + bandWidth;
            float area = Mathf.PI * (outer * outer - inner * inner);
            return Mathf.Min(maxBladesPerBand, Mathf.CeilToInt(area / (bladeSpacing * bladeSpacing)));
        }

        /// <summary>Makes ready, a few a frame, the most blades the attack ever has out at once.</summary>
        private IEnumerable Warm(List<Beat> strikes, int outermost)
        {
            if (spear.spearPrefab == null)
            {
                yield break;
            }

            float life = spear.flightTime + spear.lingerTime + spear.sinkTime;
            int most = 0;

            for (int first = 0; first < strikes.Count; first++)
            {
                int blades = 0;

                for (int i = first; i < strikes.Count && strikes[i].time - strikes[first].time <= life; i++)
                {
                    blades += BladesIn(strikes[i].band, outermost) + BladesIn(outermost, outermost);
                }

                most = Mathf.Max(most, blades);
            }

            for (int ready = 0; ready < most; )
            {
                ready = Mathf.Min(most, ready + SpearsWarmedPerFrame);
                HallSpear.Prewarm(spear.spearPrefab, ready);
                yield return null;
            }
        }

        /// <summary>Height of the floor under <paramref name="spot"/>, if there is some below it or a little above.</summary>
        private bool FindFloor(Vector3 spot, out float floor)
        {
            const float Above = 4f;
            const float Below = 20f;

            Vector3 from = spot + Vector3.up * Above;
            int hits = Physics.RaycastNonAlloc(from, Vector3.down, FloorHits, Above + Below, floorMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            floor = default;

            // The nearest floor below, seen through her own body and the player.
            for (int i = 0; i < hits; i++)
            {
                RaycastHit hit = FloorHits[i];

                if (hit.distance < best
                    && hit.normal.y > 0.7f
                    && hit.collider.GetComponentInParent<PlayerMotor>() == null
                    && hit.collider.GetComponentInParent<Enemy>() == null)
                {
                    best = hit.distance;
                    floor = hit.point.y;
                }
            }

            return best < float.MaxValue;
        }

        private void Reset()
        {
            delayAfter = 1.5f;
            Movement.type = MovementType.StandStill;
        }
    }
}
