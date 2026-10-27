using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// What the altars feed: each altar that lights sends an arc of runes
    /// through the air to the centre object, which wakes a little more as
    /// each arrives, and once every one is lit it summons a boss.
    /// </summary>
    /// <remarks>
    /// The centre object is a <see cref="SummoningCore"/>, set in
    /// <see cref="centre"/>: the arcs land on its pivot, and as each one
    /// arrives the core is raised to the fraction of altars whose arcs have
    /// arrived. Without one, the arcs land just above the top of this object.
    ///
    /// Once every arc has arrived, the summoning starts as soon as the
    /// player comes within <see cref="summonRadius"/> of the centre. The
    /// player's controls lock, then:
    ///   1. A great rune circle is drawn on the floor, centred under the core.
    ///   2. The core surges, spewing runes and spinning faster and faster.
    ///   3. A <see cref="LightPillar"/> strikes down from the sky through the
    ///      core to the floor and widens until it swallows the whole device.
    ///   4. Inside the light, the core, its rings, the arcs and this object
    ///      with everything under it dissolve away, and the boss appears
    ///      standing at the centre.
    ///   5. The light narrows and withdraws into the sky, revealing the boss, and its
    ///      <see cref="BossIntro"/> carries on from its entrance animation:
    ///      dialogue, health bar, gates, fight.
    /// The rune circle stays on the floor for the fight.
    ///
    /// Turn off the boss intro's Start On Trigger so walking into it does not
    /// start the fight early; it needs no collider then. Its drop is
    /// ignored: the boss appears in the light, standing on the intro's
    /// landing point if it has one, or on the floor found under the core.
    ///
    /// The altars can be lit in any order. Summons once.
    ///
    /// Once the boss fight starts, every altar's core is armed, so the player
    /// can break it (see <see cref="Altar.ArmCore"/>). Each time the boss
    /// enters one of the phases in <see cref="restoreCoresOnPhases"/>, every
    /// broken core is restored.
    ///
    /// The boss also gets a <see cref="RunicShield"/>, fed by those cores:
    /// while any of them stands, no blow can kill it, so the player must
    /// break every core to finish the fight.
    /// </remarks>
    public sealed class SummoningAltar : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Every altar that must be lit.")]
        private List<Altar> altars = new List<Altar>();

        [SerializeField]
        [Tooltip("Boss intro started once all the altars are lit. Turn off its Start On Trigger.")]
        private BossIntro bossIntro;

        [SerializeField]
        [Tooltip("The centre object, with a Summoning Core on it. The arcs land on it and it wakes as they arrive. Empty lands them just above the top of this object.")]
        private SummoningCore centre;

        [Header("Rune arcs")]
        [SerializeField]
        [Tooltip("Send an arc of runes through the air from each altar to the centre as it lights.")]
        private bool drawArcs = true;

        [SerializeField] private RuneArc.Settings arc = new RuneArc.Settings();

        [Header("Summoning")]
        [SerializeField]
        [Tooltip("Once every altar is lit, the summoning starts when the player comes this close to the centre, measured flat.")]
        [Min(0f)]
        private float summonRadius = 15f;

        [SerializeField]
        [Tooltip("The floor under the centre, where the rune circle is drawn and the boss appears. Empty finds the floor under the centre object.")]
        private Transform floorPoint;

        [SerializeField]
        [Tooltip("Seconds after the circle starts drawing before the core surges.")]
        [Min(0f)]
        private float surgeDelay = 1.5f;

        [SerializeField]
        [Tooltip("How many times harder the core spews and spins at the height of the surge.")]
        [Min(1f)]
        private float surgeMultiplier = 4f;

        [SerializeField]
        [Tooltip("Seconds the core takes to build up to the full surge.")]
        [Min(0f)]
        private float surgeRamp = 1.5f;

        [SerializeField]
        [Tooltip("Seconds at full surge before the light strikes.")]
        [Min(0f)]
        private float surgeHold = 1.5f;

        [SerializeField]
        [Tooltip("Seconds the device takes to dissolve inside the light.")]
        [Min(0.01f)]
        private float consumeTime = 0.6f;

        [SerializeField]
        [Tooltip("Seconds the light holds after the boss appears inside it, before it narrows and withdraws.")]
        [Min(0f)]
        private float revealDelay = 1f;

        [SerializeField]
        private RuneCircle.Settings summoningCircle = new RuneCircle.Settings
        {
            radius = 45f,
            runeCount = 16,
            runeSize = 4f,
            ringWidth = 0.4f,
            runeWidth = 0.25f,
            drawTime = 3f,
            spinSpeed = 0.5f,
        };

        [SerializeField] private LightPillar.Settings pillar = new LightPillar.Settings();

        [Header("Fight")]
        [SerializeField]
        [Tooltip("Restore every broken altar core when the boss enters any of these phases: indices into its phase list, 0 being the first.")]
        private List<int> restoreCoresOnPhases = new List<int> { 1 };

        [SerializeField]
        [Tooltip("Shield the boss with a sphere of runes while any altar core stands, so it cannot be killed until every core is broken.")]
        private bool shieldBoss = true;

        [SerializeField] private RunicShield.Settings runicShield = new RunicShield.Settings();

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private readonly List<RuneArc> arcs = new List<RuneArc>();
        private readonly HashSet<Altar> arcsSent = new HashSet<Altar>();
        private int arrived;
        private bool completed;
        private Enemy fightingBoss;

        /// <summary>How many of the altars are lit.</summary>
        public int LitCount
        {
            get
            {
                int count = 0;

                foreach (Altar altar in altars)
                {
                    if (altar != null && altar.IsLit)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public int AltarCount => altars.Count;

        /// <summary>Where the arcs land: the centre, or just above the top of this object.</summary>
        public Vector3 ArcTarget
        {
            get
            {
                if (centre != null)
                {
                    return centre.Centre;
                }

                Renderer[] renderers = GetComponentsInChildren<Renderer>();

                if (renderers.Length == 0)
                {
                    return transform.position + Vector3.up;
                }

                Bounds bounds = renderers[0].bounds;

                foreach (Renderer part in renderers)
                {
                    bounds.Encapsulate(part.bounds);
                }

                return new Vector3(bounds.center.x, bounds.max.y + 0.3f, bounds.center.z);
            }
        }

        /// <summary>Raised whenever one of the altars lights, with that altar.</summary>
        public event Action<SummoningAltar, Altar> AltarLit;

        /// <summary>Raised once, when the last altar lights, before its arc arrives.</summary>
        public event Action<SummoningAltar> Completed;

        /// <summary>Raised once, as the summoning starts with the player in range.</summary>
        public event Action<SummoningAltar> SummoningStarted;

        /// <summary>Middle of the device: the core, or this object without one.</summary>
        public Vector3 CentrePoint => centre != null ? centre.Centre : transform.position;

        /// <summary>The floor under the centre: the floor point, or found below the core.</summary>
        private Vector3 Floor
        {
            get
            {
                if (floorPoint != null)
                {
                    return floorPoint.position;
                }

                return RuneCircle.FloorBelow(CentrePoint, centre != null ? centre.transform : null, transform);
            }
        }

        private void OnEnable()
        {
            foreach (Altar altar in altars)
            {
                if (altar != null)
                {
                    altar.Lit += OnAltarLit;
                }
            }

            if (bossIntro != null)
            {
                bossIntro.FightStarted += OnFightStarted;
            }

            if (fightingBoss != null)
            {
                fightingBoss.PhaseChanged += OnBossPhaseChanged;
            }
        }

        private void OnDisable()
        {
            foreach (Altar altar in altars)
            {
                if (altar != null)
                {
                    altar.Lit -= OnAltarLit;
                }
            }

            if (bossIntro != null)
            {
                bossIntro.FightStarted -= OnFightStarted;
            }

            if (fightingBoss != null)
            {
                fightingBoss.PhaseChanged -= OnBossPhaseChanged;
            }
        }

        private void OnFightStarted(BossIntro intro)
        {
            foreach (Altar altar in altars)
            {
                if (altar != null)
                {
                    altar.ArmCore();
                }
            }

            if (fightingBoss == null && intro.Boss != null)
            {
                fightingBoss = intro.Boss;
                fightingBoss.PhaseChanged += OnBossPhaseChanged;

                if (shieldBoss)
                {
                    RunicShield.Raise(fightingBoss.Health, altars, runicShield);
                }
            }
        }

        private void OnBossPhaseChanged(Enemy boss, int phase)
        {
            if (!restoreCoresOnPhases.Contains(phase))
            {
                return;
            }

            foreach (Altar altar in altars)
            {
                if (altar != null)
                {
                    altar.RestoreCore();
                }
            }
        }

        private void Start()
        {
            if (bossIntro == null)
            {
                Debug.LogWarning($"[SummoningAltar] {name} has no boss intro assigned.", this);
            }

            // Altars lit before this was listening still count, and still
            // send their arcs.
            foreach (Altar altar in altars)
            {
                if (altar != null && altar.IsLit)
                {
                    SendArc(altar);
                }
            }

            CheckComplete();
        }

        private void OnAltarLit(Altar altar)
        {
            SendArc(altar);
            AltarLit?.Invoke(this, altar);
            CheckComplete();
        }

        private void SendArc(Altar altar)
        {
            // Once each, however the altar's lighting was heard.
            if (!arcsSent.Add(altar))
            {
                return;
            }

            RuneArc sent = drawArcs ? RuneArc.Create(altar.ArcOrigin, ArcTarget, arc) : null;

            if (sent != null)
            {
                arcs.Add(sent);
            }

            StartCoroutine(PowerOnArrival(sent));
        }

        /// <summary>Wakes the centre a step further once the arc has been written all the way to it.</summary>
        private IEnumerator PowerOnArrival(RuneArc sent)
        {
            while (sent != null && !sent.IsComplete)
            {
                yield return null;
            }

            arrived++;

            if (centre != null)
            {
                centre.SetPower(arrived, altars.Count);
            }
        }

        private void CheckComplete()
        {
            if (completed || altars.Count == 0 || LitCount < altars.Count)
            {
                return;
            }

            completed = true;
            Completed?.Invoke(this);
            StartCoroutine(Summon());
        }

        private IEnumerator Summon()
        {
            foreach (RuneArc written in arcs)
            {
                while (written != null && !written.IsComplete)
                {
                    yield return null;
                }
            }

            PlayerMotor player = null;

            while (player == null || FlatDistance(player.transform.position, CentrePoint) > summonRadius)
            {
                yield return null;

                if (player == null)
                {
                    player = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);
                }
            }

            PlayerInputReader input = player.GetComponent<PlayerInputReader>();

            if (input != null)
            {
                input.Locked = true;
            }

            SummoningStarted?.Invoke(this);
            Vector3 floor = Floor;

            RuneCircle.Create(floor, summoningCircle);
            yield return Wait(surgeDelay);

            if (centre != null)
            {
                centre.Surge(surgeMultiplier, surgeRamp);
            }

            yield return Wait(surgeRamp + surgeHold);

            LightPillar beam = LightPillar.Create(floor, pillar);

            while (!beam.IsFull)
            {
                yield return null;
            }

            // All of it hidden inside the light by now.
            if (centre != null)
            {
                centre.Consume(consumeTime);
            }

            foreach (RuneArc written in arcs)
            {
                if (written != null)
                {
                    written.Dissolve(consumeTime);
                }
            }

            StartCoroutine(Dissolve());

            bool summoned = false;

            if (bossIntro != null)
            {
                bossIntro.HoldDialogue = true;
                summoned = bossIntro.Begin(floor, false);
            }

            yield return Wait(revealDelay);

            beam.Withdraw();

            while (beam != null)
            {
                yield return null;
            }

            // The intro unlocks the controls itself once its dialogue is over.
            if (summoned)
            {
                bossIntro.HoldDialogue = false;
            }
            else if (input != null)
            {
                input.Locked = false;
            }
        }

        /// <summary>
        /// Switches off the colliders of this object and everything under it
        /// at once, then dissolves their renderers over the consume time and
        /// switches those off too, so nothing of the device is left to see or
        /// bump into. The object itself stays on, as the summoning still runs
        /// on it.
        /// </summary>
        /// <remarks>
        /// The colliders go first, before the boss appears: it stands where
        /// the device was, and a body that appears inside a collider is shoved
        /// out of it -- through a thin floor, as often as not.
        /// </remarks>
        private IEnumerator Dissolve()
        {
            foreach (Collider solid in GetComponentsInChildren<Collider>())
            {
                solid.enabled = false;
            }

            Renderer[] parts = GetComponentsInChildren<Renderer>();
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            float clock = 0f;

            while (clock < consumeTime)
            {
                clock += Time.deltaTime;
                float fade = 1f - Mathf.Clamp01(clock / consumeTime);

                foreach (Renderer part in parts)
                {
                    if (part == null)
                    {
                        continue;
                    }

                    part.GetPropertyBlock(block);
                    block.SetFloat(FadeId, fade);
                    part.SetPropertyBlock(block);
                }

                yield return null;
            }

            foreach (Renderer part in parts)
            {
                if (part != null)
                {
                    part.enabled = false;
                }
            }
        }

        private static WaitForSeconds Wait(float seconds)
        {
            return seconds > 0f ? new WaitForSeconds(seconds) : null;
        }

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.15f, 0.1f, 0.8f);
            Vector3 target = ArcTarget;

            foreach (Altar altar in altars)
            {
                if (altar != null)
                {
                    Gizmos.DrawLine(altar.ArcOrigin, target);
                }
            }

            if (bossIntro != null)
            {
                Gizmos.DrawLine(transform.position, bossIntro.transform.position);
            }

            // The summoning range, flat round the centre.
            Vector3 middle = CentrePoint;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);

            for (int i = 0; i < 48; i++)
            {
                float from = i * Mathf.PI * 2f / 48f;
                float to = (i + 1) * Mathf.PI * 2f / 48f;
                Gizmos.DrawLine(middle + new Vector3(Mathf.Cos(from), 0f, Mathf.Sin(from)) * summonRadius,
                                middle + new Vector3(Mathf.Cos(to), 0f, Mathf.Sin(to)) * summonRadius);
            }
        }
    }
}
