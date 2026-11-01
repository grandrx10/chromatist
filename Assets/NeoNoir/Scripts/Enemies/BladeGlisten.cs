using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A boss blade glistening as it builds to a strike: it glows, gleams
    /// twinkle up and down it, quicker and bigger as the strike nears, the
    /// glow brightening with them, and one last big gleam peaks just as it
    /// strikes.
    /// </summary>
    /// <remarks>
    /// Made by an attack with <see cref="Begin"/>, which starts the glow and
    /// the first gleam. The attack then calls <see cref="Tick"/> every frame
    /// with the seconds since it began, and <see cref="End"/> when the blade
    /// heads back, which fades the glow. The gleams are
    /// <see cref="BladeGleam"/>s and the glow a <see cref="HammerGlow"/>.
    /// </remarks>
    public sealed class BladeGlisten
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Colour of the glow and the gleams.")]
            public Color colour = new Color(0.6f, 0.2f, 1f);

            [Header("Glow")]
            [Tooltip("Brightness of the glow as it starts. Above 1 blooms.")]
            [Min(0f)] public float glowIntensity = 2.5f;

            [Tooltip("What the glow's brightness is multiplied by just before the strike, building up from 1.")]
            [Min(0f)] public float glowPeak = 2f;

            [Tooltip("Seconds the glow takes to come on, and to go at the end.")]
            [Min(0f)] public float glowFadeTime = 0.2f;

            [Header("Gleams")]
            public BladeGleam.Settings gleam = new BladeGleam.Settings();

            [Tooltip("Where on the blade the gleams centre, in the blade's own space.")]
            public Vector3 gleamOffset;

            [Tooltip("Gleams twinkle up to this far either way from the Gleam Offset, in the blade's own space: X, Y and Z each.")]
            public Vector3 gleamSpread = new Vector3(0.3f, 0.3f, 0.3f);

            [Tooltip("Seconds between gleams as it starts, and just before the strike.")]
            public Vector2 interval = new Vector2(0.3f, 0.07f);

            [Tooltip("Size of each gleam, as a multiple of the gleam's own, as it starts and just before the strike.")]
            public Vector2 size = new Vector2(0.5f, 1.1f);

            [Tooltip("Size of the last gleam, timed to be fully open just as it strikes.")]
            [Min(0f)] public float finalSize = 2.5f;
        }

        private readonly Settings settings;
        private readonly Transform blade;
        private readonly HammerGlow glow;
        private float strike;
        private float nextGleam;
        private bool finalDone;

        private BladeGlisten(Transform blade, Settings settings, float strike)
        {
            this.blade = blade;
            this.settings = settings;
            this.strike = Mathf.Max(0.01f, strike);

            Color glowColour = settings.colour * settings.glowIntensity;
            glowColour.a = settings.colour.a;
            glow = HammerGlow.On(blade.gameObject, null, glowColour, settings.glowFadeTime);

            BladeGleam.Flash(blade, settings.gleamOffset, settings.colour, settings.gleam);
            nextGleam = Mathf.Max(0.01f, settings.interval.x);
        }

        /// <summary>
        /// Starts <paramref name="blade"/> glistening, building to a strike
        /// <paramref name="strike"/> seconds from now.
        /// </summary>
        public static BladeGlisten Begin(Transform blade, Settings settings, float strike)
        {
            return new BladeGlisten(blade, settings, strike);
        }

        /// <summary>Glistens for <paramref name="clock"/> seconds since it began. Past the strike, only the glow holds.</summary>
        public void Tick(float clock)
        {
            float charge = Mathf.Clamp01(clock / strike);

            if (glow != null)
            {
                glow.GlowIntensity = Mathf.Lerp(1f, settings.glowPeak, charge * charge);
            }

            if (finalDone)
            {
                return;
            }

            // Fully open the moment it strikes.
            if (clock >= strike - settings.gleam.time * settings.gleam.openFraction)
            {
                finalDone = true;
                BladeGleam.Flash(blade, settings.gleamOffset, settings.colour, settings.gleam, settings.finalSize);
                return;
            }

            while (clock >= nextGleam)
            {
                float at = Mathf.Clamp01(nextGleam / strike);
                Vector3 spread = settings.gleamSpread;
                Vector3 spot = settings.gleamOffset + new Vector3(UnityEngine.Random.Range(-spread.x, spread.x),
                                                                  UnityEngine.Random.Range(-spread.y, spread.y),
                                                                  UnityEngine.Random.Range(-spread.z, spread.z));

                BladeGleam.Flash(blade, spot, settings.colour, settings.gleam, Mathf.Lerp(settings.size.x, settings.size.y, at));
                nextGleam += Mathf.Max(0.01f, Mathf.Lerp(settings.interval.x, settings.interval.y, at));
            }
        }

        /// <summary>
        /// Builds up again, to a new strike <paramref name="strike"/> seconds
        /// from now, with a fresh first gleam: for a blade that strikes again.
        /// The glow stays on, dropping back to its starting brightness. Count
        /// <see cref="Tick"/>'s clock from 0 again after this.
        /// </summary>
        public void Restart(float strike)
        {
            this.strike = Mathf.Max(0.01f, strike);
            finalDone = false;

            BladeGleam.Flash(blade, settings.gleamOffset, settings.colour, settings.gleam);
            nextGleam = Mathf.Max(0.01f, settings.interval.x);
        }

        /// <summary>Fades the glow away.</summary>
        public void End()
        {
            if (glow != null)
            {
                glow.FadeOut(settings.glowFadeTime);
            }
        }
    }
}
