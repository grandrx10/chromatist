using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// How the captive blades come into their fights: gone from the sky
    /// where <see cref="LunaCaptives"/> turned them into their bosses, they
    /// appear straight above where they will land and drop onto it.
    /// </summary>
    /// <remarks>
    /// The boss is posed for the landing before the fall, and stays so: only
    /// its root moves, straight down, gathering speed all the way. The
    /// landing itself, impact and damage, is the attack's own.
    /// </remarks>
    internal static class BladeDrop
    {
        /// <summary>
        /// Puts <paramref name="root"/> <paramref name="height"/> above
        /// <paramref name="landing"/> and drops it there over
        /// <paramref name="time"/>, filling <paramref name="warning"/>, if
        /// any, as it falls.
        /// </summary>
        public static IEnumerable Fall(Transform root, Vector3 landing, float height, float time, WarningCircle warning)
        {
            Vector3 top = landing + Vector3.up * height;
            root.position = top;

            for (float clock = 0f; clock < time;)
            {
                yield return null;
                clock += Time.deltaTime;

                float t = Mathf.Clamp01(clock / time);
                root.position = Vector3.Lerp(top, landing, t * t);

                if (warning != null)
                {
                    warning.Fill = t;
                }
            }

            root.position = landing;
        }
    }
}
