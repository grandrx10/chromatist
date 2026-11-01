using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Rings of spear strikes closing in on the boss: a ring of warning
    /// circles far out, then another a little closer, and closer, each ring
    /// turned a little from the last, so the strikes wind in on the boss in
    /// spiral arms. Spears rain into each circle as its warning fills.
    /// </summary>
    /// <remarks>
    /// In order, from where the boss stands as it starts:
    ///   1. A ring of <see cref="circlesPerRing"/> circles, evenly spaced,
    ///      <see cref="startDistance"/> out, the first at a random angle.
    ///   2. Every <see cref="ringInterval"/> seconds another ring,
    ///      <see cref="ringStep"/> closer and turned a further
    ///      <see cref="ringTurn"/> degrees, down to no closer than
    ///      <see cref="endDistance"/>.
    ///
    /// The circles, spears and impacts are as in <see cref="SpearShotBase"/>;
    /// the rings can start well outside the arena and close in.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Spear Shot", fileName = "SpearShot")]
    public sealed class SpearShotPattern : SpearShotBase
    {
        [Header("Rings")]
        [SerializeField]
        [Tooltip("Circles in each ring, evenly spaced round the boss.")]
        [Min(1)]
        private int circlesPerRing = 6;

        [SerializeField]
        [Tooltip("Distance from the boss of the first, outermost ring.")]
        [Min(0f)]
        private float startDistance = 20f;

        [SerializeField]
        [Tooltip("How much closer each ring is than the last.")]
        [Min(0.1f)]
        private float ringStep = 4f;

        [SerializeField]
        [Tooltip("No ring comes closer to the boss than this.")]
        [Min(0f)]
        private float endDistance = 4f;

        [SerializeField]
        [Tooltip("Seconds between one ring appearing and the next.")]
        [Min(0f)]
        private float ringInterval = 0.3f;

        [SerializeField]
        [Tooltip("Degrees each ring is turned from the last, so the circles wind in as spiral arms. 0 lines them up as spokes.")]
        private float ringTurn = 15f;

        protected override List<Ring> PlanRings()
        {
            int count = endDistance > startDistance ? 1 : Mathf.FloorToInt((startDistance - endDistance) / ringStep) + 1;
            float startAngle = Random.Range(0f, 360f);
            List<Ring> rings = new List<Ring>(count);

            for (int i = 0; i < count; i++)
            {
                rings.Add(new Ring
                {
                    time = i * ringInterval,
                    distance = startDistance - i * ringStep,
                    angle = startAngle + i * ringTurn,
                    circles = circlesPerRing,
                });
            }

            return rings;
        }
    }
}
