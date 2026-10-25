# Generates AxeHead.obj: a bearded axe head for Amber. Lives in a folder
# ending in ~ so Unity never imports it.
#
#   python axe_head.py > ../AxeHead.obj
#
# Laid out like ScytheHead: the head in the XY plane, flats (cheeks) facing
# +-Z. The handle runs along Y straight through the pivot, at the back of
# the head where it meets the handle. The edge faces +X once imported.
#
# Unity's OBJ importer mirrors X, so everything is written at -X here and
# the winding reversed to match; it lands the right way round in the editor.
#
# The head alone, no socket or poll: a grid from the handle out to a curved
# edge, thick at the back and thinning to a near-sharp edge, with a beard
# that sweeps down below the handle and a hollow underneath it. The cheeks,
# rims and edge each have their own vertices so the creases stay hard.
#
# About 0.75 out from the handle and 0.9 tall at the edge, to sit on a
# handle of roughly 2 units.

import math
import sys

NU = 18  # steps from the eye to the edge
NV = 28  # steps from the bottom of the beard to the top horn

# ---- shape ------------------------------------------------------------------

EYE_X = 0.0             # where the head leaves the handle
EYE_HALF = 0.15         # half height of the head where it meets the handle

BEARD = (0.50, -0.66)   # bottom of the edge: the tip of the beard
BULGE = (0.74, -0.16)   # pulls the edge outward into a curve
HORN = (0.66, 0.24)     # top of the edge

BEARD_HOLLOW = 0.22     # how far the underside curves up between eye and beard
TOP_HOLLOW = 0.01       # a slight dip along the top

EYE_THICK = 0.075       # half thickness of the bit at the eye
EDGE_THICK = 0.005      # half thickness at the edge
TAPER = 1.5             # higher thins the bit faster near the eye


def bezier(p0, p1, p2, t):
    a = (1 - t) * (1 - t)
    b = 2 * (1 - t) * t
    c = t * t
    return (a * p0[0] + b * p1[0] + c * p2[0], a * p0[1] + b * p1[1] + c * p2[1])


def bit_point(u, v):
    """Point on the bit's centre plane: u from eye (0) to edge (1), v from beard (0) to horn (1)."""
    back = (EYE_X, -EYE_HALF + 2 * EYE_HALF * v)
    edge = bezier(BEARD, BULGE, HORN, v)

    x = back[0] + (edge[0] - back[0]) * u
    y = back[1] + (edge[1] - back[1]) * u

    # Hollows along the top and bottom, fading toward the middle of the bit.
    arch = math.sin(math.pi * u)
    y += BEARD_HOLLOW * arch * (1 - v) ** 3
    y -= TOP_HOLLOW * arch * v ** 3
    return x, y


def half_thickness(u):
    return EDGE_THICK + (EYE_THICK - EDGE_THICK) * (1 - u) ** TAPER


# ---- mesh ------------------------------------------------------------------

surfaces = []  # each: (vertices, triangles), triangles index into its own vertices


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def add_surface(vertices, quads, outward):
    """Quads are split into triangles, each wound so it faces along outward(point)."""
    triangles = []

    for quad in quads:
        a, b, c, d = (vertices[i] for i in quad)
        centre = tuple((a[k] + b[k] + c[k] + d[k]) / 4 for k in range(3))
        normal = cross(sub(c, a), sub(d, b))

        if dot(normal, outward(centre)) < 0:
            quad = quad[::-1]

        triangles.append((quad[0], quad[1], quad[2]))
        triangles.append((quad[0], quad[2], quad[3]))

    surfaces.append((vertices, triangles))


def grid(rows, cols, at):
    """Vertices at(i, j) for i in 0..rows, j in 0..cols, and the quads between them."""
    vertices = [at(i, j) for i in range(rows + 1) for j in range(cols + 1)]
    quads = []

    for i in range(rows):
        for j in range(cols):
            k = i * (cols + 1) + j
            quads.append((k, k + 1, k + cols + 2, k + cols + 1))

    return vertices, quads


def bit():
    def face(side):
        def at(i, j):
            u, v = i / NU, j / NV
            x, y = bit_point(u, v)
            return (x, y, side * half_thickness(u))

        vertices, quads = grid(NU, NV, at)
        add_surface(vertices, quads, lambda p: (0, 0, side))

    face(1)
    face(-1)

    centre = (0.4, -0.05, 0.0)

    def outward_from_centre(p):
        return (p[0] - centre[0], p[1] - centre[1], 0)

    # Walls joining the two cheeks: along the bottom and top, at the edge,
    # and at the back where the head meets the handle.
    def wall(point_at, steps):
        def at(i, j):
            x, y, h = point_at(i / steps)
            return (x, y, h if j == 0 else -h)

        vertices, quads = grid(steps, 1, at)
        add_surface(vertices, quads, outward_from_centre)

    def along_u(v):
        return lambda u: (*bit_point(u, v), half_thickness(u))

    def along_v(u):
        return lambda v: (*bit_point(u, v), half_thickness(u))

    wall(along_u(0), NU)
    wall(along_u(1), NU)
    wall(along_v(1), NV)
    wall(along_v(0), NV)


bit()


# ---- write ------------------------------------------------------------------

def normalised(v):
    length = math.sqrt(dot(v, v))
    return (v[0] / length, v[1] / length, v[2] / length) if length > 1e-12 else (0.0, 0.0, 1.0)


out = sys.stdout
out.write("# Axe head: in the XY plane, flats facing Z, pivot where it meets the handle.\n")
out.write("# Generated by Source~/axe_head.py -- edit that, not this.\n")
out.write("o AxeHead\n")

lines_v, lines_n, lines_f = [], [], []
base = 1

for vertices, triangles in surfaces:
    # Smooth normals within a surface, from the triangles around each vertex.
    normals = [(0.0, 0.0, 0.0)] * len(vertices)

    for a, b, c in triangles:
        n = cross(sub(vertices[b], vertices[a]), sub(vertices[c], vertices[a]))
        for i in (a, b, c):
            normals[i] = (normals[i][0] + n[0], normals[i][1] + n[1], normals[i][2] + n[2])

    for (x, y, z), n in zip(vertices, normals):
        n = normalised(n)
        # Mirrored in X for Unity's importer, which mirrors it back.
        lines_v.append(f"v {-x:.6f} {y:.6f} {z:.6f}\n")
        lines_n.append(f"vn {-n[0]:.6f} {n[1]:.6f} {n[2]:.6f}\n")

    for a, b, c in triangles:
        # Reversed, since mirroring turned every triangle inside out.
        i, j, k = base + a, base + c, base + b
        lines_f.append(f"f {i}//{i} {j}//{j} {k}//{k}\n")

    base += len(vertices)

out.writelines(lines_v)
out.writelines(lines_n)
out.writelines(lines_f)
