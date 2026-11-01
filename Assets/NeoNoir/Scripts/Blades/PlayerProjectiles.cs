using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A player attack that flies on its own -- an arrow, a note, a slash --
    /// and that an enemy can knock out of the air before it lands.
    /// </summary>
    public interface IParryableProjectile
    {
        /// <summary>Where it is now.</summary>
        Vector3 Position { get; }

        /// <summary>
        /// Stops it where it is, still showing and harmless, until
        /// <see cref="Deflect"/>. During a run it keeps riding along with the
        /// run, the way it was carried in flight.
        /// </summary>
        void Hold();

        /// <summary>Ends its flight on the spot, harmlessly, the way it ends on meeting a wall.</summary>
        void Deflect();
    }

    /// <summary>
    /// Every player projectile in flight, so a guard such as
    /// <see cref="KatanaGuard"/> can find what is coming at it without
    /// searching the scene.
    /// </summary>
    /// <remarks>
    /// Projectiles add themselves on launch and take themselves off when
    /// their flight ends, however it ends.
    /// </remarks>
    public static class PlayerProjectiles
    {
        private static readonly List<IParryableProjectile> active = new List<IParryableProjectile>();

        public static IReadOnlyList<IParryableProjectile> Active => active;

        public static void Add(IParryableProjectile projectile)
        {
            if (!active.Contains(projectile))
            {
                active.Add(projectile);
            }
        }

        public static void Remove(IParryableProjectile projectile)
        {
            active.Remove(projectile);
        }

        // Static state survives leaving play mode when domain reload is off.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad()
        {
            active.Clear();
        }
    }
}
