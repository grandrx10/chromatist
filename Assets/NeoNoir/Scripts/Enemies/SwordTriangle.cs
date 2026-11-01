using System;
using System.Collections;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Three swords split out of an enemy, stab into the floor round a
    /// triangle, and join up with lasers between orbs above them. While the
    /// lasers are up, a player outside the triangle is chased by homing
    /// missiles from every orb, and debris rains down outside it.
    /// </summary>
    /// <remarks>
    /// In order, from <see cref="Create"/>:
    ///   1. Split: the swords appear at the owner's model, at
    ///      <see cref="Settings.startScale"/> of their size, and fly out over
    ///      <see cref="Settings.flyTime"/> along a curve -- each bending the
    ///      same way round -- growing to full size, to hang
    ///      <see cref="Settings.stabHeight"/> above their corners, point down,
    ///      flat facing out.
    ///   2. Stab: after <see cref="Settings.hangTime"/> they plunge, speeding
    ///      up, sinking their point <see cref="Settings.stabDepth"/> into the
    ///      floor over <see cref="Settings.stabTime"/>, each with a
    ///      <see cref="HammerImpact"/> and a jolt of the camera.
    ///   3. Orbs: an orb grows above each, <see cref="Settings.orbHeight"/>
    ///      over the floor, over <see cref="Settings.orbGrowTime"/>.
    ///   4. Charge: for <see cref="Settings.chargeTime"/> thin warning lines
    ///      join the orbs, widening as the charge builds, and the orbs pulse.
    ///   5. Lasers: the lines become <see cref="SweepBeam"/>s from each orb to
    ///      the next, hurting the player standing in them. From here, until
    ///      <see cref="Dismiss"/>: while the player stands outside the
    ///      triangle, every orb fires a <see cref="HomingMissile"/> at them
    ///      <see cref="Settings.missilesPerSecond"/> times a second; and
    ///      <see cref="Settings.debrisPerSecond"/> <see cref="MaceDebris"/>
    ///      chunks a second drop straight down from the sky on spots outside
    ///      the triangle, at least <see cref="Settings.debrisClearance"/> from
    ///      it, within <see cref="Settings.debrisRange"/> of the player, and
    ///      inside the owner's <see cref="Enemy.Arena"/> if it has one.
    ///   6. <see cref="Dismiss"/>: the lasers narrow away, the orbs shrink,
    ///      and the swords pull out and fly back along their curves into the
    ///      owner, shrinking as they go. Then it destroys itself.
    ///
    /// If the owner dies, is destroyed or is disabled first, the lasers stop
    /// and the swords and orbs shrink away where they are.
    ///
    /// The swords are pure looks, copied from the prefab's
    /// <see cref="ArtTemplate"/>, scripts and colliders stripped, so nothing
    /// can hit or break them. They are posed built tip down local -Y, flat
    /// facing local +Z, with <see cref="Settings.swordRotation"/> for art
    /// built otherwise. How far each point reaches below its pivot is
    /// measured from its renderers.
    ///
    /// The lasers run at the orbs' height, so they only hurt a player whose
    /// body reaches within the laser's radius of it: keep the Orb Height
    /// about chest height, or widen the laser.
    /// </remarks>
    public sealed class SwordTriangle : MonoBehaviour
    {
        private const int Corners = 3;
        private const int DebrisTries = 8;

        [Serializable]
        public sealed class Settings
        {
            [Header("Shape")]
            [Tooltip("Distance from the middle of the triangle to each corner, measured flat.")]
            [Min(1f)] public float radius = 12f;

            [Tooltip("What counts as floor. Leave the Player and Enemy layers out.")]
            public LayerMask groundMask = 1;

            [Header("Swords")]
            [Tooltip("Art for each sword. Its scripts and colliders are stripped. Empty uses a plain long box.")]
            public GameObject swordPrefab;

            [Tooltip("Multiple of the art's own scale at full size.")]
            [Min(0.01f)] public float swordScale = 1f;

            [Tooltip("Turn, in degrees, for art not built tip down local -Y with its flat facing local +Z.")]
            public Vector3 swordRotation;

            [Tooltip("Colour of the plain box, when there is no art.")]
            public Color swordColour = new Color(0.85f, 0.85f, 0.95f);

            [Header("Split")]
            [Tooltip("Size each sword starts at as it leaves the owner, as a fraction of full size.")]
            [Range(0.01f, 1f)] public float startScale = 0.3f;

            [Tooltip("Seconds the flight out to the corners takes.")]
            [Min(0.01f)] public float flyTime = 0.8f;

            [Tooltip("How far the flight bends sideways at its widest, all three the same way round.")]
            public float curveSide = 6f;

            [Tooltip("How far the flight bows upward at its widest.")]
            public float curveLift = 3f;

            [Header("Stab")]
            [Tooltip("How far above its corner each sword's point hangs before it stabs.")]
            [Min(0f)] public float stabHeight = 2f;

            [Tooltip("Seconds the swords hang, point down, before stabbing.")]
            [Min(0f)] public float hangTime = 0.15f;

            [Tooltip("Seconds the stab takes. It speeds up all the way down.")]
            [Min(0.01f)] public float stabTime = 0.12f;

            [Tooltip("How far each point sinks into the floor.")]
            [Min(0f)] public float stabDepth = 0.6f;

            [Tooltip("Material for the stab bursts, e.g. NeoNoir/Flat in the colour you want.")]
            public Material stabMaterial;

            public HammerImpact.Settings stabImpact = new HammerImpact.Settings
            {
                ringRadius = 2.5f,
                ringWidth = 0.5f,
                ringTime = 0.35f,
                debrisCount = 6,
                debrisSize = 0.3f,
                debrisUpSpeed = new Vector2(4f, 8f),
                debrisOutSpeed = new Vector2(2f, 4f),
                debrisTime = 0.6f,
            };

            [Tooltip("How far each stab jolts the camera. 0 for none.")]
            [Min(0f)] public float stabShake = 0.3f;

            [Min(0.01f)] public float stabShakeDuration = 0.2f;

            [Header("Orbs")]
            [Tooltip("Height above the floor the orbs, and so the lasers, sit at. About chest height, or the lasers pass over the player.")]
            public float orbHeight = 1.6f;

            [Tooltip("Art for each orb. Its scripts and colliders are stripped. Empty draws a glowing ball.")]
            public GameObject orbPrefab;

            [Tooltip("Width of the glowing ball, or a multiple of the art's own scale.")]
            [Min(0.01f)] public float orbSize = 1f;

            [Tooltip("Colour of the glowing ball. Above 1 blooms. Keep it strongly saturated.")]
            [ColorUsage(false, true)] public Color orbColour = new Color(0.6f, 0.9f, 3f, 1f);

            [Tooltip("Seconds an orb takes to grow, and to shrink away at the end.")]
            [Min(0.01f)] public float orbGrowTime = 0.3f;

            [Header("Charge")]
            [Tooltip("Seconds from the orbs being grown to the lasers firing.")]
            [Min(0f)] public float chargeTime = 2f;

            [Tooltip("Width the warning lines between the orbs reach just before the lasers fire.")]
            [Min(0f)] public float warningWidth = 0.15f;

            [Tooltip("Colour of the warning lines. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color warningColour = new Color(0.6f, 0.9f, 3f, 1f);

            [Tooltip("How much bigger the orbs swell at the top of each pulse while charging, as a fraction of their size.")]
            [Min(0f)] public float chargePulse = 0.2f;

            [Tooltip("Pulses a second while charging.")]
            [Min(0f)] public float chargePulseRate = 4f;

            [Header("Lasers")]
            [Tooltip("The lasers between the orbs. Their Length is set to reach the next orb.")]
            public SweepBeam.Settings laser = new SweepBeam.Settings
            {
                radius = 0.5f,
                damagePerSecond = 10f,
                colour = new Color(0.6f, 0.9f, 3f, 1f),
                wallMask = 0,
            };

            [Header("Missiles, while the player is outside")]
            [Tooltip("Missiles each orb fires a second.")]
            [Min(0.1f)] public float missilesPerSecond = 3f;

            [Tooltip("Degrees each missile may stray either way from straight at the player as it leaves.")]
            [Range(0f, 90f)] public float missileSpread = 40f;

            [Tooltip("Degrees each missile is tilted upward as it leaves.")]
            [Range(0f, 80f)] public float missileLift = 30f;

            [Tooltip("Art for each missile. Empty draws a glowing ball.")]
            public GameObject missilePrefab;

            public HomingMissile.Settings missile = new HomingMissile.Settings
            {
                scatterSpeed = 7f,
                scatterTime = 0.2f,
                lockTurnRate = 600f,
                lockTime = 0.15f,
                trackTurnRate = 160f,
                speed = 10f,
                lifetime = 5f,
                damage = 5f,
                radius = 0.4f,
                size = 0.7f,
                colour = new Color(0.6f, 0.9f, 3f, 1f),
                trailWidth = 0.6f,
            };

            [Header("Debris outside")]
            [Tooltip("Chunks dropped a second, on spots outside the triangle.")]
            [Min(0f)] public float debrisPerSecond = 7f;

            [Tooltip("Chunks land within this far of the player, measured flat.")]
            [Min(0f)] public float debrisRange = 12f;

            [Tooltip("Chunks never land closer than this to the triangle's edge, measured flat, so there is room to stand just outside it.")]
            [Min(0f)] public float debrisClearance = 3f;

            [Tooltip("How far above the floor each chunk starts falling from.")]
            [Min(0f)] public float debrisDropHeight = 25f;

            public MaceDebris.Settings debris = new MaceDebris.Settings
            {
                size = 1f,
                flightTime = new Vector2(1f, 1.3f),
                arcHeight = 0f,
                damage = 10f,
                damageRadius = 1.3f,
                shakeStrength = 0.05f,
            };

            [Header("Dismiss")]
            [Tooltip("Seconds the swords take to pull out of the floor.")]
            [Min(0.01f)] public float pullOutTime = 0.25f;

            [Tooltip("Seconds the flight back into the owner takes.")]
            [Min(0.01f)] public float returnTime = 0.6f;
        }

        private readonly Transform[] swords = new Transform[Corners];
        private readonly Vector3[] swordScales = new Vector3[Corners];
        private readonly float[] tipReach = new float[Corners];
        private readonly Vector3[] corners = new Vector3[Corners];
        private readonly Vector3[] outwards = new Vector3[Corners];
        private readonly Transform[] orbs = new Transform[Corners];
        private readonly Vector3[] orbScales = new Vector3[Corners];
        private readonly LineRenderer[] lines = new LineRenderer[Corners];
        private readonly SweepBeam[] beams = new SweepBeam[Corners];
        private readonly float[] missileWait = new float[Corners];

        private Settings settings;
        private SweepBeam.Settings beamSettings;
        private Enemy owner;
        private Quaternion swordOffset;
        private float debrisWait;
        private bool ending;

        /// <summary>True once the lasers are up, until <see cref="Dismiss"/>.</summary>
        public bool LasersOn { get; private set; }

        /// <summary>
        /// Splits the swords out of <paramref name="owner"/> to the corners
        /// of a triangle round <paramref name="floorCentre"/>, a point on the
        /// floor, its first corner <paramref name="heading"/> degrees round
        /// from world forward (+Z).
        /// </summary>
        public static SwordTriangle Create(Enemy owner, Vector3 floorCentre, float heading, Settings settings)
        {
            GameObject host = new GameObject("SwordTriangle");
            host.transform.position = floorCentre;

            SwordTriangle triangle = host.AddComponent<SwordTriangle>();
            triangle.Begin(owner, floorCentre, heading, settings);
            return triangle;
        }

        /// <summary>Whether <paramref name="point"/> is inside the triangle, measured flat.</summary>
        public bool Contains(Vector3 point)
        {
            float a = Side(corners[0], corners[1], point);
            float b = Side(corners[1], corners[2], point);
            float c = Side(corners[2], corners[0], point);

            bool anyBelow = a < 0f || b < 0f || c < 0f;
            bool anyAbove = a > 0f || b > 0f || c > 0f;
            return !(anyBelow && anyAbove);
        }

        /// <summary>A spot inside the triangle, picked evenly over its area, on the floor between its corners.</summary>
        public Vector3 RandomPoint()
        {
            float u = UnityEngine.Random.value;
            float v = UnityEngine.Random.value;

            // Folded back in, the half of the square past the diagonal lands
            // in the triangle too, so nothing is wasted or bunched.
            if (u + v > 1f)
            {
                u = 1f - u;
                v = 1f - v;
            }

            return corners[0] + (corners[1] - corners[0]) * u + (corners[2] - corners[0]) * v;
        }

        /// <summary>
        /// Stops the lasers and sends the swords back into the owner. The
        /// triangle destroys itself once they are home.
        /// </summary>
        public void Dismiss()
        {
            if (ending)
            {
                return;
            }

            ending = true;
            StopAllCoroutines();
            StartCoroutine(OwnerGone() ? Collapse() : Return());
        }

        private void Begin(Enemy triangleOwner, Vector3 floorCentre, float heading, Settings triangleSettings)
        {
            owner = triangleOwner;
            settings = triangleSettings;
            swordOffset = Quaternion.Euler(settings.swordRotation);

            // A copy, so each laser's length can be set without touching the asset.
            beamSettings = JsonUtility.FromJson<SweepBeam.Settings>(JsonUtility.ToJson(settings.laser));
            beamSettings.length = Mathf.Max(0.5f, settings.radius * Mathf.Sqrt(3f));

            Vector3 origin = owner.Model.position;

            for (int i = 0; i < Corners; i++)
            {
                outwards[i] = Quaternion.AngleAxis(heading + 360f * i / Corners, Vector3.up) * Vector3.forward;
                corners[i] = FindFloor(floorCentre + outwards[i] * settings.radius);
                swords[i] = MakeSword(origin, outwards[i], out swordScales[i], out tipReach[i]);
            }

            StartCoroutine(Deploy());
        }

        private Transform MakeSword(Vector3 origin, Vector3 outward, out Vector3 fullScale, out float reach)
        {
            GameObject art;

            if (settings.swordPrefab != null)
            {
                art = Instantiate(ArtTemplate.Of(settings.swordPrefab));
                fullScale = settings.swordPrefab.transform.localScale * settings.swordScale;
            }
            else
            {
                art = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(art.GetComponent<Collider>());
                fullScale = new Vector3(0.4f, 4f, 0.12f) * settings.swordScale;

                MeshRenderer box = art.GetComponent<MeshRenderer>();
                box.sharedMaterial = FlatMaterial.Shared;
                FlatMaterial.Tint(box, settings.swordColour, new MaterialPropertyBlock());
            }

            art.name = "TriangleSword";
            Transform sword = art.transform;
            sword.SetParent(transform, true);

            // Measured upright at full size: how far the point reaches below the pivot.
            sword.SetPositionAndRotation(origin, Upright(outward));
            sword.localScale = fullScale;
            reach = TipBelowPivot(sword);

            sword.localScale = fullScale * settings.startScale;
            return sword;
        }

        private IEnumerator Deploy()
        {
            Vector3 origin = owner.Model.position;
            Quaternion startRotation = owner.Model.rotation * swordOffset;

            // Out to hang over the corners.
            float clock = 0f;

            while (clock < settings.flyTime)
            {
                clock += Time.deltaTime;
                float t = Smooth(clock / settings.flyTime);

                for (int i = 0; i < Corners; i++)
                {
                    swords[i].SetPositionAndRotation(Curve(i, origin, t),
                                                     Quaternion.Slerp(startRotation, Upright(outwards[i]), t));
                    swords[i].localScale = swordScales[i] * Mathf.Lerp(settings.startScale, 1f, t);
                }

                yield return null;
            }

            for (int i = 0; i < Corners; i++)
            {
                swords[i].SetPositionAndRotation(Hanging(i), Upright(outwards[i]));
                swords[i].localScale = swordScales[i];
            }

            if (settings.hangTime > 0f)
            {
                yield return new WaitForSeconds(settings.hangTime);
            }

            // Down, speeding up, into the floor.
            clock = 0f;

            while (clock < settings.stabTime)
            {
                clock += Time.deltaTime;
                float t = Mathf.Clamp01(clock / settings.stabTime);

                for (int i = 0; i < Corners; i++)
                {
                    swords[i].position = Vector3.Lerp(Hanging(i), Stabbed(i), t * t);
                }

                yield return null;
            }

            for (int i = 0; i < Corners; i++)
            {
                swords[i].position = Stabbed(i);
                HammerImpact.Create(corners[i], settings.stabImpact, settings.stabMaterial);
                orbs[i] = MakeOrb(i, out orbScales[i]);
            }

            if (settings.stabShake > 0f)
            {
                TopDownCamera.Shake(settings.stabShake, settings.stabShakeDuration);
            }

            // The orbs grow.
            clock = 0f;

            while (clock < settings.orbGrowTime)
            {
                clock += Time.deltaTime;
                float t = Smooth(clock / settings.orbGrowTime);

                for (int i = 0; i < Corners; i++)
                {
                    orbs[i].localScale = orbScales[i] * t;
                }

                yield return null;
            }

            // Charging: the warning lines widen as the orbs pulse.
            for (int i = 0; i < Corners; i++)
            {
                lines[i] = MakeLine();
                lines[i].SetPosition(0, OrbPoint(i));
                lines[i].SetPosition(1, OrbPoint((i + 1) % Corners));
            }

            clock = 0f;

            while (clock < settings.chargeTime)
            {
                clock += Time.deltaTime;
                float charge = Mathf.Clamp01(clock / settings.chargeTime);
                float pulse = 1f + settings.chargePulse * charge * (0.5f + 0.5f * Mathf.Sin(clock * settings.chargePulseRate * Mathf.PI * 2f));

                for (int i = 0; i < Corners; i++)
                {
                    orbs[i].localScale = orbScales[i] * pulse;
                    lines[i].widthMultiplier = settings.warningWidth * charge;
                }

                yield return null;
            }

            for (int i = 0; i < Corners; i++)
            {
                orbs[i].localScale = orbScales[i];
                Destroy(lines[i].gameObject);
                lines[i] = null;

                Vector3 from = OrbPoint(i);
                beams[i] = SweepBeam.Fire(from, OrbPoint((i + 1) % Corners) - from, owner.transform, beamSettings);

                // Staggered, so the three orbs never fire all at once.
                missileWait[i] = (float)i / Corners / settings.missilesPerSecond;
            }

            LasersOn = true;
        }

        private void Update()
        {
            if (!ending && OwnerGone())
            {
                Dismiss();
                return;
            }

            if (!LasersOn)
            {
                return;
            }

            for (int i = 0; i < Corners; i++)
            {
                if (beams[i] != null)
                {
                    Vector3 from = OrbPoint(i);
                    beams[i].Aim(from, OrbPoint((i + 1) % Corners) - from);
                }
            }

            if (!owner.HasTarget)
            {
                return;
            }

            Transform target = owner.Target;
            float deltaTime = Time.deltaTime;

            if (!Contains(target.position))
            {
                for (int i = 0; i < Corners; i++)
                {
                    missileWait[i] -= deltaTime;

                    if (missileWait[i] <= 0f)
                    {
                        missileWait[i] += 1f / settings.missilesPerSecond;
                        FireMissile(i, target);
                    }
                }
            }
            else
            {
                // Ready to fire the moment the player steps out.
                for (int i = 0; i < Corners; i++)
                {
                    missileWait[i] = Mathf.Max(0f, missileWait[i] - deltaTime);
                }
            }

            if (settings.debrisPerSecond > 0f)
            {
                debrisWait -= deltaTime;

                while (debrisWait <= 0f)
                {
                    debrisWait += 1f / settings.debrisPerSecond;
                    DropDebris(target);
                }
            }
        }

        private void FireMissile(int corner, Transform target)
        {
            Vector3 from = OrbPoint(corner);
            Vector3 toward = target.position - from;
            toward.y = 0f;
            float heading = toward.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toward).eulerAngles.y : 0f;
            heading += UnityEngine.Random.Range(-settings.missileSpread, settings.missileSpread);

            Vector3 direction = Quaternion.Euler(-settings.missileLift, heading, 0f) * Vector3.forward;
            GameObject art = settings.missilePrefab != null ? Instantiate(settings.missilePrefab) : null;

            HomingMissile.Launch(art, from, direction, target, owner.transform, settings.missile);
        }

        private void DropDebris(Transform target)
        {
            ArenaBounds arena = owner.Arena;

            for (int i = 0; i < DebrisTries; i++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * settings.debrisRange;
                Vector3 spot = target.position + new Vector3(offset.x, 0f, offset.y);

                if (Contains(spot) || DistanceOutside(spot) < settings.debrisClearance
                    || (arena != null && !arena.Contains(spot)))
                {
                    continue;
                }

                Vector3 floor = FindFloor(spot);
                Vector2 flight = settings.debris.flightTime;
                float seconds = UnityEngine.Random.Range(Mathf.Min(flight.x, flight.y), Mathf.Max(flight.x, flight.y));
                PlayerHealth player = target.GetComponentInParent<PlayerHealth>();

                MaceDebris.Throw(floor + Vector3.up * settings.debrisDropHeight, floor, seconds, settings.debris, player);
                return;
            }
        }

        private IEnumerator Return()
        {
            StopLasers();

            Vector3[] pulledFrom = new Vector3[Corners];

            for (int i = 0; i < Corners; i++)
            {
                pulledFrom[i] = swords[i].position;
            }

            // Orbs shrink as the swords pull out.
            float shrink = 0f;
            float clock = 0f;

            while (clock < settings.pullOutTime)
            {
                clock += Time.deltaTime;
                shrink += Time.deltaTime;
                float t = Smooth(clock / settings.pullOutTime);

                for (int i = 0; i < Corners; i++)
                {
                    swords[i].position = Vector3.Lerp(pulledFrom[i], Hanging(i), t);
                }

                ShrinkOrbs(shrink);
                yield return null;
            }

            // Back along the curve, into wherever the owner is now.
            clock = 0f;

            while (clock < settings.returnTime)
            {
                if (OwnerGone())
                {
                    StartCoroutine(Collapse());
                    yield break;
                }

                clock += Time.deltaTime;
                shrink += Time.deltaTime;
                float t = Smooth(clock / settings.returnTime);
                Vector3 home = owner.Model.position;
                Quaternion homeRotation = owner.Model.rotation * swordOffset;

                for (int i = 0; i < Corners; i++)
                {
                    swords[i].SetPositionAndRotation(Curve(i, home, 1f - t),
                                                     Quaternion.Slerp(Upright(outwards[i]), homeRotation, t));
                    swords[i].localScale = swordScales[i] * Mathf.Lerp(1f, settings.startScale, t);
                }

                ShrinkOrbs(shrink);
                yield return null;
            }

            Destroy(gameObject);
        }

        /// <summary>The owner is gone: everything shrinks away where it is.</summary>
        private IEnumerator Collapse()
        {
            StopLasers();

            Vector3[] from = new Vector3[Corners];

            for (int i = 0; i < Corners; i++)
            {
                from[i] = swords[i].localScale;
            }

            float clock = 0f;

            while (clock < settings.orbGrowTime)
            {
                clock += Time.deltaTime;
                float t = 1f - Smooth(clock / settings.orbGrowTime);

                for (int i = 0; i < Corners; i++)
                {
                    swords[i].localScale = from[i] * t;
                }

                ShrinkOrbs(clock);
                yield return null;
            }

            Destroy(gameObject);
        }

        private void StopLasers()
        {
            LasersOn = false;

            for (int i = 0; i < Corners; i++)
            {
                if (beams[i] != null)
                {
                    beams[i].Stop();
                    beams[i] = null;
                }

                if (lines[i] != null)
                {
                    Destroy(lines[i].gameObject);
                    lines[i] = null;
                }
            }
        }

        private void ShrinkOrbs(float elapsed)
        {
            float t = 1f - Smooth(elapsed / settings.orbGrowTime);

            for (int i = 0; i < Corners; i++)
            {
                if (orbs[i] != null)
                {
                    orbs[i].localScale = orbScales[i] * t;
                }
            }
        }

        private Transform MakeOrb(int corner, out Vector3 fullScale)
        {
            GameObject art;

            if (settings.orbPrefab != null)
            {
                art = Instantiate(ArtTemplate.Of(settings.orbPrefab));
                fullScale = settings.orbPrefab.transform.localScale * settings.orbSize;
            }
            else
            {
                art = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(art.GetComponent<Collider>());
                fullScale = Vector3.one * settings.orbSize;

                MeshRenderer ball = art.GetComponent<MeshRenderer>();
                ball.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ball.sharedMaterial = FlatMaterial.Shared;
                FlatMaterial.Tint(ball, settings.orbColour, new MaterialPropertyBlock());
            }

            art.name = "TriangleOrb";
            Transform orb = art.transform;
            orb.SetParent(transform, true);
            orb.position = OrbPoint(corner);
            orb.localScale = Vector3.zero;
            return orb;
        }

        private LineRenderer MakeLine()
        {
            GameObject host = new GameObject("TriangleWarning");
            host.transform.SetParent(transform, false);

            LineRenderer line = host.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = FlatMaterial.DoubleSided;
            line.widthMultiplier = 0f;

            if (line.sharedMaterial != null)
            {
                FlatMaterial.Tint(line, settings.warningColour, new MaterialPropertyBlock());
            }

            return line;
        }

        private bool OwnerGone()
        {
            return owner == null || owner.Health.IsDead || !owner.isActiveAndEnabled;
        }

        /// <summary>
        /// The way out from <paramref name="origin"/> to hang over corner
        /// <paramref name="corner"/>, at <paramref name="t"/> from 0 to 1.
        /// </summary>
        private Vector3 Curve(int corner, Vector3 origin, float t)
        {
            Vector3 end = Hanging(corner);
            Vector3 side = Vector3.Cross(Vector3.up, outwards[corner]);
            Vector3 bend = (origin + end) * 0.5f + side * settings.curveSide + Vector3.up * settings.curveLift;

            float u = 1f - t;
            return u * u * origin + 2f * u * t * bend + t * t * end;
        }

        private Vector3 Hanging(int corner)
        {
            return corners[corner] + Vector3.up * (tipReach[corner] + settings.stabHeight);
        }

        private Vector3 Stabbed(int corner)
        {
            return corners[corner] + Vector3.up * (tipReach[corner] - settings.stabDepth);
        }

        private Vector3 OrbPoint(int corner)
        {
            return corners[corner] + Vector3.up * settings.orbHeight;
        }

        private Quaternion Upright(Vector3 outward)
        {
            return Quaternion.LookRotation(outward, Vector3.up) * swordOffset;
        }

        /// <summary>The floor under <paramref name="point"/>, or the point itself if there is none.</summary>
        private Vector3 FindFloor(Vector3 point)
        {
            // From well above, so a raised floor still counts.
            if (Physics.Raycast(point + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f,
                                settings.groundMask, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }

            return point;
        }

        private static float TipBelowPivot(Transform sword)
        {
            float lowest = float.MaxValue;

            foreach (Renderer part in sword.GetComponentsInChildren<Renderer>())
            {
                lowest = Mathf.Min(lowest, part.bounds.min.y);
            }

            return lowest == float.MaxValue ? 0f : sword.position.y - lowest;
        }

        /// <summary>How far <paramref name="point"/> is from the nearest of the triangle's edges, measured flat.</summary>
        private float DistanceOutside(Vector3 point)
        {
            Vector2 p = new Vector2(point.x, point.z);
            float nearest = float.MaxValue;

            for (int i = 0; i < Corners; i++)
            {
                Vector2 a = new Vector2(corners[i].x, corners[i].z);
                Vector2 b = new Vector2(corners[(i + 1) % Corners].x, corners[(i + 1) % Corners].z);
                Vector2 edge = b - a;
                float along = Mathf.Clamp01(Vector2.Dot(p - a, edge) / Mathf.Max(edge.sqrMagnitude, 0.0001f));

                nearest = Mathf.Min(nearest, Vector2.Distance(p, a + edge * along));
            }

            return nearest;
        }

        /// <summary>Which side of the line from <paramref name="a"/> to <paramref name="b"/> <paramref name="point"/> is on, flat.</summary>
        private static float Side(Vector3 a, Vector3 b, Vector3 point)
        {
            return (b.x - a.x) * (point.z - a.z) - (b.z - a.z) * (point.x - a.x);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void OnDestroy()
        {
            // Lasers are their own objects: never left burning.
            for (int i = 0; i < Corners; i++)
            {
                if (beams[i] != null)
                {
                    beams[i].Stop();
                }
            }
        }
    }
}
