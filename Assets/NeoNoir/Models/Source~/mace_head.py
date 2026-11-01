# Generates MaceHead.obj: a spiked mace head, a ball studded with cones.
# Lives in a folder ending in ~ so Unity never imports it.
#
#   python mace_head.py > ../MaceHead.obj
#
# The pivot is the middle of the ball. The handle comes in from +Y, where a
# patch round the top is left bare of spikes for it, so the head is the -Y
# end, as the other blades' tips are.
#
# The ball is smooth; each spike is a cone with its own vertices, so the
# crease where it meets the ball and the line of its sides stay hard. The
# spikes are spread evenly over the ball on a Fibonacci spiral.
#
# Unity's OBJ importer mirrors X, so everything is written at -X here and
# the winding reversed to match; it lands the right way round in the editor.
#
# About 1 across the ball and 1.7 across the spike tips.

import math
import sys

# ---- shape ------------------------------------------------------------------

BALL_RADIUS = 0.5
BALL_RINGS = 16         # latitude steps
BALL_SEGMENTS = 24      # longitude steps

SPIKES = 18             # spikes over the whole ball, before the handle's patch is cleared
SPIKE_LENGTH = 0.38     # from the ball's surface to the tip
SPIKE_RADIUS = 0.13     # radius of each spike's base
SPIKE_SIDES = 8
SPIKE_SINK = 0.04       # how far each base sinks into the ball, so no gap shows round it

HANDLE_CLEAR = 35.0     # degrees round +Y left bare for the handle


def normalised(v):
    length = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    return (v[0] / length, v[1] / length, v[2] / length) if length > 1e-12 else (0.0, 0.0, 1.0)


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def scale(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


# Each: (vertices, normals, triangles), triangles index into its own vertices
# and are wound counter-clockwise seen from outside.
surfaces = []


def ball():
    vertices, normals, triangles = [], [], []

    for i in range(BALL_RINGS + 1):
        polar = math.pi * i / BALL_RINGS
        for j in range(BALL_SEGMENTS + 1):
            around = 2 * math.pi * j / BALL_SEGMENTS
            n = (math.sin(polar) * math.cos(around), math.cos(polar), math.sin(polar) * math.sin(around))
            vertices.append(scale(n, BALL_RADIUS))
            normals.append(n)

    row = BALL_SEGMENTS + 1
    for i in range(BALL_RINGS):
        for j in range(BALL_SEGMENTS):
            a = i * row + j
            b = a + 1
            c = a + row
            d = c + 1
            # Seen from outside, top row a b, bottom row c d.
            if i > 0:
                triangles.append((a, b, c))
            if i < BALL_RINGS - 1:
                triangles.append((b, d, c))

    surfaces.append((vertices, normals, triangles))


def spike_directions():
    """Evenly over the ball on a Fibonacci spiral, minus the handle's patch round +Y."""
    golden = math.pi * (3 - math.sqrt(5))
    clear = math.cos(math.radians(HANDLE_CLEAR))
    directions = []

    for i in range(SPIKES):
        y = 1 - 2 * (i + 0.5) / SPIKES
        r = math.sqrt(max(0.0, 1 - y * y))
        theta = golden * i
        d = (r * math.cos(theta), y, r * math.sin(theta))

        if d[1] < clear:
            directions.append(d)

    return directions


def spike(direction):
    # Two directions square to the spike, for its base circle.
    helper = (0.0, 1.0, 0.0) if abs(direction[1]) < 0.9 else (1.0, 0.0, 0.0)
    u = normalised(cross(direction, helper))
    v = cross(direction, u)

    base_centre = scale(direction, BALL_RADIUS - SPIKE_SINK)
    tip = scale(direction, BALL_RADIUS + SPIKE_LENGTH)
    slant = SPIKE_LENGTH + SPIKE_SINK

    vertices, normals, triangles = [], [], []

    # Each side its own pair of vertices, flat shaded, with the tip split
    # per side so the faces stay crisp.
    for k in range(SPIKE_SIDES):
        a0 = 2 * math.pi * k / SPIKE_SIDES
        a1 = 2 * math.pi * (k + 1) / SPIKE_SIDES
        p0 = add(base_centre, add(scale(u, math.cos(a0) * SPIKE_RADIUS), scale(v, math.sin(a0) * SPIKE_RADIUS)))
        p1 = add(base_centre, add(scale(u, math.cos(a1) * SPIKE_RADIUS), scale(v, math.sin(a1) * SPIKE_RADIUS)))

        mid = (a0 + a1) * 0.5
        radial = add(scale(u, math.cos(mid)), scale(v, math.sin(mid)))
        # Square to the slanted side: out, and tipped toward the tip.
        n = normalised(add(scale(radial, slant), scale(direction, SPIKE_RADIUS)))

        at = len(vertices)
        vertices += [p0, p1, tip]
        normals += [n, n, n]
        triangles.append((at, at + 1, at + 2))

    # Wound so each faces outward, away from the spike's axis.
    fixed = []
    for a, b, c in triangles:
        n = cross(sub(vertices[b], vertices[a]), sub(vertices[c], vertices[a]))
        out = sub(vertices[a], base_centre)
        fixed.append((a, b, c) if dot(n, out) > 0 else (a, c, b))

    surfaces.append((vertices, normals, fixed))


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


ball()

# The ball's own winding, checked the same way, in case the row order above
# came out inside out.
vertices, normals, triangles = surfaces[0]
fixed = []
for a, b, c in triangles:
    n = cross(sub(vertices[b], vertices[a]), sub(vertices[c], vertices[a]))
    centre = scale(add(add(vertices[a], vertices[b]), vertices[c]), 1 / 3)
    fixed.append((a, b, c) if dot(n, centre) > 0 else (a, c, b))
surfaces[0] = (vertices, normals, fixed)

for direction in spike_directions():
    spike(direction)


# ---- write ------------------------------------------------------------------

out = sys.stdout
out.write("# Mace head: a spiked ball, pivot at its middle, handle in from +Y, head toward -Y.\n")
out.write("# Generated by Source~/mace_head.py -- edit that, not this.\n")
out.write("o MaceHead\n")

lines_v, lines_n, lines_f = [], [], []
base = 1

for vertices, normals, triangles in surfaces:
    for (x, y, z), (nx, ny, nz) in zip(vertices, normals):
        # Mirrored in X for Unity's importer, which mirrors it back.
        lines_v.append(f"v {-x:.6f} {y:.6f} {z:.6f}\n")
        lines_n.append(f"vn {-nx:.6f} {ny:.6f} {nz:.6f}\n")

    for a, b, c in triangles:
        # Reversed, since mirroring turned every triangle inside out.
        i, j, k = base + a, base + c, base + b
        lines_f.append(f"f {i}//{i} {j}//{j} {k}//{k}\n")

    base += len(vertices)

out.writelines(lines_v)
out.writelines(lines_n)
out.writelines(lines_f)
