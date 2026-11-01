# Generates KatanaBlade.obj: the blade of a katana alone, no habaki, guard
# or hilt. Lives in a folder ending in ~ so Unity never imports it.
#
#   python katana_blade.py > ../KatanaBlade.obj
#
# Laid out like the other blades: in the XY plane, flats facing +-Z, tip
# down -Y. The pivot is the middle of the base, where the blade would meet
# the guard. The edge faces +X once imported and the back -X.
#
# Unity's OBJ importer mirrors X, so everything is written at -X here and
# the winding reversed to match; it lands the right way round in the editor.
#
# Shinogi-zukuri: across the blade, a peaked back (iori-mune), a ridge line
# (shinogi) about a third of the way from the back where it is thickest,
# and flats running down from it to the edge. The blade curves gently back
# toward the spine along its length (sori), the edge on the outside of the
# curve. At the tip a crease (yokote) starts the point (kissaki), whose
# edge sweeps round in a curve to meet the back. Every face strip has its
# own vertices so the creases stay hard.
#
# About 2.2 long and 0.14 wide at the base.

import math
import sys

NB = 40  # steps along the body, base to yokote
NK = 14  # steps along the kissaki, yokote to tip

# ---- shape ------------------------------------------------------------------

LENGTH = 2.2            # base to tip, along Y
KISSAKI = 0.17          # length of the point, from the yokote to the tip
SORI = 0.1              # how far the tip curves back toward the spine

BASE_WIDTH = 0.14       # back to edge, at the base
YOKOTE_WIDTH = 0.10     # back to edge, where the point begins

SHINOGI = 0.32          # where the ridge runs, as a fraction from back to edge
MUNE = 0.07             # where the back's slopes end, as a fraction from back to edge

SHINOGI_THICK = (0.026, 0.018)  # half thickness at the ridge, at the base and the yokote
MUNE_THICK = (0.016, 0.011)     # half thickness where the back's slopes end

TIP_BACK = 0.12         # how far in from the back line the very tip sits, as a fraction of the yokote width


def lerp(a, b, t):
    return a + (b - a) * t


def spine(s):
    """The back of the blade at s along it (0 base, LENGTH tip): its point and the unit direction across to the edge."""
    t = s / LENGTH
    x = -SORI * t * t
    y = -s
    # Tangent (dx/ds, -1), turned a quarter toward the edge.
    tx, ty = -2 * SORI * t / LENGTH, -1.0
    length = math.hypot(tx, ty)
    tx, ty = tx / length, ty / length
    return (x, y), (-ty, tx)


def section(s):
    """
    Where the back and the edge sit across the blade at s, measured from
    the back line, and how thick it is there as a fraction of the yokote's.
    Plain along the body; in the kissaki the edge sweeps round to the tip
    and the blade thins to nothing.
    """
    yokote = LENGTH - KISSAKI

    if s <= yokote:
        u = s / yokote
        width = lerp(BASE_WIDTH, YOKOTE_WIDTH, u)
        return 0.0, width, lerp(SHINOGI_THICK[0], SHINOGI_THICK[1], u), lerp(MUNE_THICK[0], MUNE_THICK[1], u)

    t = min(1.0, (s - yokote) / KISSAKI)
    tip = TIP_BACK * YOKOTE_WIDTH
    back = tip * t * t
    # A quarter ellipse: the fukura, the point's curved edge.
    edge = tip + (YOKOTE_WIDTH - tip) * math.sqrt(max(0.0, 1 - t * t))
    thin = math.sqrt(max(0.0, 1 - t))
    return back, edge, SHINOGI_THICK[1] * thin, MUNE_THICK[1] * thin


def profile(s):
    """
    The cross-section at s as a loop of points round the blade: the back
    ridge, the far back slope, the far shinogi, the edge, the near shinogi,
    the near back slope.
    """
    (px, py), (nx, ny) = spine(s)
    back, edge, shinogi_thick, mune_thick = section(s)
    span = edge - back

    def at(fraction, z):
        a = back + span * fraction
        return (px + nx * a, py + ny * a, z)

    return [
        at(0.0, 0.0),
        at(MUNE, mune_thick),
        at(SHINOGI, shinogi_thick),
        at(1.0, 0.0),
        at(SHINOGI, -shinogi_thick),
        at(MUNE, -mune_thick),
    ]


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


def strips(stations):
    """One surface per side of the section, between each pair of neighbouring profile points, along the stations."""
    profiles = [profile(s) for s in stations]
    sides = len(profiles[0])

    for k in range(sides):
        vertices = []

        for p in profiles:
            vertices.append(p[k])
            vertices.append(p[(k + 1) % sides])

        quads = [(2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2) for i in range(len(stations) - 1)]

        def outward(point, stations=stations):
            # Away from the blade's middle at that height.
            s = min(stations, key=lambda st: abs(spine(st)[0][1] - point[1]))
            (px, py), (nx, ny) = spine(s)
            back, edge, _, _ = section(s)
            middle = (back + edge) * 0.5
            return (point[0] - (px + nx * middle), point[1] - (py + ny * middle), point[2])

        add_surface(vertices, quads, outward)


def base_cap():
    """Closes the base, where the guard would hide it."""
    loop = profile(0.0)
    centre = tuple(sum(p[k] for p in loop) / len(loop) for k in range(3))
    vertices = [centre] + loop
    triangles = []

    for i in range(len(loop)):
        a, b = 1 + i, 1 + (i + 1) % len(loop)
        # Facing up +Y, out of the base.
        n = cross(sub(vertices[a], centre), sub(vertices[b], centre))
        triangles.append((0, a, b) if n[1] > 0 else (0, b, a))

    surfaces.append((vertices, triangles))


yokote = LENGTH - KISSAKI
strips([yokote * i / NB for i in range(NB + 1)])
strips([yokote + KISSAKI * i / NK for i in range(NK + 1)])
base_cap()

# Pivot at the middle of the base.
(bx, by), (bnx, bny) = spine(0.0)
mid = BASE_WIDTH * 0.5
pivot = (bx + bnx * mid, by + bny * mid, 0.0)


# ---- write ------------------------------------------------------------------

def normalised(v):
    length = math.sqrt(dot(v, v))
    return (v[0] / length, v[1] / length, v[2] / length) if length > 1e-12 else (0.0, 0.0, 1.0)


out = sys.stdout
out.write("# Katana blade: in the XY plane, flats facing Z, tip down -Y, edge +X, pivot at the middle of the base.\n")
out.write("# Generated by Source~/katana_blade.py -- edit that, not this.\n")
out.write("o KatanaBlade\n")

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
        x, y, z = x - pivot[0], y - pivot[1], z - pivot[2]
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
