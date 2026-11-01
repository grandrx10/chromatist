using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Katana's column throw, on foot in an arena: the boss walks up to
    /// the nearest uncut column, stopping a little short of it, chops it
    /// into chunks and flings them at the player.
    /// </summary>
    /// <remarks>
    /// Everything from the cutting on is <see cref="KatanaColumnThrowPattern"/>'s:
    /// see there. Only getting there differs. In place of waiting for the
    /// column to come on screen, the boss walks at <see cref="walkSpeed"/>,
    /// facing the column, to a spot <see cref="standOff"/> from its foot, on
    /// the side the boss came from. It cuts once there, or once
    /// <see cref="maxWalkTime"/> is up, wherever it got to.
    ///
    /// The nearest uncut column is picked, out to Look Ahead's furthest;
    /// with none left, the attack is skipped. Side Reach, View Margin and
    /// Max Wait above are only for a run and do nothing here.
    ///
    /// Leave the Attack Animation and Range Limit above empty, and the
    /// Movement at Stand Still.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Katana Column Cut Grounded", fileName = "KatanaColumnCutGrounded")]
    public sealed class KatanaColumnCutGroundedPattern : KatanaColumnThrowPattern
    {
        [Header("Walking up")]
        [SerializeField]
        [Tooltip("How far from the column's foot, measured flat, the boss stops to cut it.")]
        [Min(0f)]
        private float standOff = 3f;

        [SerializeField]
        [Tooltip("Walking speed to the column, in units per second.")]
        [Min(0.1f)]
        private float walkSpeed = 6f;

        [SerializeField]
        [Tooltip("Close enough to the stopping spot to start cutting.")]
        [Min(0.01f)]
        private float arriveDistance = 0.3f;

        [SerializeField]
        [Tooltip("Most seconds spent walking. Past this the boss cuts from wherever it got to.")]
        [Min(0f)]
        private float maxWalkTime = 4f;

        protected override IEnumerator GetInPlace(Enemy enemy, CuttableColumn target)
        {
            // Stops on the side it comes from, so it never walks round the column.
            Vector3 side = Flat(enemy.transform.position - target.CutPoint);
            side = side.sqrMagnitude > 0.0001f ? side.normalized : -Flat(enemy.Model.forward).normalized;

            Vector3 spot = target.CutPoint + side * standOff;
            float walked = 0f;

            while (walked < maxWalkTime)
            {
                if (target == null || target.Taken || enemy.Health.IsDead)
                {
                    break;
                }

                Vector3 toSpot = Flat(spot - enemy.transform.position);
                float distance = toSpot.magnitude;

                if (distance <= arriveDistance)
                {
                    break;
                }

                // Eases in over the last stretch rather than overshooting.
                float speed = Mathf.Min(walkSpeed, distance / Mathf.Max(Time.deltaTime, 0.001f));
                enemy.VelocityOverride = toSpot / distance * speed;

                Vector3 toColumn = Flat(target.CutPoint - enemy.transform.position);

                if (toColumn.sqrMagnitude > 0.0001f)
                {
                    enemy.FacingOverride = toColumn.normalized;
                }

                yield return null;
                walked += Time.deltaTime;
            }

            enemy.VelocityOverride = null;
            enemy.FacingOverride = null;
        }

        protected override bool InPlace(Enemy enemy, CuttableColumn target) => true;

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        protected override void Reset()
        {
            base.Reset();
            pieceCount = new Vector2Int(10, 10);
            lookAhead = new Vector2(0f, 1000f);
        }
    }
}
