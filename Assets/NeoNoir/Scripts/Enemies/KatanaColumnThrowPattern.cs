using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The Katana chops a passing column into glowing chunks, which float
    /// behind the boss and are flung at the player one at a time. The player
    /// can cut each one apart or dodge it.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. The nearest <see cref="CuttableColumn"/> ahead is picked: during a
    ///      run, such as <see cref="SpearHallIntro"/>'s, the nearest one
    ///      between <see cref="lookAhead"/>.x and .y further down the run
    ///      than the boss, no more than <see cref="sideReach"/> off to the
    ///      side, and the boss runs on until it is on screen, inside
    ///      <see cref="viewMargin"/>; otherwise simply the nearest within
    ///      <see cref="lookAhead"/>.y. With none, the attack is skipped.
    ///   2. The Katana is borrowed from the boss's <see cref="BossBlades"/>
    ///      -- the one spawned from <see cref="bladePrefab"/> -- and flies,
    ///      over <see cref="flyTime"/>, to the column's foot. From the moment
    ///      it leaves the orbit until the last cut it glistens
    ///      <see cref="glistenColour"/>, as the greatsword does: a glow
    ///      building in brightness, gleams twinkling along it ever quicker,
    ///      and a last big gleam at the last cut. While it is
    ///      out, a <see cref="KatanaGuard"/> on the boss stands aside, and
    ///      blows on the boss simply miss.
    ///   3. It sweeps back and forth across the column, working up it, each
    ///      sweep taking <see cref="slashTime"/>: the first cuts it off its
    ///      root, leaving the stump, and each after chops off a chunk, until
    ///      it is in <see cref="pieceCount"/> chunks. Each cut is real, with a
    ///      <see cref="SlashEffect"/> streak and a glowing
    ///      <see cref="CutOutline"/> traced round the column along it, and
    ///      <see cref="cutSpeed"/> speeds the Katana up. Each chunk, as it comes free,
    ///      trails <see cref="trailColour"/> and shrinks to
    ///      <see cref="chunkScale"/> of its size over
    ///      <see cref="shrinkTime"/>. The Katana then goes back into the
    ///      orbit.
    ///   4. The chunks rise, one just after another, to float in a row
    ///      behind the boss, tumbling slowly and keeping up with it, for
    ///      <see cref="hoverTime"/>.
    ///   5. One at a time, <see cref="throwInterval"/> apart, each is flung
    ///      straight at the player at <see cref="throwSpeed"/> on top of the
    ///      run's own speed, so it comes at them straight as they see it.
    /// Each chunk is a <see cref="ThrownColumn"/> with the health in
    /// <see cref="pieces"/>: it can be cut apart from the moment it comes
    /// free, and hurts the player only if it reaches them.
    ///
    /// The Katana is posed the way <see cref="BossBlades"/> poses it: built
    /// flat side facing local +Z, tip down local -Y, with the entry's
    /// Rotation Offset for one built otherwise.
    ///
    /// Leave the Attack Animation and Range Limit above empty.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Katana Column Throw", fileName = "KatanaColumnThrow")]
    public class KatanaColumnThrowPattern : AttackPattern
    {
        [SerializeField]
        [Tooltip("The Katana's prefab, as listed in the boss's Boss Blades. The blade spawned from it does the cutting.")]
        private GameObject bladePrefab;

        [Header("Picking a column")]
        [SerializeField]
        [Tooltip("Nearest and furthest a column can be down the run from the boss to be picked. Outside a run, only the furthest counts, in any direction.")]
        protected Vector2 lookAhead = new Vector2(2f, 100f);

        [SerializeField]
        [Tooltip("Furthest a column can be off to the side of the boss's line down the run.")]
        [Min(0f)]
        private float sideReach = 30f;

        [SerializeField]
        [Tooltip("The Katana waits until the picked column is on screen, at least this far in from every edge as a share of the screen, before it flies out to cut it.")]
        [Range(0f, 0.45f)]
        private float viewMargin = 0.1f;

        [SerializeField]
        [Tooltip("Most seconds the boss waits for the picked column to come on screen before giving up on it.")]
        [Min(0f)]
        private float maxWait = 6f;

        [Header("Katana")]
        [SerializeField]
        [Tooltip("Seconds the Katana takes to fly from the orbit to the column.")]
        [Min(0.01f)]
        private float flyTime = 0.22f;

        [SerializeField]
        [Tooltip("Seconds each sweep through the column takes.")]
        [Min(0.01f)]
        private float slashTime = 0.08f;

        [SerializeField]
        [Tooltip("Seconds the Katana takes to rise from the end of one sweep to the start of the next.")]
        [Min(0f)]
        private float stepTime = 0.04f;

        [SerializeField]
        [Tooltip("Speeds up the Katana's flight out and every sweep and step together. 2 is twice as fast as the times above.")]
        [Min(0.1f)]
        private float cutSpeed = 2f;

        [SerializeField]
        [Tooltip("How far each sweep carries the Katana, centred on the column.")]
        [Min(0.1f)]
        private float slashLength = 4f;

        [SerializeField]
        [Tooltip("Colour of the slash streak across the screen for each cut. Black uses the usual two-tone inversion.")]
        [ColorUsage(false, true)]
        private Color streakColour = new Color(0.2f, 1.6f, 2.2f, 1f);

        [SerializeField]
        [Tooltip("Size of each slash streak, as a multiple of its usual size.")]
        [Min(0f)]
        private float slashScale = 1f;

        [SerializeField]
        [Tooltip("How far each cut jolts the camera. 0 for none.")]
        [Min(0f)]
        private float cutShake = 0.25f;

        [SerializeField] [Min(0.01f)] private float cutShakeDuration = 0.15f;

        [Header("Cut outline")]
        [SerializeField]
        [Tooltip("Colour of the line traced around the column along each cut. Keep it strongly saturated; above 1 it blooms.")]
        [ColorUsage(false, true)]
        private Color outlineColour = new Color(0.2f, 2f, 2.6f, 1f);

        [SerializeField]
        [Tooltip("Thickness of the cut line. 0 draws none.")]
        [Min(0f)]
        private float outlineWidth = 0.14f;

        [SerializeField]
        [Tooltip("Seconds the cut line shows at full thickness.")]
        [Min(0f)]
        private float outlineHold = 0.35f;

        [SerializeField]
        [Tooltip("Seconds the cut line takes to thin away after that.")]
        [Min(0.01f)]
        private float outlineFade = 0.25f;

        [Header("Glisten")]
        [SerializeField]
        [Tooltip("Colour of the Katana's glow and gleams while it is out cutting.")]
        private Color glistenColour = new Color(0.1f, 0.8f, 1f);

        [SerializeField]
        [Tooltip("Brightness of the Katana's glow. Above 1 blooms.")]
        [Min(0f)]
        private float glowIntensity = 2.5f;

        [SerializeField]
        [Tooltip("What the glow's brightness is multiplied by at the last cut, building up from 1 as the Katana flies out.")]
        [Min(0f)]
        private float glowPeak = 2f;

        [SerializeField]
        [Tooltip("Seconds the glow takes to come on, and to go once the Katana heads back.")]
        [Min(0f)]
        private float glowFadeTime = 0.15f;

        [SerializeField] private BladeGleam.Settings gleam = new BladeGleam.Settings { size = 1f, time = 0.35f };

        [SerializeField]
        [Tooltip("Where on the Katana the gleams centre, in its own space. The tip is down -Y as built.")]
        private Vector3 gleamOffset;

        [SerializeField]
        [Tooltip("Gleams twinkle up to this far either way along the blade from the Gleam Offset.")]
        [Min(0f)]
        private float glistenSpread = 0.6f;

        [SerializeField]
        [Tooltip("Seconds between gleams as the Katana flies out, and at the last cut.")]
        private Vector2 glistenInterval = new Vector2(0.2f, 0.06f);

        [SerializeField]
        [Tooltip("Size of each gleam, as a multiple of the gleam's own, as the Katana flies out and at the last cut.")]
        private Vector2 glistenSize = new Vector2(0.5f, 1f);

        [SerializeField]
        [Tooltip("Size of the gleam flashed at the very last cut.")]
        [Min(0f)]
        private float finalGleamSize = 1.8f;

        [Header("Cut")]
        [SerializeField]
        [Tooltip("The Katana's cut: the cut faces it leaves, and how a chunk falls to pieces when the player destroys it.")]
        private KatanaCut.Settings cut = new KatanaCut.Settings { slashCount = 2, maxPieces = 6 };

        [Header("Chunks")]
        [SerializeField]
        [Tooltip("Fewest and most chunks the column is chopped into, picked at random each time.")]
        protected Vector2Int pieceCount = new Vector2Int(4, 5);

        [SerializeField]
        [Tooltip("Seconds after the first cut before the stump left standing starts to dissolve.")]
        [Min(0f)]
        private float stumpFadeDelay = 0.5f;

        [SerializeField]
        [Tooltip("Seconds the stump takes to dissolve away.")]
        [Min(0.01f)]
        private float stumpFadeTime = 0.8f;

        [SerializeField]
        [Tooltip("Up to this many degrees each chop leans from level, picked at random.")]
        [Range(0f, 45f)]
        private float chopTilt = 20f;

        [SerializeField]
        [Tooltip("How far each chop can stray up or down from an even split, as a share of a chunk's height.")]
        [Range(0f, 0.45f)]
        private float chopJitter = 0.25f;

        [SerializeField]
        [Tooltip("Size each chunk shrinks to once cut, as a share of its size as cut.")]
        [Range(0.01f, 1f)]
        private float chunkScale = 0.1f;

        [SerializeField]
        [Tooltip("Seconds a chunk takes to shrink to Chunk Scale after it is cut.")]
        [Min(0f)]
        private float shrinkTime = 0.5f;

        [SerializeField]
        [Tooltip("Colour of the trail each chunk leaves. Keep it strongly saturated; above 1 it blooms.")]
        [ColorUsage(false, true)]
        private Color trailColour = new Color(0.1f, 1.6f, 2.2f, 1f);

        [SerializeField]
        [Tooltip("Width of the trail at the chunk. It tapers to nothing behind.")]
        [Min(0f)]
        private float trailWidth = 0.15f;

        [SerializeField]
        [Tooltip("Seconds the trail lasts behind the chunk; longer is a longer trail.")]
        [Min(0f)]
        private float trailTime = 0.2f;

        [SerializeField]
        private ThrownColumn.Settings pieces = new ThrownColumn.Settings
        {
            health = 10f,
            contactDamage = 10f,
            contactPadding = 0.1f,
            maxFlightTime = 3f,
            roll = 540f,
        };

        [Header("Hover")]
        [SerializeField]
        [Tooltip("Seconds each chunk takes to rise from the column to its spot behind the boss.")]
        [Min(0.01f)]
        private float liftTime = 0.45f;

        [SerializeField]
        [Tooltip("Seconds between one chunk setting off and the next.")]
        [Min(0f)]
        private float liftStagger = 0.06f;

        [SerializeField]
        [Tooltip("Degrees the fan of chunks spreads across, centred straight behind the boss. 180 reaches out level with it on both sides.")]
        [Range(0f, 300f)]
        private float fanAngle = 200f;

        [SerializeField]
        [Tooltip("How far from the boss the chunks float.")]
        [Min(0f)]
        private float fanRadius = 6f;

        [SerializeField]
        [Tooltip("How high above the boss's pivot the chunks float, on average.")]
        private float hoverHeight = 3.5f;

        [SerializeField]
        [Tooltip("How far above and below that height neighbouring chunks are staggered.")]
        [Min(0f)]
        private float heightSpread = 1.2f;

        [SerializeField]
        [Tooltip("Up to this much each chunk's spot is nudged at random, so the fan is not too neat.")]
        [Min(0f)]
        private float fanJitter = 0.6f;

        [SerializeField]
        [Tooltip("How far the chunks bob while floating.")]
        [Min(0f)]
        private float hoverBob = 0.15f;

        [SerializeField]
        [Tooltip("Degrees per second the floating chunks tumble.")]
        [Min(0f)]
        private float hoverTumble = 90f;

        [SerializeField]
        [Tooltip("Seconds the whole row floats, once risen, before the first chunk is thrown.")]
        [Min(0f)]
        private float hoverTime = 0.5f;

        [Header("Throw")]
        [SerializeField]
        [Tooltip("Speed of each throw toward the player, on top of the run's own.")]
        [Min(0f)]
        private float throwSpeed = 22f;

        [SerializeField]
        [Tooltip("Seconds between one chunk being thrown and the next.")]
        [Min(0f)]
        private float throwInterval = 0.4f;

        /// <summary>A chunk on its way from the column to being thrown.</summary>
        private sealed class Chunk
        {
            public ThrownColumn column;
            public Vector3 cutFrom;
            public Quaternion cutRotation;
            public Vector3 tumbleAxis;
            public Vector3 nudge;
            public float liftStart;
        }

        public override bool CanPerform(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            return blades != null && blades.IndexOf(bladePrefab) >= 0 && FindColumn(enemy) != null;
        }

        public override IEnumerator Perform(Enemy enemy)
        {
            BossBlades blades = enemy.GetComponent<BossBlades>();
            int index = blades != null ? blades.IndexOf(bladePrefab) : -1;
            CuttableColumn target = FindColumn(enemy);

            if (index < 0 || target == null)
            {
                yield break;
            }

            IEnumerator getReady = GetInPlace(enemy, target);

            while (getReady.MoveNext())
            {
                yield return getReady.Current;
            }

            if (target == null || target.Taken || enemy.Health.IsDead || !InPlace(enemy, target))
            {
                yield break;
            }

            // A KatanaGuard sees the katana is out and stands aside on its own.
            IEnumerator attack = CutAndThrow(enemy, blades, index, target);

            while (attack.MoveNext())
            {
                yield return attack.Current;
            }
        }

        /// <summary>
        /// Until the boss is ready to cut <paramref name="target"/>: here,
        /// waits for it to come on screen, so the cutting is seen. Gives up
        /// after Max Wait, or once the column is gone or the boss dies.
        /// </summary>
        protected virtual IEnumerator GetInPlace(Enemy enemy, CuttableColumn target)
        {
            float waited = 0f;

            while (!OnScreen(target))
            {
                if (waited >= maxWait || target == null || target.Taken || enemy.Health.IsDead)
                {
                    yield break;
                }

                yield return null;
                waited += Time.deltaTime;
            }
        }

        /// <summary>Whether, after <see cref="GetInPlace"/>, the cutting goes ahead.</summary>
        protected virtual bool InPlace(Enemy enemy, CuttableColumn target) => OnScreen(target);

        private IEnumerator CutAndThrow(Enemy enemy, BossBlades blades, int index, CuttableColumn target)
        {
            Transform katana = blades.Borrow(index);
            Quaternion modelOffset = blades.RotationOffset(index);
            List<Chunk> chunks = new List<Chunk>();
            int chunkCount = Mathf.Max(1, Random.Range(Mathf.Min(pieceCount.x, pieceCount.y), Mathf.Max(pieceCount.x, pieceCount.y) + 1));

            float speed = Mathf.Max(0.1f, cutSpeed);
            float fly = flyTime / speed;
            float slash = slashTime / speed;
            float step = stepTime / speed;

            // The last cut lands partway through the last sweep, which speeds up.
            float lastCut = fly + (chunkCount - 1) * (step + slash) + slash * Mathf.Sqrt(0.5f);
            Glisten glisten = new Glisten(this, katana, lastCut);

            Vector3 across = Vector3.Cross(Vector3.up, RunForward(enemy)).normalized * (Random.value < 0.5f ? 1f : -1f);
            Vector3 root = target.CutPoint;

            // 1. Out of the orbit to the column's foot.
            Vector3 fromPosition = katana.position;
            Quaternion fromRotation = katana.rotation;
            Vector3 firstStart = root - across * (slashLength * 0.5f);
            Quaternion firstPose = SweepPose(across, modelOffset);
            float elapsed = 0f;

            while (elapsed < fly)
            {
                float eased = Smooth(elapsed / fly);
                katana.SetPositionAndRotation(Vector3.Lerp(fromPosition, firstStart, eased),
                                              Quaternion.Slerp(fromRotation, firstPose, eased));

                yield return null;
                elapsed += Time.deltaTime;
                glisten.Tick(Time.deltaTime);

                if (enemy.Health.IsDead)
                {
                    glisten.End(blades, index);
                    yield break;
                }
            }

            // 2. Back and forth up the column: off its root, then chunk by chunk.
            List<Material> materials = new List<Material>();
            MeshSlicer.Piece remaining = null;
            GameObject remainingShown = null;
            float[] heights = null;

            for (int sweep = 0; sweep < chunkCount; sweep++)
            {
                Vector3 direction = sweep % 2 == 0 ? across : -across;
                Vector3 centre = sweep == 0 ? root : new Vector3(root.x, heights[sweep - 1], root.z);
                Vector3 start = centre - direction * (slashLength * 0.5f);
                Vector3 end = centre + direction * (slashLength * 0.5f);
                Quaternion pose = SweepPose(direction, modelOffset);

                // Up from where the last sweep ended to where this one starts.
                if (sweep > 0)
                {
                    Vector3 risingFrom = katana.position;
                    Quaternion turningFrom = katana.rotation;
                    elapsed = 0f;

                    while (elapsed < step)
                    {
                        float eased = Smooth(elapsed / step);
                        katana.SetPositionAndRotation(Vector3.Lerp(risingFrom, start, eased),
                                                      Quaternion.Slerp(turningFrom, pose, eased));
                        HoldAll(chunks);

                        yield return null;
                        elapsed += Time.deltaTime;
                        glisten.Tick(Time.deltaTime);
                    }
                }

                bool cutDone = false;
                elapsed = 0f;

                while (true)
                {
                    float t = Mathf.Clamp01(elapsed / slash);
                    katana.SetPositionAndRotation(Vector3.Lerp(start, end, t * t), pose);

                    if (!cutDone && t * t >= 0.5f)
                    {
                        cutDone = true;
                        List<Vector3> outline = new List<Vector3>();

                        if (sweep == 0)
                        {
                            remaining = target.CutOffGeometry(direction, cut, materials, outline);

                            if (remaining == null)
                            {
                                glisten.End(blades, index);
                                yield break;
                            }

                            heights = ChopHeights(remaining.GetBounds(), chunkCount);

                            // What is left standing is no use: it dissolves away.
                            if (target.Stump != null)
                            {
                                target.Stump.AddComponent<RagdollFade>().Begin(stumpFadeDelay, stumpFadeTime);
                            }
                        }
                        else
                        {
                            remaining = Chop(enemy, remaining, materials, centre, direction, chunks, outline);
                        }

                        Flash(centre, direction, outline);

                        if (remainingShown != null)
                        {
                            Destroy(remainingShown);
                            remainingShown = null;
                        }

                        // The last sweep leaves the top as a chunk of its own.
                        if (sweep == chunkCount - 1)
                        {
                            AddChunk(enemy, remaining, materials, chunks);
                            remaining = null;
                        }
                        else
                        {
                            remainingShown = KatanaCut.BuildPiece(remaining, materials, cut, "Column (being cut)");
                        }
                    }

                    HoldAll(chunks);

                    if (t >= 1f)
                    {
                        break;
                    }

                    yield return null;
                    elapsed += Time.deltaTime;
                    glisten.Tick(Time.deltaTime);
                }
            }

            if (remainingShown != null)
            {
                Destroy(remainingShown);
            }

            glisten.End(blades, index);

            // 3. Up, one after another, into a row behind the boss.
            float clock = 0f;

            for (int i = 0; i < chunks.Count; i++)
            {
                chunks[i].liftStart = i * liftStagger;
            }

            float risen = (chunks.Count - 1) * liftStagger + liftTime;

            while (clock < risen + hoverTime)
            {
                if (Abandoned(enemy, chunks))
                {
                    yield break;
                }

                Hover(enemy, chunks, clock);

                yield return null;
                clock += Time.deltaTime;
            }

            // 4. One at a time, at the player.
            List<Chunk> order = new List<Chunk>(chunks);
            Shuffle(order);

            foreach (Chunk next in order)
            {
                if (next.column != null && !next.column.Broken)
                {
                    Vector3 toPlayer = enemy.HasTarget
                        ? enemy.TargetPoint - next.column.transform.position
                        : -RunForward(enemy);

                    next.column.Throw(ChaseFrame.Velocity + toPlayer.normalized * throwSpeed);
                }

                float wait = 0f;

                while (wait < throwInterval)
                {
                    if (Abandoned(enemy, chunks))
                    {
                        yield break;
                    }

                    Hover(enemy, chunks, clock);

                    yield return null;
                    wait += Time.deltaTime;
                    clock += Time.deltaTime;
                }
            }
        }

        /// <summary>
        /// The Katana's glisten while it is out: a cyan glow building in
        /// brightness, gleams twinkling along the blade ever quicker and
        /// bigger, and one last, biggest gleam at the last cut.
        /// </summary>
        private sealed class Glisten
        {
            private readonly KatanaColumnThrowPattern pattern;
            private readonly Transform katana;
            private readonly HammerGlow glow;
            private readonly float strike;
            private float clock;
            private float nextGleam;
            private bool finalDone;

            public Glisten(KatanaColumnThrowPattern pattern, Transform katana, float strike)
            {
                this.pattern = pattern;
                this.katana = katana;
                this.strike = Mathf.Max(0.01f, strike);

                Color glowColour = pattern.glistenColour * pattern.glowIntensity;
                glowColour.a = pattern.glistenColour.a;
                glow = HammerGlow.On(katana.gameObject, null, glowColour, pattern.glowFadeTime);

                BladeGleam.Flash(katana, pattern.gleamOffset, pattern.glistenColour, pattern.gleam, pattern.glistenSize.x);
                nextGleam = Mathf.Max(0.01f, pattern.glistenInterval.x);
            }

            public void Tick(float deltaTime)
            {
                clock += deltaTime;
                float charge = Mathf.Clamp01(clock / strike);

                if (glow != null)
                {
                    glow.GlowIntensity = Mathf.Lerp(1f, pattern.glowPeak, charge * charge);
                }

                if (finalDone)
                {
                    return;
                }

                // Fully open just as the last cut lands.
                float finalAt = strike - pattern.gleam.time * pattern.gleam.openFraction;

                if (clock >= finalAt)
                {
                    finalDone = true;
                    BladeGleam.Flash(katana, pattern.gleamOffset, pattern.glistenColour, pattern.gleam, pattern.finalGleamSize);
                    return;
                }

                while (clock >= nextGleam)
                {
                    float at = Mathf.Clamp01(nextGleam / strike);
                    Vector3 spot = pattern.gleamOffset + Vector3.up * Random.Range(-pattern.glistenSpread, pattern.glistenSpread);

                    BladeGleam.Flash(katana, spot, pattern.glistenColour, pattern.gleam,
                                     Mathf.Lerp(pattern.glistenSize.x, pattern.glistenSize.y, at));
                    nextGleam += Mathf.Max(0.01f, Mathf.Lerp(pattern.glistenInterval.x, pattern.glistenInterval.y, at));
                }
            }

            /// <summary>Lets the glow fade and sends the Katana back into the orbit.</summary>
            public void End(BossBlades blades, int index)
            {
                if (glow != null)
                {
                    glow.FadeOut(pattern.glowFadeTime);
                }

                blades.Return(index);
            }
        }

        /// <summary>Where each chop goes, bottom to top, splitting the column's height into roughly even chunks.</summary>
        private float[] ChopHeights(Bounds bounds, int chunkCount)
        {
            float[] heights = new float[Mathf.Max(0, chunkCount - 1)];
            float step = bounds.size.y / chunkCount;

            for (int i = 0; i < heights.Length; i++)
            {
                heights[i] = bounds.min.y + step * (i + 1) + Random.Range(-chopJitter, chopJitter) * step;
            }

            return heights;
        }

        /// <summary>Chops the bottom chunk off <paramref name="remaining"/> at <paramref name="centre"/>; returns what is left above.</summary>
        private MeshSlicer.Piece Chop(Enemy enemy, MeshSlicer.Piece remaining, List<Material> materials,
                                      Vector3 centre, Vector3 direction, List<Chunk> chunks, List<Vector3> outline)
        {
            Vector3 normal = Quaternion.AngleAxis(Random.Range(-chopTilt, chopTilt), direction) * Vector3.up;

            if (!MeshSlicer.Slice(remaining, new Plane(normal, centre), cut.cutFaceUvScale,
                                  out MeshSlicer.Piece above, out MeshSlicer.Piece below, outline))
            {
                // Missed it: the column is shorter than expected. It stays whole.
                return remaining;
            }

            AddChunk(enemy, below, materials, chunks);
            return above;
        }

        private void AddChunk(Enemy enemy, MeshSlicer.Piece geometry, List<Material> materials, List<Chunk> chunks)
        {
            GameObject piece = KatanaCut.BuildPiece(geometry, materials, cut, "Column chunk");
            ThrownColumn column = ThrownColumn.Create(piece, enemy, pieces, cut);
            column.AddTrail(trailColour, trailWidth, trailTime);
            column.ShrinkTo(chunkScale, shrinkTime);

            chunks.Add(new Chunk
            {
                column = column,
                cutFrom = piece.transform.position,
                cutRotation = piece.transform.rotation,
                tumbleAxis = Random.onUnitSphere,
                nudge = Random.insideUnitSphere * fanJitter,
            });
        }

        /// <summary>The streak across the screen, the line traced round the cut, and the jolt.</summary>
        private void Flash(Vector3 point, Vector3 direction, List<Vector3> outline)
        {
            if (SlashEffect.Current != null)
            {
                SlashEffect.Current.TriggerSlash(point, direction, slashScale, streakColour);
            }

            CutOutline.Create(outline, outlineColour, outlineWidth, outlineHold, outlineFade);

            if (cutShake > 0f)
            {
                TopDownCamera.Shake(cutShake, cutShakeDuration);
            }
        }

        /// <summary>Keeps every chunk cut so far where it was cut, while the Katana works on.</summary>
        private static void HoldAll(List<Chunk> chunks)
        {
            foreach (Chunk chunk in chunks)
            {
                if (chunk.column != null)
                {
                    chunk.column.Hold(chunk.cutFrom, chunk.cutRotation, Vector3.zero);
                }
            }
        }

        /// <summary>
        /// Poses every chunk not yet thrown: rising from the column toward
        /// its spot in the row, then floating there, <paramref name="clock"/>
        /// seconds after the cutting finished.
        /// </summary>
        private void Hover(Enemy enemy, List<Chunk> chunks, float clock)
        {
            for (int i = 0; i < chunks.Count; i++)
            {
                Chunk chunk = chunks[i];

                if (chunk.column == null || chunk.column.Broken || chunk.column.Thrown)
                {
                    continue;
                }

                float lifted = Smooth((clock - chunk.liftStart) / liftTime);
                Vector3 slot = Slot(enemy, chunk, i, chunks.Count, clock);

                // Rises first, then swings across to its spot.
                Vector3 position = Vector3.Lerp(chunk.cutFrom, slot, lifted);
                position.y = Mathf.Lerp(chunk.cutFrom.y, slot.y, Smooth((clock - chunk.liftStart) / (liftTime * 0.6f)));

                Quaternion rotation = Quaternion.AngleAxis(Mathf.Max(0f, clock - chunk.liftStart) * hoverTumble, chunk.tumbleAxis)
                                    * chunk.cutRotation;

                chunk.column.Hold(position, rotation, ChaseFrame.Velocity);
            }
        }

        /// <summary>Chunk <paramref name="i"/> of <paramref name="count"/>'s spot in the fan behind the boss, bobbing.</summary>
        private Vector3 Slot(Enemy enemy, Chunk chunk, int i, int count, float clock)
        {
            Vector3 behind = -Facing(enemy);

            // Spread evenly across the fan, centred straight behind.
            float share = count > 1 ? (float)i / (count - 1) - 0.5f : 0f;
            Vector3 outward = Quaternion.AngleAxis(share * fanAngle, Vector3.up) * behind;

            // Alternately high and low, so neighbours never line up.
            float stagger = count > 1 ? (i % 2 == 0 ? 1f : -1f) * heightSpread : 0f;

            // Each bobs out of step with its neighbours.
            float bob = Mathf.Sin((clock * 0.8f + i * 0.37f) * Mathf.PI * 2f) * hoverBob;

            return enemy.transform.position
                 + outward * fanRadius
                 + chunk.nudge
                 + Vector3.up * (hoverHeight + stagger + bob);
        }

        /// <summary>
        /// True once there is nothing left to throw or the boss can no longer
        /// hold them; breaks any still waiting if the boss is gone.
        /// </summary>
        private static bool Abandoned(Enemy enemy, List<Chunk> chunks)
        {
            bool bossGone = enemy == null || enemy.Health.IsDead;
            bool anyWaiting = false;

            foreach (Chunk chunk in chunks)
            {
                if (chunk.column == null || chunk.column.Broken || chunk.column.Thrown)
                {
                    continue;
                }

                if (bossGone)
                {
                    chunk.column.Break(chunk.column.transform.position + Vector3.up);
                }
                else
                {
                    anyWaiting = true;
                }
            }

            return bossGone || !anyWaiting;
        }

        /// <summary>Lying flat, its tip leading the way along <paramref name="direction"/>.</summary>
        private static Quaternion SweepPose(Vector3 direction, Quaternion modelOffset)
        {
            return Quaternion.LookRotation(Vector3.up, -direction) * modelOffset;
        }

        /// <summary>
        /// The nearest uncut column ahead: further down the run during one,
        /// otherwise simply the nearest in reach.
        /// </summary>
        private CuttableColumn FindColumn(Enemy enemy)
        {
            Vector3 origin = enemy.transform.position;
            bool running = ChaseFrame.Active;
            Vector3 forward = ChaseFrame.Forward;

            CuttableColumn best = null;
            float bestScore = float.PositiveInfinity;

            foreach (CuttableColumn candidate in CuttableColumn.Active)
            {
                if (candidate == null || candidate.Taken)
                {
                    continue;
                }

                Vector3 offset = candidate.CutPoint - origin;
                offset.y = 0f;
                float score;

                if (running)
                {
                    float ahead = Vector3.Dot(offset, forward);
                    float sideways = (offset - forward * ahead).magnitude;

                    if (ahead < lookAhead.x || ahead > lookAhead.y || sideways > sideReach)
                    {
                        continue;
                    }

                    score = ahead;
                }
                else
                {
                    score = offset.magnitude;

                    if (score > lookAhead.y)
                    {
                        continue;
                    }
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best;
        }

        /// <summary>True while the foot of <paramref name="target"/> is on screen, inside the view margin.</summary>
        private bool OnScreen(CuttableColumn target)
        {
            Camera view = Camera.main;

            // With no camera to ask, there is nothing to wait for.
            if (view == null)
            {
                return true;
            }

            Vector3 point = view.WorldToViewportPoint(target.CutPoint);

            return point.z > 0f
                && point.x >= viewMargin && point.x <= 1f - viewMargin
                && point.y >= viewMargin && point.y <= 1f - viewMargin;
        }

        /// <summary>Down the run, or away from the player outside one.</summary>
        private static Vector3 RunForward(Enemy enemy)
        {
            return ChaseFrame.Active ? ChaseFrame.Forward : -Facing(enemy);
        }

        /// <summary>Flat toward the player, or the way the boss's model faces.</summary>
        private static Vector3 Facing(Enemy enemy)
        {
            Vector3 toPlayer = enemy.HasTarget ? enemy.TargetPoint - enemy.transform.position : Vector3.zero;
            toPlayer.y = 0f;

            if (toPlayer.sqrMagnitude > 0.0001f)
            {
                return toPlayer.normalized;
            }

            Vector3 forward = enemy.Model.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        private static void Shuffle(List<Chunk> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        protected virtual void Reset()
        {
            delayAfter = 2f;
            Movement.type = MovementType.StandStill;
        }
    }
}
