using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// Sage the Scepter's fight, all one attack: she leaps to the middle of
    /// the arena and plants herself there, then, over and over until she is
    /// beaten, fills the floor with patterns of lotuses that bloom and burst,
    /// hurting the player.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The drop: she appears <see cref="dropHeight"/> straight above the
    ///      enemy's <see cref="Enemy.ArenaCentre"/>, side on, and drops onto
    ///      it over <see cref="dropTime"/> (see <see cref="BladeDrop"/>), while a
    ///      <see cref="WarningCircle"/> of <see cref="landingRadius"/> fills
    ///      where she will land. She comes down foot first, upright, sunk
    ///      <see cref="landingSink"/> into the floor, with a heavy impact and
    ///      <see cref="landingDamage"/> to the player inside the circle. She
    ///      stays there for the rest of the fight. Without an arena centre
    ///      she drops where she is.
    ///   2. After <see cref="landingHold"/>, the patterns, in turn, each
    ///      followed by <see cref="patternGap"/> once its last lotus has
    ///      burst, then round again:
    ///      - Diamond: the even diamond rings round her (0, 2, 4...) planted
    ///        from the middle out, <see cref="ringStep"/> apart; then,
    ///        <see cref="oddRingDelay"/> after the last of them, the odd
    ///        rings the same way.
    ///      - Rows: rows running from Luna's end of the arena to the far
    ///        end, each planted whole, one after another, in two sweeps at
    ///        once: one from the left of the screen to the right, the other
    ///        from the right to the left, ending where they meet in the
    ///        middle. Each
    ///        sweep's first <see cref="fastRows"/> go <see cref="fastRowStep"/>
    ///        apart, the rest <see cref="rowInterval"/> apart.
    ///      - Spirals: three spirals, one after another, each out from her,
    ///        starting at a random angle, <see cref="spiralGap"/> between
    ///        windings, the same way as the curls, a lotus every
    ///        <see cref="spiralStep"/>, until it reaches the arena's furthest
    ///        corner. Each starts as the one before's last lotus bursts.
    ///      - Curls: eight arms of lotuses out from her, evenly round, each
    ///        bending <see cref="curl"/> degrees a unit out, all the same way,
    ///        planted outward along every arm at once, a lotus every
    ///        <see cref="curlStep"/>, to the arena's edge.
    ///
    /// Each lotus is a <see cref="SageLotus"/> planted with
    /// <see cref="SageLotus.PlantHostile"/>: it blooms over the settings'
    /// Bloom Time and bursts the moment it is fully open, with a
    /// <see cref="BlastSphere"/> of its Burst Radius, hurting the player
    /// within that radius, measured flat, for <see cref="lotusDamage"/>.
    /// Walking over one does nothing. Lotuses that burst together each hurt.
    ///
    /// The diamond and rows lie on a grid <see cref="spacing"/> apart along
    /// the world's axes, with a spot right under her, its rows running along
    /// whichever axis is nearest the line from the enemy's
    /// <see cref="Enemy.Summoner"/> (Luna) to the arena centre, over every
    /// spot of the enemy's <see cref="Enemy.Arena"/> with floor. Without an
    /// arena, the grid covers a square <see cref="fallbackHalfSize"/> out
    /// from her each way, and the curls and spirals keep within it.
    ///
    /// The lotuses take their material and model from the
    /// <see cref="SageScepter"/> on <see cref="sage"/>'s prefab, unless
    /// <see cref="lotusMaterial"/> is set.
    ///
    /// The attack never ends by itself, so give the enemy only this pattern.
    /// Her art is posed tip down local -Y, flat facing local +Z, as the
    /// player's blades are. Her Rigidbody, if she has one, is kinematic
    /// throughout.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Blade Attacks/Sage Lotus", fileName = "SageLotusAttack")]
    public sealed class SageLotusPattern : AttackPattern
    {
        private const int CurlArms = 8;
        private const int SpiralCount = 3;

        private struct Spot
        {
            public int I;
            public int J;
            public Vector3 Floor;
        }

        /// <summary>One lotus of a pattern, planted <see cref="Time"/> seconds after the pattern starts.</summary>
        private struct Planting
        {
            public float Time;
            public Vector3 Floor;
        }

        [Header("Drop to the middle")]
        [SerializeField]
        [Tooltip("How far above where she lands she appears, to drop straight down.")]
        [FormerlySerializedAs("leapHeight")]
        [Min(0f)]
        private float dropHeight = 8f;

        [SerializeField]
        [Tooltip("Seconds the drop takes. She gathers speed all the way down.")]
        [FormerlySerializedAs("leapTime")]
        [Min(0.01f)]
        private float dropTime = 0.9f;

        [SerializeField]
        [Tooltip("Degrees she is turned edge first from side on as she drops. Whole turns bring her down foot down.")]
        private float leapSpin = 360f;

        [SerializeField]
        [Tooltip("How far her foot sinks into the floor as she lands.")]
        [Min(0f)]
        private float landingSink = 0.3f;

        [SerializeField]
        [Tooltip("Radius of the warning circle where she lands, and of the landing's damage.")]
        [Min(0f)]
        private float landingRadius = 4f;

        [SerializeField]
        [Tooltip("Width of the warning circle's outline.")]
        [Min(0f)]
        private float landingOutline = 0.12f;

        [SerializeField]
        [Tooltip("Damage to the player inside the circle as she lands.")]
        [Min(0f)]
        private float landingDamage = 25f;

        [SerializeField]
        private Color warningColour = new Color(0.45f, 1f, 0.55f);

        [SerializeField]
        [Tooltip("Height above the floor the warning circle is drawn at, to clear a rug or carpet.")]
        [Range(0f, 1f)]
        private float warningHeight = 0.12f;

        [SerializeField]
        [Tooltip("What counts as floor. Leave the Player and Enemy layers out.")]
        private LayerMask groundMask = 1;

        [Header("Heavy impact")]
        [SerializeField]
        [Tooltip("Material for the landing burst, e.g. NeoNoir/Flat in the colour you want. Empty draws no burst.")]
        private Material landingMaterial;

        [SerializeField]
        private HammerImpact.Settings landing = new HammerImpact.Settings
        {
            ringRadius = 6f,
            ringWidth = 1f,
            ringTime = 0.45f,
            debrisCount = 14,
            debrisSize = 0.5f,
            debrisUpSpeed = new Vector2(6f, 10f),
            debrisOutSpeed = new Vector2(3f, 7f),
            debrisTime = 0.8f,
        };

        [SerializeField]
        [Tooltip("Radius of the explosion sphere the landing bursts into. 0 for none.")]
        [Min(0f)]
        private float blastRadius = 3.5f;

        [SerializeField]
        private BlastSphere.Settings blast = new BlastSphere.Settings
        {
            growTime = 0.12f,
            fadeTime = 0.35f,
            colour = new Color(0.9f, 3f, 1.1f, 1f),
            outlineWidth = 0.15f,
            outlineColour = new Color(2f, 2f, 2f, 1f),
        };

        [SerializeField]
        [Tooltip("How far the landing jolts the camera. 0 for none.")]
        [Min(0f)]
        private float landingShake = 0.8f;

        [SerializeField]
        [Min(0.01f)]
        private float landingShakeDuration = 0.4f;

        [SerializeField]
        [Tooltip("Seconds she stands after landing before the first pattern.")]
        [Min(0f)]
        private float landingHold = 1f;

        [Header("Lotuses")]
        [SerializeField]
        [Tooltip("Sage's blade. The lotuses take their material and model from the SageScepter on its prefab.")]
        private BladeDefinition sage;

        [SerializeField]
        [Tooltip("Material for the lotuses, rings and petals. Empty uses the Effect Material on Sage's scepter.")]
        private Material lotusMaterial;

        [SerializeField]
        [Tooltip("How each lotus blooms and bursts. Burst Radius is how far its explosion reaches. Damage, Burst On Bloom and the stepping and heal settings are ignored.")]
        private SageLotus.Settings lotus = new SageLotus.Settings
        {
            size = 1.2f,
            bloomTime = 1.5f,
            burstRadius = 2f,
            petals = 8,
        };

        [SerializeField]
        [Tooltip("Damage to the player within a lotus's Burst Radius as it bursts.")]
        [Min(0f)]
        private float lotusDamage = 10f;

        [SerializeField]
        [Tooltip("The explosion sphere each lotus bursts into, out to its Burst Radius.")]
        private BlastSphere.Settings lotusBlast = new BlastSphere.Settings
        {
            growTime = 0.1f,
            fadeTime = 0.3f,
            colour = new Color(0.9f, 3f, 1.1f, 1f),
            outlineWidth = 0.1f,
            outlineColour = new Color(2f, 2f, 2f, 1f),
        };

        [SerializeField]
        [Tooltip("Distance between neighbouring lotuses on the grid. At most Burst Radius x 1.41 for a covered quarter to have no gaps.")]
        [Min(0.1f)]
        private float spacing = 2.8f;

        [SerializeField]
        [Tooltip("How far out from her, each way, the grid and curls reach when the enemy has no arena.")]
        [Min(0.1f)]
        private float fallbackHalfSize = 12f;

        [Header("Patterns")]
        [SerializeField]
        [Tooltip("Diamond: seconds between one ring being planted and the next ring out.")]
        [Min(0f)]
        private float ringStep = 0.03f;

        [SerializeField]
        [Tooltip("Diamond: seconds from the last even ring being planted to the first odd ring.")]
        [Min(0f)]
        private float oddRingDelay = 1f;

        [SerializeField]
        [Tooltip("Rows: how many rows in from its starting edge each sweep plants quickly, Fast Row Step apart.")]
        [Min(0)]
        private int fastRows = 8;

        [SerializeField]
        [Tooltip("Rows: seconds between one row and the next over the first Fast Rows of each sweep.")]
        [Min(0f)]
        private float fastRowStep = 0.03f;

        [SerializeField]
        [Tooltip("Rows: seconds between one row and the next after the first Fast Rows, in the middle of the arena.")]
        [Min(0f)]
        private float rowInterval = 1f;

        [SerializeField]
        [Tooltip("Spirals: distance between one winding and the next.")]
        [Min(0.1f)]
        private float spiralGap = 4.5f;

        [SerializeField]
        [Tooltip("Spirals: distance between neighbouring lotuses along a spiral.")]
        [Min(0.1f)]
        private float spiralSpacing = 1.5f;

        [SerializeField]
        [Tooltip("Spirals: seconds between one lotus being planted along a spiral and the next.")]
        [Min(0f)]
        private float spiralStep = 0.04f;

        [SerializeField]
        [Tooltip("Curls: degrees each arm bends for every unit it reaches out from her. Positive bends clockwise, seen from above.")]
        private float curl = 6f;

        [SerializeField]
        [Tooltip("Curls: distance between neighbouring lotuses along an arm.")]
        [Min(0.1f)]
        private float curlSpacing = 1.5f;

        [SerializeField]
        [Tooltip("Curls: seconds between one lotus being planted along an arm and the next.")]
        [Min(0f)]
        private float curlStep = 0.05f;

        [SerializeField]
        [Tooltip("Seconds from a pattern's last burst to the next pattern being planted.")]
        [Min(0f)]
        private float patternGap = 1f;

        public override IEnumerator Perform(Enemy enemy)
        {
            enemy.VelocityOverride = Vector3.zero;

            // Posed by hand for the whole fight: the enemy's own turning
            // would right her while she stands planted.
            enemy.PhysicsDriven = true;

            Rigidbody body = enemy.GetComponent<Rigidbody>();

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            SageScepter scepter = sage != null && sage.Prefab != null ? sage.Prefab.GetComponent<SageScepter>() : null;
            Material material = lotusMaterial != null ? lotusMaterial : scepter != null ? scepter.EffectMaterial : null;
            GameObject model = scepter != null ? scepter.LotusModel : null;

            if (material == null)
            {
                Debug.LogWarning($"[SageLotusPattern] {name} has no lotus material: set Lotus Material, or the Sage blade's Effect Material.", this);
            }

            SageLotus.Prewarm(model);

            Vector3 centre = enemy.ArenaCentre ?? enemy.transform.position;

            foreach (object frame in Leap(enemy, centre))
            {
                yield return frame;
            }

            Vector3 floor = FindFloor(centre);
            Axes(enemy, floor, out Vector3 across, out Vector3 along);
            List<Spot> grid = Grid(enemy, floor, across, along);

            Action<Vector3> burst = point =>
            {
                BlastSphere.Create(point, lotus.burstRadius, lotusBlast);
                Hurt(enemy != null && enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null,
                     point, lotus.burstRadius, lotusDamage);
            };

            if (landingHold > 0f)
            {
                yield return new WaitForSeconds(landingHold);
            }

            // Until she dies, which stops this from outside.
            while (true)
            {
                for (int pattern = 0; pattern < 4; pattern++)
                {
                    List<Planting> plantings = pattern == 0 ? Diamond(grid)
                                             : pattern == 1 ? Rows(grid)
                                             : pattern == 2 ? Spirals(enemy, floor)
                                             : Curls(enemy, floor, along);

                    foreach (object frame in Plant(plantings, material, model, burst))
                    {
                        yield return frame;
                    }

                    // The last planted bursts as it opens.
                    yield return new WaitForSeconds(lotus.bloomTime + patternGap);
                }
            }
        }

        // ---- patterns -------------------------------------------------------

        private List<Planting> Diamond(List<Spot> grid)
        {
            int lastEven = 0;

            foreach (Spot spot in grid)
            {
                int ring = Mathf.Abs(spot.I) + Mathf.Abs(spot.J);

                if (ring % 2 == 0)
                {
                    lastEven = Mathf.Max(lastEven, ring);
                }
            }

            // Rings 0, 2, 4... go at steps 0, 1, 2...; rings 1, 3, 5... likewise, later.
            float oddStart = lastEven / 2 * ringStep + oddRingDelay;
            List<Planting> plantings = new List<Planting>();

            foreach (Spot spot in grid)
            {
                int ring = Mathf.Abs(spot.I) + Mathf.Abs(spot.J);
                float time = (ring % 2 == 0 ? 0f : oddStart) + ring / 2 * ringStep;
                plantings.Add(new Planting { Time = time, Floor = spot.Floor });
            }

            return plantings;
        }

        private List<Planting> Rows(List<Spot> grid)
        {
            int left = int.MaxValue;
            int right = int.MinValue;

            foreach (Spot spot in grid)
            {
                left = Mathf.Min(left, spot.I);
                right = Mathf.Max(right, spot.I);
            }

            List<Planting> plantings = new List<Planting>();

            foreach (Spot spot in grid)
            {
                // Two sweeps at once, from either side, ending where they
                // meet in the middle: each row is the nearer sweep's.
                float time = Mathf.Min(RowTime(spot.I - left), RowTime(right - spot.I));
                plantings.Add(new Planting { Time = time, Floor = spot.Floor });
            }

            return plantings;
        }

        /// <summary>
        /// When a sweep plants the row <paramref name="rowsIn"/> from its
        /// starting edge: the first <see cref="fastRows"/> quickly, the rest
        /// slower.
        /// </summary>
        private float RowTime(int rowsIn)
        {
            if (rowsIn < fastRows)
            {
                return rowsIn * fastRowStep;
            }

            return Mathf.Max(0, fastRows - 1) * fastRowStep + (rowsIn - Mathf.Max(0, fastRows - 1)) * rowInterval;
        }

        private List<Planting> Spirals(Enemy enemy, Vector3 origin)
        {
            // Distance out the spiral gains per radian it turns, and how far
            // round it goes to reach the furthest of the arena.
            float growth = spiralGap / (2f * Mathf.PI);
            float end = FurthestFloor(enemy, origin) / growth;
            float start = 0f;
            List<Planting> plantings = new List<Planting>();

            for (int spiral = 0; spiral < SpiralCount; spiral++)
            {
                float turn = UnityEngine.Random.Range(0f, 360f);
                float last = 0f;
                int step = 0;

                for (float angle = 0f; angle <= end; step++)
                {
                    // Wound the same way as the curls.
                    float reach = growth * angle;
                    float yaw = turn + Mathf.Sign(curl) * angle * Mathf.Rad2Deg;
                    Vector3 point = origin + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * reach;

                    // Off the arena's floor: skipped, but the spiral carries on round.
                    if (OnFloor(enemy, origin, ref point))
                    {
                        last = step * spiralStep;
                        plantings.Add(new Planting { Time = start + last, Floor = point });
                    }

                    // On by one spacing along the curve.
                    angle += spiralSpacing / Mathf.Sqrt(reach * reach + growth * growth);
                }

                // The next starts as this one's last lotus bursts.
                start += last + lotus.bloomTime;
            }

            return plantings;
        }

        /// <summary>How far from <paramref name="origin"/> the arena's furthest corner is, measured flat.</summary>
        private float FurthestFloor(Enemy enemy, Vector3 origin)
        {
            ArenaBounds arena = enemy.Arena;

            if (arena == null)
            {
                return fallbackHalfSize * Mathf.Sqrt(2f);
            }

            arena.Extent(out Vector3 right, out Vector3 forward, out Vector2 min, out Vector2 max);
            float furthest = 0f;

            for (int corner = 0; corner < 4; corner++)
            {
                Vector3 point = arena.Centre + right * ((corner & 1) == 0 ? min.x : max.x)
                                             + forward * ((corner & 2) == 0 ? min.y : max.y);
                furthest = Mathf.Max(furthest, Flat(point - origin).magnitude);
            }

            return furthest;
        }

        private List<Planting> Curls(Enemy enemy, Vector3 origin, Vector3 along)
        {
            // Radians of bend per unit out.
            float bend = curl * Mathf.Deg2Rad;
            List<Planting> plantings = new List<Planting>();

            for (int arm = 0; arm < CurlArms; arm++)
            {
                float start = 360f * arm / CurlArms;
                float reach = curlSpacing;

                for (int step = 0; ; step++)
                {
                    Vector3 point = origin + Quaternion.Euler(0f, start + curl * reach, 0f) * along * reach;

                    if (!OnFloor(enemy, origin, ref point))
                    {
                        break;
                    }

                    plantings.Add(new Planting { Time = step * curlStep, Floor = point });

                    // On by one spacing along the curve, not straight out:
                    // further out, the bend carries it further round.
                    reach += curlSpacing / Mathf.Sqrt(1f + reach * bend * reach * bend);
                }
            }

            return plantings;
        }

        /// <summary>Plants each lotus of <paramref name="plantings"/> at its time.</summary>
        private IEnumerable Plant(List<Planting> plantings, Material material, GameObject model, Action<Vector3> burst)
        {
            plantings.Sort((a, b) => a.Time.CompareTo(b.Time));
            float clock = 0f;
            int next = 0;

            while (true)
            {
                while (next < plantings.Count && plantings[next].Time <= clock)
                {
                    SageLotus.PlantHostile(plantings[next].Floor, lotus, material, model, burst);
                    next++;
                }

                if (next >= plantings.Count)
                {
                    yield break;
                }

                yield return null;
                clock += Time.deltaTime;
            }
        }

        // ---- grid -----------------------------------------------------------

        /// <summary>
        /// The world axes the grid is laid along: <paramref name="along"/>,
        /// the one nearest the line from Luna (the enemy's
        /// <see cref="Enemy.Summoner"/>) to <paramref name="centre"/>, pointing
        /// away from her, and <paramref name="across"/>, square to it,
        /// pointing to the right of the screen.
        /// </summary>
        private static void Axes(Enemy enemy, Vector3 centre, out Vector3 across, out Vector3 along)
        {
            Vector3 fromLuna = enemy.Summoner != null ? Flat(centre - enemy.Summoner.position) : Vector3.forward;

            along = Mathf.Abs(fromLuna.x) > Mathf.Abs(fromLuna.z)
                ? new Vector3(Mathf.Sign(fromLuna.x), 0f, 0f)
                : new Vector3(0f, 0f, fromLuna.z < 0f ? -1f : 1f);

            across = Vector3.Cross(Vector3.up, along);

            Camera view = Camera.main;

            if (view != null && Vector3.Dot(across, view.transform.right) < 0f)
            {
                across = -across;
            }
        }

        /// <summary>
        /// Every grid spot round <paramref name="origin"/> with arena floor
        /// under it, <see cref="spacing"/> apart along <paramref name="across"/>
        /// (I) and <paramref name="along"/> (J). Each spot's indices count
        /// steps out from the origin, negative on the left and on Luna's side.
        /// </summary>
        private List<Spot> Grid(Enemy enemy, Vector3 origin, Vector3 across, Vector3 along)
        {
            float halfSize = fallbackHalfSize;
            ArenaBounds arena = enemy.Arena;

            if (arena != null)
            {
                // Generous: the arena's own axes may be turned from the world's.
                arena.Extent(out _, out _, out Vector2 min, out Vector2 max);
                float reach = Mathf.Max(Mathf.Max(-min.x, max.x), Mathf.Max(-min.y, max.y));
                halfSize = reach * 1.42f + Flat(origin - arena.Centre).magnitude;
            }

            int steps = Mathf.CeilToInt(halfSize / spacing) + 1;
            List<Spot> spots = new List<Spot>();

            for (int i = -steps; i <= steps; i++)
            {
                for (int j = -steps; j <= steps; j++)
                {
                    Vector3 point = origin + across * (i * spacing) + along * (j * spacing);

                    if (OnFloor(enemy, origin, ref point))
                    {
                        spots.Add(new Spot { I = i, J = j, Floor = point });
                    }
                }
            }

            return spots;
        }

        /// <summary>
        /// Whether <paramref name="point"/> is on the arena's floor, moving it
        /// down onto it if so. Without an arena, anything within
        /// <see cref="fallbackHalfSize"/> of <paramref name="origin"/> each way counts.
        /// </summary>
        private bool OnFloor(Enemy enemy, Vector3 origin, ref Vector3 point)
        {
            ArenaBounds arena = enemy.Arena;

            if (arena != null)
            {
                if (!arena.Contains(point) || !arena.FloorAt(point, enemy.transform, out float height))
                {
                    return false;
                }

                point.y = height;
                return true;
            }

            Vector3 offset = point - origin;

            if (Mathf.Abs(offset.x) > fallbackHalfSize || Mathf.Abs(offset.z) > fallbackHalfSize)
            {
                return false;
            }

            point = FindFloor(point);
            return true;
        }

        // ---- drop -----------------------------------------------------------

        /// <summary>
        /// A drop straight down onto <paramref name="centre"/>, to land foot
        /// first in the floor there with a heavy impact, where she stays.
        /// </summary>
        private IEnumerable Leap(Enemy enemy, Vector3 centre)
        {
            Transform root = enemy.transform;
            Transform model = enemy.Model;

            List<Collider> switchedOff = new List<Collider>();

            foreach (Collider part in enemy.GetComponentsInChildren<Collider>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    switchedOff.Add(part);
                }
            }

            // Measured with her colliders off, so the ray never finds her.
            Vector3 floor = FindFloor(centre);
            Vector3 start = root.position;

            Vector3 heading = Flat(floor - start);
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Flat(model.forward);
            heading = heading.sqrMagnitude > 0.0001f ? heading.normalized : Vector3.forward;
            Vector3 tumbleAxis = Vector3.Cross(Vector3.up, heading);

            // Side on, her flat square to the way she came, posed for the
            // landing for the whole drop. Whole turns end foot down.
            Quaternion sideOn = Quaternion.LookRotation(tumbleAxis, Vector3.up);
            Quaternion landed = Quaternion.AngleAxis(leapSpin, tumbleAxis) * sideOn;
            Vector3 planted = AmberTrianglePattern.Buried(root, model, floor, landed, floor.y - landingSink);

            WarningCircle warning = WarningCircle.Create(floor + Vector3.up * warningHeight, landingRadius,
                                                         landingOutline, null, warningColour);
            enemy.FacingOverride = heading;
            model.rotation = landed;

            foreach (object frame in BladeDrop.Fall(root, planted, dropHeight, dropTime, warning))
            {
                yield return frame;
            }

            Destroy(warning.gameObject);

            HeavyImpact(floor);
            Hurt(enemy.HasTarget ? enemy.Target.GetComponentInParent<PlayerHealth>() : null, floor, landingRadius, landingDamage);

            foreach (Collider part in switchedOff)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }

            enemy.FacingOverride = null;
        }

        /// <summary>The heavy landing: dust and debris, an explosion sphere, and a hard jolt.</summary>
        private void HeavyImpact(Vector3 floor)
        {
            if (landingMaterial != null)
            {
                HammerImpact.Create(floor, landing, landingMaterial);
            }

            if (blastRadius > 0f)
            {
                BlastSphere.Create(floor, blastRadius, blast);
            }

            if (landingShake > 0f)
            {
                TopDownCamera.Shake(landingShake, landingShakeDuration);
            }
        }

        /// <summary>Hurts <paramref name="player"/> if within <paramref name="radius"/> of <paramref name="centre"/>, measured flat.</summary>
        private static void Hurt(PlayerHealth player, Vector3 centre, float radius, float damage)
        {
            if (player == null || player.IsDead || damage <= 0f)
            {
                return;
            }

            Vector3 offset = Flat(player.transform.position - centre);

            if (offset.sqrMagnitude <= radius * radius)
            {
                player.TakeDamage(damage, centre);
            }
        }

        /// <summary>The floor under <paramref name="point"/>, or the point itself if there is none.</summary>
        private Vector3 FindFloor(Vector3 point)
        {
            // From well above, so a raised floor still counts.
            if (Physics.Raycast(point + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f,
                                groundMask, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }

            return point;
        }

        private static Vector3 Flat(Vector3 vector)
        {
            vector.y = 0f;
            return vector;
        }

        private void Reset()
        {
            delayAfter = 0f;
            Movement.type = MovementType.StandStill;
            groundMask = LayerMask.GetMask("Default");
        }
    }
}
