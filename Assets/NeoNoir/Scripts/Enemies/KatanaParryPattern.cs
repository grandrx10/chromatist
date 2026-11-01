using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The boss stands on guard: for a few seconds the katana parries
    /// everything the player throws at it or swings, glistening brighter
    /// with every parry. Then it lets loose what it caught -- curves of
    /// light shoot out across the floor from the boss, and walls of light
    /// flow out along them.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   0. For <see cref="windUp"/>, the katana glistens but the guard is
    ///      not yet up: the boss can be hurt, and nothing is parried or
    ///      stored.
    ///   1. For <see cref="duration"/>, the boss's <see cref="KatanaGuard"/>
    ///      is on, whatever it was before, and turns aside every projectile
    ///      and blow the usual way, the katana darting out to cut each one.
    ///      The katana glistens <see cref="glistenColour"/> throughout. Each
    ///      parry flashes a gleam on it and brightens its glow, and gleams
    ///      twinkle along it quicker and bigger, all building toward
    ///      <see cref="maxCuts"/>.
    ///   2. Each parry stores <see cref="cutsPerParry"/> cuts, up to
    ///      <see cref="maxCuts"/>; reaching that ends the guard early. Then
    ///      the guard goes back to how it was.
    ///   3. With nothing parried, the glisten fades and that is all.
    ///      Otherwise one last big gleam flashes and the cuts are let loose
    ///      as <see cref="KatanaLightCuts"/>: see there for how they look and
    ///      hurt. The glisten holds until the walls set off, then fades. The
    ///      attack waits for the last wall to run out.
    ///
    /// Needs a <see cref="KatanaGuard"/> and <see cref="BossBlades"/> on the
    /// boss, with the katana among the blades as <see cref="bladePrefab"/>;
    /// without them the attack is skipped. Meant for a boss standing still,
    /// so leave the Movement above at Stand Still.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Katana Parry", fileName = "KatanaParry")]
    public sealed class KatanaParryPattern : AttackPattern
    {
        [SerializeField]
        [Tooltip("The katana's prefab, as listed in the boss's Boss Blades. The blade spawned from it glistens.")]
        private GameObject bladePrefab;

        [Header("Guard")]
        [SerializeField]
        [Tooltip("Seconds the katana glistens before the guard goes up. The boss can be hurt meanwhile, and nothing is parried.")]
        [Min(0f)]
        private float windUp = 0.6f;

        [SerializeField]
        [Tooltip("Seconds the katana stays on guard, parrying everything.")]
        [Min(0.1f)]
        private float duration = 3f;

        [SerializeField]
        [Tooltip("Cuts stored for each parry.")]
        [Min(1)]
        private int cutsPerParry = 4;

        [SerializeField]
        [Tooltip("Most cuts that can be stored. Reaching it ends the guard early.")]
        [Min(1)]
        private int maxCuts = 24;

        [Header("Glisten")]
        [SerializeField]
        [Tooltip("Colour of the katana's glow and gleams.")]
        private Color glistenColour = new Color(0.75f, 0.95f, 1f);

        [SerializeField]
        [Tooltip("Brightness of the glow with nothing parried yet. Above 1 blooms.")]
        [Min(0f)]
        private float glowIntensity = 1.5f;

        [SerializeField]
        [Tooltip("What the glow's brightness is multiplied by once the cuts are full, building up with each parry.")]
        [Min(0f)]
        private float glowPeak = 4f;

        [SerializeField]
        [Tooltip("Seconds the glow takes to come on, and to go at the end.")]
        [Min(0f)]
        private float glowFadeTime = 0.2f;

        [SerializeField] private BladeGleam.Settings gleam = new BladeGleam.Settings { size = 1f, time = 0.35f };

        [SerializeField]
        [Tooltip("Where on the katana the gleams centre, in its own space. The tip is down -Y as built.")]
        private Vector3 gleamOffset;

        [SerializeField]
        [Tooltip("Gleams twinkle up to this far either way along the blade from the Gleam Offset.")]
        [Min(0f)]
        private float glistenSpread = 0.6f;

        [SerializeField]
        [Tooltip("Seconds between twinkling gleams with nothing parried, and once the cuts are full.")]
        private Vector2 glistenInterval = new Vector2(0.4f, 0.06f);

        [SerializeField]
        [Tooltip("Size of each twinkling gleam, as a multiple of the gleam's own, with nothing parried and once the cuts are full.")]
        private Vector2 glistenSize = new Vector2(0.4f, 1.1f);

        [Header("Wind-up glisten")]
        [SerializeField]
        [Tooltip("What the glow's brightness is multiplied by during the wind-up, building from half of it, so the coming parry reads clearly.")]
        [Min(0f)]
        private float windUpGlow = 3f;

        [SerializeField]
        [Tooltip("Seconds between gleams during the wind-up.")]
        [Min(0.01f)]
        private float windUpInterval = 0.08f;

        [SerializeField]
        [Tooltip("Size of each gleam during the wind-up, as a multiple of the gleam's own.")]
        [Min(0f)]
        private float windUpGleamSize = 1.4f;

        [SerializeField]
        [Tooltip("Size of the gleam flashed as the guard goes up.")]
        [Min(0f)]
        private float guardUpGleamSize = 2.2f;

        [Header("Parries")]
        [SerializeField]
        [Tooltip("Size of the gleam flashed at each parry, with nothing parried before and once the cuts are full.")]
        private Vector2 parryGleamSize = new Vector2(0.9f, 1.6f);

        [SerializeField]
        [Tooltip("Size of the gleam flashed as the cuts are let loose.")]
        [Min(0f)]
        private float finalGleamSize = 2.5f;

        [Header("Cuts")]
        [SerializeField] private KatanaLightCuts.Settings cuts = new KatanaLightCuts.Settings();

        public override bool CanPerform(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            return enemy.GetComponent<KatanaGuard>() != null && blades != null && blades.IndexOf(bladePrefab) >= 0;
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            KatanaGuard guard = enemy.GetComponent<KatanaGuard>();
            BossBlades blades = enemy.GetComponent<BossBlades>();
            int index = blades != null ? blades.IndexOf(bladePrefab) : -1;
            Transform katana = index >= 0 ? blades.BladeAt(index) : null;

            if (guard == null || katana == null)
            {
                yield break;
            }

            int cap = Mathf.Max(1, maxCuts);
            int stored = 0;
            float twinkleClock = 0f;
            float nextGleam = Mathf.Max(0.01f, glistenInterval.x);

            Color glowColour = glistenColour * glowIntensity;
            glowColour.a = glistenColour.a;
            HammerGlow glow = HammerGlow.On(katana.gameObject, null, glowColour, glowFadeTime);
            BladeGleam.Flash(katana, gleamOffset, glistenColour, gleam, glistenSize.x);

            void Twinkles(float charge)
            {
                while (twinkleClock >= nextGleam)
                {
                    BladeGleam.Flash(katana, Twinkle(), glistenColour, gleam, Mathf.Lerp(glistenSize.x, glistenSize.y, charge));
                    nextGleam += Mathf.Max(0.01f, Mathf.Lerp(glistenInterval.x, glistenInterval.y, charge));
                }
            }

            // 1. Glistening hard, guard still down: the boss can be hurt, nothing is stored.
            float nextWindUpGleam = 0f;

            for (float wound = 0f; wound < windUp; wound += Time.deltaTime)
            {
                if (enemy.Health.IsDead)
                {
                    FadeGlow(glow);
                    yield break;
                }

                if (glow != null)
                {
                    glow.GlowIntensity = Mathf.Lerp(windUpGlow * 0.5f, windUpGlow, wound / Mathf.Max(0.01f, windUp));
                }

                while (wound >= nextWindUpGleam)
                {
                    BladeGleam.Flash(katana, Twinkle(), glistenColour, gleam, windUpGleamSize);
                    nextWindUpGleam += Mathf.Max(0.01f, windUpInterval);
                }

                yield return null;
            }

            // The guard goes up with a flash.
            if (windUp > 0f)
            {
                BladeGleam.Flash(katana, gleamOffset, glistenColour, gleam, guardUpGleamSize);
            }

            void OnParried()
            {
                float before = stored / (float)cap;
                stored = Mathf.Min(cap, stored + Mathf.Max(1, cutsPerParry));
                BladeGleam.Flash(katana, Twinkle(), glistenColour, gleam, Mathf.Lerp(parryGleamSize.x, parryGleamSize.y, before));
            }

            // 2. On guard, catching cuts.
            bool wasGuarding = guard.Guarding;
            guard.Guarding = true;
            guard.Parried += OnParried;

            float clock = 0f;

            try
            {
                while (clock < duration && stored < cap)
                {
                    if (enemy.Health.IsDead)
                    {
                        break;
                    }

                    float charge = stored / (float)cap;

                    if (glow != null)
                    {
                        // Carries on from the wind-up's brightness, never dimming as the guard goes up.
                        float from = windUp > 0f ? Mathf.Min(windUpGlow, glowPeak) : 1f;
                        glow.GlowIntensity = Mathf.Lerp(from, glowPeak, charge);
                    }

                    Twinkles(charge);

                    yield return null;
                    clock += Time.deltaTime;
                    twinkleClock += Time.deltaTime;
                }
            }
            finally
            {
                // 3. Back to how it was.
                guard.Parried -= OnParried;
                guard.Guarding = wasGuarding;
            }

            // 4. Nothing caught, or the boss is gone: the glisten simply fades.
            if (stored == 0 || enemy.Health.IsDead)
            {
                FadeGlow(glow);
                yield break;
            }

            if (glow != null)
            {
                glow.GlowIntensity = glowPeak;
            }

            BladeGleam.Flash(katana, gleamOffset, glistenColour, gleam, finalGleamSize);
            KatanaLightCuts released = KatanaLightCuts.Release(enemy, stored, cuts);

            float waited = 0f;

            while (released != null && !released.Done)
            {
                // The glisten goes as the walls set off.
                if (glow != null && waited >= cuts.wallDelay)
                {
                    FadeGlow(glow);
                    glow = null;
                }

                yield return null;
                waited += Time.deltaTime;
            }

            FadeGlow(glow);
        }

        private Vector3 Twinkle()
        {
            return gleamOffset + Vector3.up * Random.Range(-glistenSpread, glistenSpread);
        }

        private void FadeGlow(HammerGlow glow)
        {
            if (glow != null)
            {
                glow.FadeOut(glowFadeTime);
            }
        }
    }
}
