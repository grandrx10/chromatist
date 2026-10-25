using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One attack an <see cref="Enemy"/> can perform. An asset, so the same
    /// attack can be dropped into any phase of any enemy.
    /// </summary>
    /// <remarks>
    /// The asset is shared by every enemy that uses it, so it must hold no
    /// state of its own while running. Everything an attack needs to remember
    /// lives in local variables inside <see cref="Perform"/>, which runs as a
    /// coroutine on the enemy.
    ///
    /// To add a new kind of attack, subclass this and give it a
    /// CreateAssetMenu, the way <see cref="AimedShotPattern"/> does.
    /// </remarks>
    public abstract class AttackPattern : ScriptableObject
    {
        [SerializeField]
        [Tooltip("Seconds the enemy waits after this attack finishes before starting its next one.")]
        [Min(0f)]
        private float delayAfter = 5f;

        public float DelayAfter => delayAfter;

        /// <summary>
        /// Plays the attack out. The enemy waits for it to finish before
        /// counting down <see cref="DelayAfter"/>.
        /// </summary>
        public abstract IEnumerator Perform(Enemy enemy);
    }
}
