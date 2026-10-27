using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The witch draws on her altars: every lit altar with its core still
    /// standing sends an arc of runes into her, and each rune she absorbs
    /// heals her a little.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The attack's own animation (WitchHeal) plays. She is made
    ///      invulnerable, shown by her <see cref="RunicShield"/> if she has
    ///      one -- held up for the heal, see <see cref="RunicShield.Hold"/>
    ///      -- or else by a red <see cref="HexShield"/>
    ///      (<see cref="shieldPrefab"/>), and a
    ///      <see cref="RuneCircle"/> is written on the floor at her feet.
    ///   2. <see cref="runeStartTime"/> seconds in, each empowering
    ///      <see cref="Altar"/> -- lit, core not broken -- sends a burst of
    ///      <see cref="runesPerAltar"/> runes from its core along a
    ///      <see cref="RuneArc"/> into the middle of her body, timed so the
    ///      last arrives as the attack ends.
    ///   3. Each rune that reaches her dissolves into her with a small
    ///      <see cref="EnergyBurst"/> and heals her
    ///      <see cref="healPerAltar"/> / <see cref="runesPerAltar"/>, so an
    ///      altar whose every rune arrives heals her the full amount, never
    ///      past her max health.
    ///   4. <see cref="duration"/> seconds after the start, the attack is
    ///      over: the shield dissolves, taking the invulnerability with it,
    ///      and the circle fades out.
    ///
    /// Breaking an altar's core mid-heal cuts its stream: the runes still on
    /// the way dissolve and heal nothing. With no altar empowering at all she
    /// skips the attack entirely and goes straight on to her next.
    ///
    /// The arcs aim where she stands when they start and do not follow her,
    /// so leave the attack's Movement on Stand Still. Altars are found
    /// through <see cref="Altar.All"/>: every altar in the scene counts.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Witch Heal", fileName = "WitchHeal")]
    public sealed class WitchHealPattern : AttackPattern
    {
        [Header("Timing")]
        [SerializeField]
        [Tooltip("Seconds the whole attack lasts, from the start of the animation.")]
        [Min(0.1f)]
        private float duration = 3f;

        [SerializeField]
        [Tooltip("Seconds into the attack before the altars start sending runes.")]
        [Min(0f)]
        private float runeStartTime = 0.3f;

        [Header("Healing")]
        [SerializeField]
        [Tooltip("Health each empowering altar restores, if every one of its runes arrives.")]
        [Min(0f)]
        private float healPerAltar = 100f;

        [SerializeField]
        [Tooltip("Runes each altar sends. Each heals Heal Per Altar divided by this.")]
        [Min(1)]
        private int runesPerAltar = 10;

        [SerializeField]
        [Tooltip("Seconds a cut-off stream takes to dissolve when its altar's core breaks.")]
        [Min(0.01f)]
        private float cutOffFade = 0.25f;

        [Header("Runes")]
        [SerializeField]
        private RuneArc.Settings arc = new RuneArc.Settings
        {
            arcHeight = 3f,
            spacing = 1.5f,
        };

        [SerializeField]
        [Tooltip("Burst at the witch as each rune is absorbed.")]
        private EnergyBurst.Settings absorbFlash = new EnergyBurst.Settings
        {
            flashSize = 0.6f,
            flashTime = 0.12f,
            sparkCount = 4,
            sparkSize = 0.15f,
            sparkSpeed = new Vector2(2f, 4f),
            sparkLife = 0.2f,
        };

        [Header("Shield")]
        [SerializeField]
        [Tooltip("Art for the shield round her while she heals, e.g. HexBarrier, if she has no runic shield to show instead. Needs no scripts; it is given its own. Empty: still invulnerable, just invisibly.")]
        private GameObject shieldPrefab;

        [SerializeField] private HexShield.Settings shield = new HexShield.Settings();

        [Header("Circle")]
        [SerializeField]
        [Tooltip("Seconds the circle at her feet takes to fade out once the heal is over.")]
        [Min(0.01f)]
        private float circleFadeTime = 0.8f;

        [SerializeField]
        private RuneCircle.Settings circle = new RuneCircle.Settings
        {
            radius = 3f,
            runeCount = 6,
            drawTime = 1f,
        };

        private sealed class Stream
        {
            public Altar altar;
            public RuneArc arc;
        }

        /// <summary>Only with at least one altar to draw on.</summary>
        public override bool CanPerform(Enemy enemy)
        {
            foreach (Altar altar in Altar.All)
            {
                if (altar != null && altar.IsEmpowering)
                {
                    return true;
                }
            }

            return false;
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            // Her runic shield, if the summoning gave her one, shows in
            // place of the hex art; the hex shield still makes her
            // invulnerable, invisibly.
            RunicShield runic = RunicShield.For(enemy.Health);

            if (runic != null)
            {
                runic.Hold();
            }

            HexShield raised = HexShield.Raise(runic != null ? null : shieldPrefab, enemy.Health, shield);
            RuneCircle written = RuneCircle.Create(RuneCircle.FloorBelow(enemy.transform.position, enemy.transform),
                                                   circle);

            yield return Channel(enemy);

            if (raised != null)
            {
                raised.Dismiss();
            }

            if (runic != null)
            {
                runic.Unhold();
            }

            if (written != null)
            {
                written.FadeOut(circleFadeTime);
            }
        }

        private IEnumerator Channel(Enemy enemy)
        {
            float elapsed = 0f;

            if (runeStartTime > 0f)
            {
                yield return new WaitForSeconds(Mathf.Min(runeStartTime, duration));
                elapsed = runeStartTime;
            }

            List<Stream> streams = new List<Stream>();
            Vector3 body = enemy.Health.BodyBounds().center;
            float flowTime = Mathf.Max(0.1f, duration - elapsed);
            float perRune = healPerAltar / runesPerAltar;

            foreach (Altar altar in Altar.All)
            {
                if (altar == null || !altar.IsEmpowering)
                {
                    continue;
                }

                Stream stream = new Stream
                {
                    altar = altar,
                    arc = RuneArc.CreateBurst(altar.CorePoint, body, arc, runesPerAltar, flowTime),
                };

                stream.arc.RuneArrived += arrived => Absorb(enemy, stream.altar, perRune);
                streams.Add(stream);
            }

            while (elapsed < duration)
            {
                foreach (Stream stream in streams)
                {
                    // A broken core cuts its stream; the rest heal nothing.
                    if (stream.arc != null && !stream.altar.IsEmpowering)
                    {
                        stream.arc.Dissolve(cutOffFade);
                        stream.arc = null;
                    }
                }

                yield return null;
                elapsed += Time.deltaTime;
            }
        }

        private void Absorb(Enemy enemy, Altar altar, float amount)
        {
            if (enemy == null || altar == null || !altar.IsEmpowering)
            {
                return;
            }

            enemy.Health.Heal(amount);
            EnergyBurst.Create(enemy.Health.BodyBounds().center, absorbFlash);
        }
    }
}
