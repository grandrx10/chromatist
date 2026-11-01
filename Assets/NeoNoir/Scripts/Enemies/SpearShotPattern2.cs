using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Alternating rings of spear strikes round the boss: the odd rings
    /// first, outside in, leaving bands of safe ground between them; then
    /// the even rings, outside in, striking those bands, so the player must
    /// step out of a gap into ground the first wave has just struck.
    /// </summary>
    /// <remarks>
    /// Rings are counted from the outside: ring 1 is
    /// <see cref="startDistance"/> out, each next ring
    /// <see cref="ringStep"/> closer, down to no closer than
    /// <see cref="endDistance"/>.
    ///   1. Rings 1, 3, 5... appear one every <see cref="ringInterval"/>
    ///      seconds, outside in.
    ///   2. <see cref="waveGap"/> seconds after the last of them, rings 2, 4,
    ///      6... appear the same way.
    ///
    /// Each ring's circles are spread evenly round it, about
    /// <see cref="circleSpacing"/> apart, so outer rings have more; each ring
    /// starts at a random angle. A ring right on the boss is one circle.
    ///
    /// For the bands between odd rings to be safe, Ring Step should be at
    /// least twice the Circle Radius, and Circle Spacing no more than twice
    /// it so each ring has no holes.
    ///
    /// The circles, spears and impacts are as in <see cref="SpearShotBase"/>.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Spear Shot 2", fileName = "SpearShot2")]
    public sealed class SpearShotPattern2 : SpearShotBase
    {
        [Header("Rings")]
        [SerializeField]
        [Tooltip("Distance from the boss of ring 1, the outermost.")]
        [Min(0f)]
        private float startDistance = 20f;

        [SerializeField]
        [Tooltip("How much closer each ring is than the last. At least twice the Circle Radius leaves safe ground between the odd rings.")]
        [Min(0.1f)]
        private float ringStep = 5f;

        [SerializeField]
        [Tooltip("No ring comes closer to the boss than this. 0 lets the last ring be one circle on the boss.")]
        [Min(0f)]
        private float endDistance = 0f;

        [SerializeField]
        [Tooltip("Rough distance between the centres of neighbouring circles in a ring. No more than twice the Circle Radius leaves no holes.")]
        [Min(0.1f)]
        private float circleSpacing = 4f;

        [SerializeField]
        [Tooltip("Seconds between one ring of a wave appearing and the next, outside in.")]
        [Min(0f)]
        private float ringInterval = 0.08f;

        [SerializeField]
        [Tooltip("Seconds from the last odd ring appearing to the first even ring.")]
        [Min(0f)]
        private float waveGap = 0.6f;

        protected override List<Ring> PlanRings()
        {
            int count = endDistance > startDistance ? 1 : Mathf.FloorToInt((startDistance - endDistance) / ringStep) + 1;
            List<Ring> rings = new List<Ring>(count);

            // Index 0 is ring 1, so the even indices are the odd rings.
            int oddRings = (count + 1) / 2;
            float evenStart = (oddRings - 1) * ringInterval + waveGap;

            for (int i = 0; i < count; i++)
            {
                bool odd = i % 2 == 0;
                int place = i / 2;
                float distance = startDistance - i * ringStep;

                rings.Add(new Ring
                {
                    time = odd ? place * ringInterval : evenStart + place * ringInterval,
                    distance = distance,
                    angle = Random.Range(0f, 360f),
                    circles = Mathf.Max(1, Mathf.RoundToInt(2f * Mathf.PI * distance / circleSpacing)),
                });
            }

            return rings;
        }
    }
}
