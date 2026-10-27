using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The ritual runes, as lines in a -1 to 1 square with the top up, for
    /// <see cref="RuneDrawing"/> to lay out and draw.
    /// </summary>
    /// <remarks>
    /// Four runes, which <see cref="Rune"/> cycles through:
    ///   the hook -- a curl over the top falling into a spiral,
    ///   the hourglass -- two cups mouth to mouth on a spine,
    ///   the bowtie -- a bar with two legs crossing beneath it,
    ///   the orbit -- a circle with a small one on either shoulder.
    /// Each rune is a list of lines, written in order. A line whose last
    /// point is its first is a closed loop.
    /// </remarks>
    public static class RuneGlyphs
    {
        public const int Count = 4;

        /// <summary>The <paramref name="index"/>th rune, cycling through the four.</summary>
        public static List<List<Vector2>> Rune(int index)
        {
            switch (((index % Count) + Count) % Count)
            {
                case 0: return Hook();
                case 1: return Hourglass();
                case 2: return Bowtie();
                default: return Orbit();
            }
        }

        /// <summary>A curl over the top, falling away into a spiral.</summary>
        public static List<List<Vector2>> Hook()
        {
            List<Vector2> line = Arc(new Vector2(0.1f, 0.5f), 0.32f, 200f, -45f, 24);
            line.AddRange(Spiral(new Vector2(0f, -0.45f), 0.48f, 0.07f, 115f, 600f, 64));
            return new List<List<Vector2>> { line };
        }

        /// <summary>Two cups mouth to mouth, nearly touching, on a spine.</summary>
        public static List<List<Vector2>> Hourglass()
        {
            return new List<List<Vector2>>
            {
                new List<Vector2> { new Vector2(0f, -1f), new Vector2(0f, 1f) },
                Parabola(new Vector2(0f, 0.12f), 0.58f, 0.85f, 20),
                Parabola(new Vector2(0f, -0.12f), 0.58f, -0.85f, 20),
            };
        }

        /// <summary>A bar across the top, with two legs crossing beneath it.</summary>
        public static List<List<Vector2>> Bowtie()
        {
            return new List<List<Vector2>>
            {
                new List<Vector2>
                {
                    new Vector2(-0.72f, -0.8f),
                    new Vector2(0.82f, 0.6f),
                    new Vector2(-0.82f, 0.6f),
                    new Vector2(0.72f, -0.8f),
                },
            };
        }

        /// <summary>A circle with a small circle on either shoulder.</summary>
        public static List<List<Vector2>> Orbit()
        {
            return new List<List<Vector2>>
            {
                Arc(Vector2.zero, 0.55f, 90f, 450f, 40),
                Arc(new Vector2(0.45f, 0.45f), 0.3f, 225f, 585f, 24),
                Arc(new Vector2(-0.45f, -0.45f), 0.3f, 45f, 405f, 24),
            };
        }

        /// <summary>A small diamond, for spacing between runes.</summary>
        public static List<List<Vector2>> Diamond()
        {
            return new List<List<Vector2>>
            {
                new List<Vector2>
                {
                    new Vector2(0f, 1f), new Vector2(0.6f, 0f), new Vector2(0f, -1f), new Vector2(-0.6f, 0f), new Vector2(0f, 1f),
                },
            };
        }

        /// <summary>An arc from one angle to another, either way round. Ending a full turn on is a closed circle.</summary>
        public static List<Vector2> Arc(Vector2 centre, float radius, float fromDegrees, float toDegrees, int steps)
        {
            List<Vector2> points = new List<Vector2>(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                float angle = Mathf.Lerp(fromDegrees, toDegrees, (float)i / steps) * Mathf.Deg2Rad;
                points.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }

            if (Mathf.Abs(Mathf.Abs(toDegrees - fromDegrees) - 360f) < 0.01f)
            {
                points[points.Count - 1] = points[0];
            }

            return points;
        }

        /// <summary>A spiral winding anticlockwise from one radius to another.</summary>
        public static List<Vector2> Spiral(Vector2 centre, float fromRadius, float toRadius, float fromDegrees,
                                           float sweepDegrees, int steps)
        {
            List<Vector2> points = new List<Vector2>(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                float angle = (fromDegrees + sweepDegrees * t) * Mathf.Deg2Rad;
                points.Add(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Mathf.Lerp(fromRadius, toRadius, t));
            }

            return points;
        }

        /// <summary>A cup with its lowest point at <paramref name="vertex"/>. A negative height turns it over.</summary>
        public static List<Vector2> Parabola(Vector2 vertex, float halfWidth, float height, int steps)
        {
            List<Vector2> points = new List<Vector2>(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                float x = Mathf.Lerp(-1f, 1f, (float)i / steps);
                points.Add(vertex + new Vector2(x * halfWidth, height * x * x));
            }

            return points;
        }

        public static float PathLength(List<Vector2> line)
        {
            float length = 0f;

            for (int i = 1; i < line.Count; i++)
            {
                length += Vector2.Distance(line[i - 1], line[i]);
            }

            return length;
        }
    }
}
