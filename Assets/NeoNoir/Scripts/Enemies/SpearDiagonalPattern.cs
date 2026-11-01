using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// For <see cref="ChromatistPhaseThree"/>'s run: the boss's spear gleams,
    /// and spear strikes are called down one after another at random spots
    /// on the strip ahead of the player.
    /// </summary>
    /// <remarks>
    /// As the attack starts, the spear in the boss's <see cref="BossBlades"/>
    /// made from <see cref="bladePrefab"/> gleams where it orbits. Then
    /// <see cref="count"/> circles, picked at random, appear
    /// <see cref="circleInterval"/> seconds apart, each somewhere
    /// <see cref="aheadDistance"/> up the run from where the player is by
    /// then, and anywhere across the strip.
    ///
    /// The circles stay where they are laid. The strip is found from the
    /// scene's <see cref="WallRunStage"/>: its centre line runs up from the
    /// Strip Base. Without one, or out of a run, the circles are laid round
    /// the player's line, ahead being the way from the boss to them.
    ///
    /// The circles, spears and impacts are as in <see cref="SpearShotBase"/>;
    /// the spears are thrown in from ahead.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Spear Diagonal", fileName = "SpearDiagonal")]
    public sealed class SpearDiagonalPattern : SpearShotBase
    {
        [Header("Gleam")]
        [SerializeField]
        [Tooltip("The spear's prefab, as listed in the boss's Boss Blades. The blade spawned from it gleams as the attack starts.")]
        private GameObject bladePrefab;

        [SerializeField]
        [Tooltip("Colour of the gleam.")]
        private Color colour = new Color(1f, 0.75f, 0.2f);

        [SerializeField] private BladeGleam.Settings gleam = new BladeGleam.Settings();

        [SerializeField]
        [Tooltip("Nudges the gleam from the spear's tip, in the spear's own space. The tip is found from the model.")]
        private Vector3 gleamOffset;

        [SerializeField]
        [Tooltip("Size of the gleam, as a multiple of the gleam's own.")]
        [Min(0f)]
        private float gleamSize = 2f;

        [Header("Circles ahead")]
        [SerializeField]
        [Tooltip("Fewest and most circles called down, picked at random.")]
        private Vector2Int count = new Vector2Int(20, 25);

        [SerializeField]
        [Tooltip("Seconds between one circle appearing and the next.")]
        [Min(0f)]
        private float circleInterval = 0.05f;

        [SerializeField]
        [Tooltip("Nearest and furthest up the run from the player each circle lands, picked at random.")]
        private Vector2 aheadDistance = new Vector2(3f, 25f);

        [SerializeField]
        [Tooltip("Half the strip's width. Circles land anywhere up to this far to either side of its centre line.")]
        [Min(0f)]
        private float halfWidth = 6f;

        // The attack under way: who is throwing, and from which way.
        [NonSerialized] private Enemy thrower;
        [NonSerialized] private Vector3 ahead = Vector3.forward;

        public override IEnumerator Perform(Enemy enemy)
        {
            thrower = enemy;
            Gleam(enemy);
            return base.Perform(enemy);
        }

        /// <summary>The boss's spear gleams where it orbits.</summary>
        private void Gleam(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            int index = blades != null && bladePrefab != null ? blades.IndexOf(bladePrefab) : -1;
            Transform blade = index >= 0 ? blades.BladeAt(index) : null;

            if (blade != null)
            {
                BladeGleam.Flash(blade, BladeGleam.TipOf(blade) + gleamOffset, colour, gleam, gleamSize);
            }
        }

        protected override List<Ring> PlanRings()
        {
            List<Ring> rings = new List<Ring>();

            if (thrower == null)
            {
                return rings;
            }

            Vector3 boss = Flat(thrower.transform.position);
            Vector3 player = thrower.HasTarget ? Flat(thrower.Target.position) : boss;

            if (ChaseFrame.Active)
            {
                ahead = Flat(ChaseFrame.Forward).normalized;
            }
            else
            {
                Vector3 away = player - boss;
                ahead = away.sqrMagnitude > 0.0001f ? away.normalized : Flat(thrower.Model.forward).normalized;
            }

            Vector3 across = Vector3.Cross(Vector3.up, ahead);
            float speed = ChaseFrame.Active ? Vector3.Dot(ChaseFrame.Velocity, ahead) : 0f;

            // Where the strip's centre line is.
            WallRunStage stage = FindAnyObjectByType<WallRunStage>();
            Vector3 middle = stage != null && stage.StripBase != null ? Flat(stage.StripBase.position) : player;
            float playerAlong = Vector3.Dot(player - middle, ahead);

            int circles = UnityEngine.Random.Range(Mathf.Min(count.x, count.y), Mathf.Max(count.x, count.y) + 1);
            float near = Mathf.Min(aheadDistance.x, aheadDistance.y);
            float far = Mathf.Max(aheadDistance.x, aheadDistance.y);

            for (int i = 0; i < circles; i++)
            {
                // Ahead of where the player will be as it appears.
                float time = i * circleInterval;
                float along = playerAlong + speed * time + UnityEngine.Random.Range(near, far);
                Vector3 spot = middle + ahead * along + across * UnityEngine.Random.Range(-halfWidth, halfWidth);
                Vector3 offset = spot - boss;

                rings.Add(new Ring
                {
                    time = time,
                    distance = offset.magnitude,
                    angle = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg,
                    circles = 1,
                });
            }

            return rings;
        }

        protected override Vector3 ThrownFrom(Vector3 floor, Vector3 centre)
        {
            return ahead;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }
    }
}
