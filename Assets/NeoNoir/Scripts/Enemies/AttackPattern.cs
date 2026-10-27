using System;
using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>How an enemy moves while one of its attacks, and the wait after it, plays out.</summary>
    public enum MovementType
    {
        /// <summary>Stays where it is.</summary>
        StandStill,

        /// <summary>Ambles in one random direction, then another, never straying far from where the attack began.</summary>
        Idle,

        /// <summary>Walks toward the player, stopping a little way short.</summary>
        Chase,
    }

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
    /// Every attack carries a <see cref="Movement"/>. The enemy follows it
    /// from the moment the attack starts until the next attack does, so it
    /// covers the wait in <see cref="DelayAfter"/> too. Which way the enemy
    /// walks never decides which way it looks: it always turns toward the
    /// player.
    ///
    /// An attack with a <see cref="MaxRange"/> cannot start from that far
    /// from the player or further: the enemy first walks toward the player,
    /// at <see cref="ApproachSpeed"/>, until within
    /// <see cref="ApproachDistance"/>, and only then begins it.
    ///
    /// To add a new kind of attack, subclass this and give it a
    /// CreateAssetMenu, the way <see cref="AimedShotPattern"/> does.
    /// </remarks>
    public abstract class AttackPattern : ScriptableObject
    {
        [Serializable]
        public sealed class MovementSettings
        {
            public MovementType type = MovementType.StandStill;

            [Tooltip("Walking speed, in units per second.")]
            [Min(0f)] public float speed = 2f;

            [Header("Idle")]
            [Tooltip("The enemy never wanders further than this from where the attack began.")]
            [Min(0f)] public float wanderRadius = 3f;

            [Tooltip("Shortest and longest seconds spent walking one way before picking another.")]
            public Vector2 wanderLegTime = new Vector2(0.6f, 1.6f);

            [Header("Chase")]
            [Tooltip("Stops walking once this close to the player.")]
            [Min(0f)] public float stopDistance = 2.5f;
        }

        [SerializeField]
        [Tooltip("Seconds the enemy waits after this attack finishes before starting its next one.")]
        [Min(0f)]
        protected float delayAfter = 5f;

        [SerializeField]
        [Tooltip("Animator state played for the attack. The enemy goes back to its idle state when the attack ends. Empty plays nothing.")]
        private string attackAnimation = "";

        [SerializeField] private MovementSettings movement = new MovementSettings();

        [Header("Range")]
        [SerializeField]
        [Tooltip("The attack cannot start from this far from the player or further; the enemy walks closer first. 0 attacks from any distance.")]
        [Min(0f)]
        private float rangeLimit;

        [SerializeField]
        [Tooltip("When out of range, walks toward the player until this close before attacking. Kept inside the range limit.")]
        [Min(0f)]
        private float rangeApproachDistance = 15f;

        [SerializeField]
        [Tooltip("Walking speed while closing in, in units per second.")]
        [Min(0.1f)]
        private float rangeApproachSpeed = 3f;

        public float DelayAfter => delayAfter;

        public float MaxRange => rangeLimit;

        public float ApproachDistance => Mathf.Min(rangeApproachDistance, rangeLimit);

        public float ApproachSpeed => rangeApproachSpeed;

        public string AttackAnimation => attackAnimation;

        public MovementSettings Movement => movement;

        /// <summary>Seconds to blend into <see cref="AttackAnimation"/>. Negative uses the enemy's usual blend.</summary>
        public virtual float AttackAnimationBlend => -1f;

        /// <summary>Seconds to blend back to idle once the attack ends. Negative uses the enemy's usual blend.</summary>
        public virtual float IdleReturnBlend => -1f;

        /// <summary>
        /// Whether the attack can be performed right now. When false the
        /// enemy skips it, with no animation and no Delay After, and moves on
        /// to its next attack.
        /// </summary>
        public virtual bool CanPerform(Enemy enemy) => true;

        /// <summary>
        /// Plays the attack out. The enemy waits for it to finish before
        /// counting down <see cref="DelayAfter"/>.
        /// </summary>
        public abstract IEnumerator Perform(Enemy enemy);
    }
}
