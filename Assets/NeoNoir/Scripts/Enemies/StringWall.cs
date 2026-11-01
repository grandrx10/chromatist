using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A wall of stationary <see cref="TargetedStrings"/> circles that sweeps
    /// across the arena one row at a time, from one side to the other.
    /// </summary>
    /// <remarks>
    /// The direction is one of the four square to the arena's main cube:
    /// along its local X or Z, either way. Each row runs the arena's full
    /// width, circles <see cref="spacing"/> apart, and all of a row's circles
    /// appear together. The next row, one spacing further on, appears only
    /// once every circle of the row before has struck and its strings are
    /// gone. The wall covers the arena's whole bounding square, the gaps of
    /// an L- or cross-shaped arena included; circles off the arena's floor
    /// sit at the height of its middle.
    ///
    /// Runs on its own object, so it carries on to the far side after the
    /// attack that started it is over. Stops, its circles letting go, if the
    /// boss dies.
    /// </remarks>
    public sealed class StringWall : MonoBehaviour
    {
        private Enemy owner;
        private Transform target;
        private ArenaBounds arena;
        private float spacing;
        private TargetedStrings.Settings circles;

        /// <summary>
        /// Starts a wall across <paramref name="arena"/> in a random one of
        /// the four directions, for <paramref name="boss"/>, aimed at
        /// <paramref name="player"/>.
        /// </summary>
        public static StringWall Sweep(Enemy boss, Transform player, ArenaBounds arena, float spacing,
                                       TargetedStrings.Settings circles)
        {
            StringWall wall = new GameObject("StringWall").AddComponent<StringWall>();
            wall.owner = boss;
            wall.target = player;
            wall.arena = arena;
            wall.spacing = Mathf.Max(0.1f, spacing);
            wall.circles = circles;
            wall.StartCoroutine(wall.Run(Random.Range(0, 4)));
            return wall;
        }

        private IEnumerator Run(int side)
        {
            arena.Extent(out Vector3 right, out Vector3 forward, out Vector2 min, out Vector2 max);

            // Travel along X for sides 0 and 1, along Z for 2 and 3; odd sides
            // run backwards.
            bool alongX = side < 2;
            bool backwards = (side & 1) == 1;

            Vector3 travel = alongX ? right : forward;
            Vector3 across = alongX ? forward : right;
            float travelMin = alongX ? min.x : min.y;
            float travelMax = alongX ? max.x : max.y;
            float acrossMin = alongX ? min.y : min.x;
            float acrossMax = alongX ? max.y : max.x;

            int rows = Mathf.Max(1, Mathf.FloorToInt((travelMax - travelMin) / spacing));
            int columns = Mathf.Max(1, Mathf.FloorToInt((acrossMax - acrossMin) / spacing));

            // Spread evenly, each row and column in the middle of its strip.
            float rowStep = (travelMax - travelMin) / rows;
            float columnStep = (acrossMax - acrossMin) / columns;

            Vector3 centre = arena.Centre;
            List<TargetedStrings> row = new List<TargetedStrings>();

            for (int r = 0; r < rows; r++)
            {
                if (Gone())
                {
                    break;
                }

                int index = backwards ? rows - 1 - r : r;
                float along = travelMin + rowStep * (index + 0.5f);

                row.Clear();

                for (int c = 0; c < columns; c++)
                {
                    float sideways = acrossMin + columnStep * (c + 0.5f);
                    Vector3 spot = centre + travel * along + across * sideways;
                    TargetedStrings circle = StringRingPattern.Place(owner, target, arena, spot, circles);

                    if (circle != null)
                    {
                        row.Add(circle);
                    }
                }

                // Until every circle of this row has struck and let go.
                while (!Gone() && row.Exists(circle => circle != null))
                {
                    yield return null;
                }
            }

            Destroy(gameObject);
        }

        private bool Gone()
        {
            return owner == null || owner.Health.IsDead || target == null;
        }
    }
}
