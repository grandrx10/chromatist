using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Cuts geometry in two along a plane, closing each half with a cap where
    /// the cut went through, so the halves read as solid.
    /// </summary>
    /// <remarks>
    /// Works on a <see cref="Piece"/>: plain lists of positions, normals and
    /// UVs with a triangle list per material, all in one shared space (the
    /// world, for <see cref="KatanaCut"/>). The last triangle list is always
    /// the cut faces, so cutting a piece again carries its earlier caps along
    /// like any other surface.
    ///
    /// The cap is a fan from the middle of the cut outline. That is exact
    /// for a convex cross-section -- a column, a block, a beam -- and close
    /// enough for most others.
    /// </remarks>
    public static class MeshSlicer
    {
        /// <summary>Geometry being cut, in one space shared by every piece.</summary>
        public sealed class Piece
        {
            public readonly List<Vector3> positions = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<Vector2> uvs = new List<Vector2>();

            /// <summary>One triangle list per material; the last is the cut faces.</summary>
            public readonly List<List<int>> submeshes = new List<List<int>>();

            public Piece(int submeshCount)
            {
                for (int i = 0; i < submeshCount; i++)
                {
                    submeshes.Add(new List<int>());
                }
            }

            public int CapSubmesh => submeshes.Count - 1;

            public bool IsEmpty
            {
                get
                {
                    foreach (List<int> triangles in submeshes)
                    {
                        if (triangles.Count > 0)
                        {
                            return false;
                        }
                    }

                    return true;
                }
            }

            public Bounds GetBounds()
            {
                if (positions.Count == 0)
                {
                    return new Bounds();
                }

                Bounds bounds = new Bounds(positions[0], Vector3.zero);

                foreach (Vector3 position in positions)
                {
                    bounds.Encapsulate(position);
                }

                return bounds;
            }

            public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv)
            {
                positions.Add(position);
                normals.Add(normal);
                uvs.Add(uv);
                return positions.Count - 1;
            }

            /// <summary>A mesh of this piece, its vertices moved so <paramref name="origin"/> is at its pivot.</summary>
            public Mesh ToMesh(Vector3 origin)
            {
                List<Vector3> local = new List<Vector3>(positions.Count);

                foreach (Vector3 position in positions)
                {
                    local.Add(position - origin);
                }

                Mesh mesh = new Mesh { name = "Cut piece" };

                if (local.Count > 65000)
                {
                    mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                }

                mesh.SetVertices(local);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = submeshes.Count;

                for (int i = 0; i < submeshes.Count; i++)
                {
                    mesh.SetTriangles(submeshes[i], i);
                }

                mesh.RecalculateBounds();
                return mesh;
            }
        }

        /// <summary>
        /// Cuts <paramref name="piece"/> along <paramref name="plane"/>.
        /// <paramref name="above"/> is the half on the side the plane's normal
        /// points to. Returns false, leaving both null, if the plane misses it.
        /// </summary>
        public static bool Slice(Piece piece, Plane plane, float capUvScale, out Piece above, out Piece below)
        {
            return Slice(piece, plane, capUvScale, out above, out below, null);
        }

        /// <summary>
        /// As <see cref="Slice(Piece, Plane, float, out Piece, out Piece)"/>,
        /// also adding the outline of the cut to <paramref name="cutOutline"/>,
        /// as pairs of points, one pair per edge.
        /// </summary>
        public static bool Slice(Piece piece, Plane plane, float capUvScale, out Piece above, out Piece below, List<Vector3> cutOutline)
        {
            above = null;
            below = null;

            int count = piece.positions.Count;
            float[] distance = new float[count];
            bool anyAbove = false;
            bool anyBelow = false;

            for (int i = 0; i < count; i++)
            {
                distance[i] = plane.GetDistanceToPoint(piece.positions[i]);

                if (distance[i] >= 0f)
                {
                    anyAbove = true;
                }
                else
                {
                    anyBelow = true;
                }
            }

            if (!anyAbove || !anyBelow)
            {
                return false;
            }

            int submeshCount = piece.submeshes.Count;
            Builder up = new Builder(piece, submeshCount);
            Builder down = new Builder(piece, submeshCount);
            List<Vector3> cutEdges = new List<Vector3>();

            for (int s = 0; s < submeshCount; s++)
            {
                List<int> triangles = piece.submeshes[s];

                for (int t = 0; t + 2 < triangles.Count; t += 3)
                {
                    int a = triangles[t];
                    int b = triangles[t + 1];
                    int c = triangles[t + 2];

                    bool sideA = distance[a] >= 0f;
                    bool sideB = distance[b] >= 0f;
                    bool sideC = distance[c] >= 0f;

                    if (sideA == sideB && sideB == sideC)
                    {
                        (sideA ? up : down).CopyTriangle(s, a, b, c);
                        continue;
                    }

                    // Turned so the vertex alone on its side comes first,
                    // keeping the winding.
                    if (sideA == sideB)
                    {
                        Split(piece, distance, s, c, a, b, sideC ? up : down, sideC ? down : up, cutEdges);
                    }
                    else if (sideA == sideC)
                    {
                        Split(piece, distance, s, b, c, a, sideB ? up : down, sideB ? down : up, cutEdges);
                    }
                    else
                    {
                        Split(piece, distance, s, a, b, c, sideA ? up : down, sideA ? down : up, cutEdges);
                    }
                }
            }

            // Each half's cap faces away from it, into the cut.
            Cap(up.piece, cutEdges, -plane.normal, capUvScale);
            Cap(down.piece, cutEdges, plane.normal, capUvScale);

            above = up.piece;
            below = down.piece;

            if (cutOutline != null)
            {
                cutOutline.AddRange(cutEdges);
            }

            return !above.IsEmpty && !below.IsEmpty;
        }

        /// <summary>
        /// Splits a triangle the plane runs through. <paramref name="lone"/> is
        /// alone on its side, <paramref name="lone"/>, <paramref name="next"/>,
        /// <paramref name="last"/> in winding order.
        /// </summary>
        private static void Split(Piece piece, float[] distance, int submesh, int lone, int next, int last,
                                  Builder loneSide, Builder pairSide, List<Vector3> cutEdges)
        {
            float toNext = distance[lone] / (distance[lone] - distance[next]);
            float toLast = distance[lone] / (distance[lone] - distance[last]);

            Vector3 positionNext = Vector3.Lerp(piece.positions[lone], piece.positions[next], toNext);
            Vector3 positionLast = Vector3.Lerp(piece.positions[lone], piece.positions[last], toLast);
            Vector3 normalNext = Vector3.Lerp(piece.normals[lone], piece.normals[next], toNext).normalized;
            Vector3 normalLast = Vector3.Lerp(piece.normals[lone], piece.normals[last], toLast).normalized;
            Vector2 uvNext = Vector2.Lerp(piece.uvs[lone], piece.uvs[next], toNext);
            Vector2 uvLast = Vector2.Lerp(piece.uvs[lone], piece.uvs[last], toLast);

            int loneA = loneSide.Copy(lone);
            int loneNext = loneSide.piece.AddVertex(positionNext, normalNext, uvNext);
            int loneLast = loneSide.piece.AddVertex(positionLast, normalLast, uvLast);
            loneSide.AddTriangle(submesh, loneA, loneNext, loneLast);

            int pairNext = pairSide.Copy(next);
            int pairLast = pairSide.Copy(last);
            int pairCutNext = pairSide.piece.AddVertex(positionNext, normalNext, uvNext);
            int pairCutLast = pairSide.piece.AddVertex(positionLast, normalLast, uvLast);
            pairSide.AddTriangle(submesh, pairCutNext, pairNext, pairLast);
            pairSide.AddTriangle(submesh, pairCutNext, pairLast, pairCutLast);

            cutEdges.Add(positionNext);
            cutEdges.Add(positionLast);
        }

        /// <summary>Closes the cut on <paramref name="piece"/> with faces turned toward <paramref name="facing"/>.</summary>
        private static void Cap(Piece piece, List<Vector3> cutEdges, Vector3 facing, float uvScale)
        {
            if (cutEdges.Count < 2)
            {
                return;
            }

            Vector3 centre = Vector3.zero;

            foreach (Vector3 point in cutEdges)
            {
                centre += point;
            }

            centre /= cutEdges.Count;

            // Flat UVs across the cut, so a textured cap is not smeared.
            Vector3 right = Vector3.Cross(facing, Mathf.Abs(facing.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 upward = Vector3.Cross(right, facing);

            Vector2 Uv(Vector3 point)
            {
                Vector3 offset = point - centre;
                return new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, upward)) * uvScale;
            }

            int middle = piece.AddVertex(centre, facing, Uv(centre));
            List<int> cap = piece.submeshes[piece.CapSubmesh];

            for (int i = 0; i + 1 < cutEdges.Count; i += 2)
            {
                Vector3 a = cutEdges[i];
                Vector3 b = cutEdges[i + 1];

                Vector3 normal = Vector3.Cross(a - centre, b - centre);

                if (normal.sqrMagnitude < 1e-12f)
                {
                    continue;
                }

                int ia = piece.AddVertex(a, facing, Uv(a));
                int ib = piece.AddVertex(b, facing, Uv(b));

                cap.Add(middle);

                // Wound so its face normal -- the cross product of its first
                // two edges, as Unity takes it -- points toward facing.
                if (Vector3.Dot(normal, facing) > 0f)
                {
                    cap.Add(ia);
                    cap.Add(ib);
                }
                else
                {
                    cap.Add(ib);
                    cap.Add(ia);
                }
            }
        }

        /// <summary>One half under construction, copying vertices over from the source once each.</summary>
        private sealed class Builder
        {
            public readonly Piece piece;
            private readonly Piece source;
            private readonly Dictionary<int, int> copied = new Dictionary<int, int>();

            public Builder(Piece source, int submeshCount)
            {
                this.source = source;
                piece = new Piece(submeshCount);
            }

            public int Copy(int index)
            {
                if (!copied.TryGetValue(index, out int at))
                {
                    at = piece.AddVertex(source.positions[index], source.normals[index], source.uvs[index]);
                    copied[index] = at;
                }

                return at;
            }

            public void CopyTriangle(int submesh, int a, int b, int c)
            {
                AddTriangle(submesh, Copy(a), Copy(b), Copy(c));
            }

            public void AddTriangle(int submesh, int a, int b, int c)
            {
                List<int> triangles = piece.submeshes[submesh];
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
        }
    }
}
