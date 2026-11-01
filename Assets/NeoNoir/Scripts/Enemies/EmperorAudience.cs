using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// The audience with the Emperor: when the player walks up to the throne,
    /// the dialogue plays, Luna asks to kill her, and whichever way the
    /// player answers, Luna flies from the player's side and runs her
    /// through. At the end, Luna's own fight is handed over through
    /// <see cref="LunaFightStarted"/>.
    /// </summary>
    /// <remarks>
    /// Goes on a trigger box near the throne, which needs a trigger collider.
    /// Plays once. Needs the <see cref="EmperorRoom"/>, which has the Emperor.
    ///
    /// In order:
    ///   1. The player's controls lock and the camera turns toward her.
    ///   2. <see cref="opening"/>, then <see cref="question"/> with its two
    ///      choices.
    ///   3. Yes: Luna strikes, then <see cref="afterYes"/>.
    ///      No: <see cref="beforeNoStrike"/>, Luna strikes, then <see cref="afterNo"/>.
    ///   4. Controls unlock, the blades unsheathe, and
    ///      <see cref="LunaFightStarted"/> is raised with Luna hovering at
    ///      her hover point, still <see cref="Blade.Seized"/>.
    ///
    /// The strike: if Luna is in view -- the main blade, or the shown
    /// secondary -- that very blade goes. Otherwise a Luna materialises in
    /// the orbit around the player first. Either way she leaves the player
    /// for good as she flies (see <see cref="BladeArsenal.Surrender"/>): if
    /// she was the main blade, the first other secondary takes her place.
    /// She turns her tip on the Emperor while drawing back, flies at her,
    /// gathering speed, and kills her on arrival, throwing the ragdoll away
    /// from where she came. She holds there a moment, then rises to hover.
    /// </remarks>
    public sealed class EmperorAudience : MonoBehaviour
    {
        [Header("Scene")]
        [SerializeField] private EmperorRoom room;

        [SerializeField]
        [Tooltip("Luna's blade definition: to find her in the player's loadout, or to make one if she is not in it.")]
        private BladeDefinition luna;

        [SerializeField]
        [Tooltip("Where Luna's pivot ends up when she runs the Emperor through. Empty uses the middle of the Emperor's body.")]
        private Transform strikePoint;

        [SerializeField]
        [Tooltip("Luna carries on this far past the strike point (negative stops short).")]
        private float pierce;

        [SerializeField]
        [Tooltip("Where Luna hovers once the deed is done. Empty uses Hover Rise above the strike point.")]
        private Transform hoverPoint;

        [SerializeField]
        [Min(0f)]
        private float hoverRise = 2f;

        [Header("Camera")]
        [SerializeField] private bool turnCamera = true;

        [SerializeField]
        [Tooltip("Seconds the camera takes to swing round toward the Emperor. 0 snaps.")]
        [Min(0f)]
        private float cameraTurnTime = 1f;

        [Header("Dialogue")]
        [SerializeField] private List<DialogueLine> opening = new List<DialogueLine>();

        [SerializeField]
        [Tooltip("The line the choices come up under.")]
        private DialogueLine question = new DialogueLine();

        [SerializeField] private string yesLabel = "Yes";
        [SerializeField] private string noLabel = "No";

        [SerializeField]
        [Tooltip("After the strike, on Yes.")]
        private List<DialogueLine> afterYes = new List<DialogueLine>();

        [SerializeField]
        [Tooltip("Before the strike, on No.")]
        private List<DialogueLine> beforeNoStrike = new List<DialogueLine>();

        [SerializeField]
        [Tooltip("After the strike, on No.")]
        private List<DialogueLine> afterNo = new List<DialogueLine>();

        [Header("Luna appears")]
        [SerializeField]
        [Tooltip("Degrees around the player's orbit, from the main blade, a Luna who was not in view appears at.")]
        private float appearPhase = 90f;

        [SerializeField]
        [Tooltip("Seconds she takes to fade in there.")]
        [Min(0.01f)]
        private float appearTime = 0.5f;

        [SerializeField]
        [Tooltip("Seconds she waits, fully there, before taking aim.")]
        [Min(0f)]
        private float appearHold = 0.3f;

        [Header("Strike")]
        [SerializeField]
        [Tooltip("Seconds Luna takes to turn her tip on the Emperor and draw back.")]
        [Min(0.01f)]
        private float aimTime = 0.4f;

        [SerializeField]
        [Tooltip("How far she draws back while aiming.")]
        [Min(0f)]
        private float drawBack = 0.8f;

        [SerializeField]
        [Tooltip("Seconds the flight to the Emperor takes. It speeds up all the way.")]
        [Min(0.01f)]
        private float flyTime = 0.2f;

        [SerializeField]
        [Tooltip("Seconds she stays where she struck before rising.")]
        [Min(0f)]
        private float holdTime = 0.5f;

        [SerializeField]
        [Tooltip("Seconds she takes to rise to her hover point and turn upright.")]
        [Min(0.01f)]
        private float riseTime = 0.8f;

        [SerializeField]
        [Tooltip("How far the camera shakes as she strikes. 0 disables it.")]
        [Min(0f)]
        private float shakeStrength = 0.6f;

        [SerializeField]
        [Min(0.01f)]
        private float shakeDuration = 0.4f;

        [Header("Hover")]
        [SerializeField] private float bobAmplitude = 0.08f;
        [SerializeField] private float bobFrequency = 0.5f;

        private bool played;
        private PlayerInputReader input;
        private Transform player;
        private Coroutine hovering;

        /// <summary>Luna, once she has struck: seized, hovering, and no longer the player's.</summary>
        public Blade Luna { get; private set; }

        /// <summary>True if the player answered Yes. Only meaningful once the choice is made.</summary>
        public bool ChoseYes { get; private set; }

        /// <summary>Raised at the very end, as controls come back. Luna's fight starts here.</summary>
        public event Action<EmperorAudience> LunaFightStarted;

        private EnemyHealth Emperor => room != null ? room.Emperor : null;

        /// <summary>Luna's blade definition, as the player's loadout knows her.</summary>
        public BladeDefinition LunaDefinition => luna;

        /// <summary>
        /// For the dev shortcut to Luna's fight: the audience is over without
        /// playing, or cut short if it is playing. The Emperor dies, the room
        /// gives the blades back, and no Luna blade is left behind. Returns
        /// where Luna would have hovered. <see cref="LunaFightStarted"/> is
        /// not raised.
        /// </summary>
        public Vector3 Skip()
        {
            played = true;
            StopAllCoroutines();
            hovering = null;

            if (DialogueBox.Instance != null)
            {
                DialogueBox.Instance.Close();
            }

            if (Luna != null)
            {
                Destroy(Luna.gameObject);
                Luna = null;
            }

            EnemyHealth emperor = Emperor;
            Vector3 struck = strikePoint != null ? strikePoint.position
                           : emperor != null ? emperor.BodyBounds().center
                           : transform.position;

            if (emperor != null)
            {
                Kill(emperor, null, emperor.transform.position - emperor.transform.forward);
            }

            if (room != null)
            {
                room.Release();
            }

            return hoverPoint != null ? hoverPoint.position : struck + Vector3.up * hoverRise;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (played)
            {
                return;
            }

            PlayerMotor motor = other.GetComponentInParent<PlayerMotor>();

            if (motor == null)
            {
                return;
            }

            if (Emperor == null)
            {
                Debug.LogWarning($"[EmperorAudience] {name} has no Emperor Room, or its room has no Emperor.", this);
                return;
            }

            played = true;
            player = motor.transform;
            input = motor.GetComponent<PlayerInputReader>();
            StartCoroutine(Play());
        }

        private IEnumerator Play()
        {
            if (input != null)
            {
                input.Locked = true;
            }

            if (turnCamera)
            {
                TopDownCamera.TurnToward(Emperor.transform.position, cameraTurnTime);
            }

            yield return Say(opening);

            int choice = -1;
            DialogueBox.Instance.Ask(question, yesLabel, noLabel, picked => choice = picked);

            while (choice < 0)
            {
                yield return null;
            }

            ChoseYes = choice == 0;

            if (!ChoseYes)
            {
                yield return Say(beforeNoStrike);
            }

            DialogueBox.Instance.Close();
            yield return Strike();

            yield return Say(ChoseYes ? afterYes : afterNo);

            DialogueBox.Instance.Close();

            if (hovering != null)
            {
                StopCoroutine(hovering);
                hovering = null;
            }

            if (input != null)
            {
                input.Locked = false;
            }

            room.Release();
            LunaFightStarted?.Invoke(this);
        }

        /// <summary>Says <paramref name="lines"/> in turn, waiting for each click.</summary>
        private IEnumerator Say(List<DialogueLine> lines)
        {
            foreach (DialogueLine line in lines)
            {
                bool waiting = true;
                DialogueBox.Instance.Say(line, () => waiting = false);

                while (waiting)
                {
                    yield return null;
                }
            }
        }

        private IEnumerator Strike()
        {
            EnemyHealth emperor = Emperor;

            if (luna == null || luna.Prefab == null)
            {
                Debug.LogWarning($"[EmperorAudience] {name} has no Luna blade definition with a prefab, so the Emperor just dies.", this);
                Kill(emperor, null, player.position);
                yield break;
            }

            BladeArsenal arsenal = player.GetComponent<BladeArsenal>();
            Blade blade = arsenal != null ? arsenal.Surrender(luna) : null;

            if (blade == null)
            {
                blade = Instantiate(luna.Prefab, player.position, Quaternion.identity);
                blade.name = luna.DisplayName;
                blade.Bind(player, BladeSlot.Secondary, appearPhase);
                blade.Visibility = 0f;

                // Orbits like any blade while she fades in.
                for (float clock = 0f; clock < appearTime; clock += Time.deltaTime)
                {
                    blade.Visibility = clock / appearTime;
                    yield return null;
                }

                blade.Visibility = 1f;

                if (appearHold > 0f)
                {
                    yield return new WaitForSeconds(appearHold);
                }

                blade.Seized = true;
            }

            Luna = blade;
            Transform sword = blade.transform;

            Vector3 start = sword.position;
            Quaternion startRotation = sword.rotation;
            Vector3 target = strikePoint != null ? strikePoint.position : emperor.BodyBounds().center;

            Vector3 direction = target - start;

            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = player.forward;
            }

            direction.Normalize();
            target += direction * pierce;

            Quaternion stab = TipToward(direction);
            Vector3 drawn = start - direction * drawBack;

            for (float clock = 0f; clock < aimTime; clock += Time.deltaTime)
            {
                float t = Smooth(clock / aimTime);
                sword.SetPositionAndRotation(Vector3.Lerp(start, drawn, t), Quaternion.Slerp(startRotation, stab, t));
                yield return null;
            }

            sword.SetPositionAndRotation(drawn, stab);

            // From rest under constant push: fastest at the end.
            for (float clock = 0f; clock < flyTime; clock += Time.deltaTime)
            {
                float t = clock / flyTime;
                sword.position = Vector3.LerpUnclamped(drawn, target, t * t);
                yield return null;
            }

            sword.position = target;
            Kill(emperor, blade, drawn);

            if (shakeStrength > 0f)
            {
                TopDownCamera.Shake(shakeStrength, shakeDuration);
            }

            if (holdTime > 0f)
            {
                yield return new WaitForSeconds(holdTime);
            }

            Vector3 hover = hoverPoint != null ? hoverPoint.position : target + Vector3.up * hoverRise;
            Quaternion upright = Upright(hover);

            for (float clock = 0f; clock < riseTime; clock += Time.deltaTime)
            {
                float t = Smooth(clock / riseTime);
                sword.SetPositionAndRotation(Vector3.Lerp(target, hover, t), Quaternion.Slerp(stab, upright, t));
                yield return null;
            }

            sword.SetPositionAndRotation(hover, upright);
            hovering = StartCoroutine(Hover(sword, hover));
        }

        private IEnumerator Hover(Transform sword, Vector3 hover)
        {
            float clock = 0f;

            while (sword != null)
            {
                clock += Time.deltaTime;
                sword.position = hover + Vector3.up * (Mathf.Sin(clock * bobFrequency * 2f * Mathf.PI) * bobAmplitude);
                yield return null;
            }
        }

        /// <summary>Kills the Emperor outright, throwing her body away from <paramref name="from"/>.</summary>
        private static void Kill(EnemyHealth emperor, Blade source, Vector3 from)
        {
            if (emperor == null || emperor.IsDead)
            {
                return;
            }

            DeathRagdoll ragdoll = emperor.GetComponent<DeathRagdoll>();

            if (ragdoll != null)
            {
                ragdoll.BlastFrom(from);
            }

            emperor.Invulnerable = false;
            emperor.CannotDie = false;
            emperor.TakeDamage(emperor.CurrentHealth / Mathf.Max(0.0001f, emperor.DamageTakenMultiplier), source, from);

            if (ragdoll != null)
            {
                ragdoll.ClearBlast();
            }
        }

        /// <summary>
        /// Tip (local -Y) along <paramref name="direction"/>, flat (local +Z)
        /// turned as near to straight up as it will go, toward the camera.
        /// </summary>
        private static Quaternion TipToward(Vector3 direction)
        {
            Vector3 flat = Vector3.ProjectOnPlane(Vector3.up, direction);

            if (flat.sqrMagnitude < 0.0001f)
            {
                flat = Vector3.ProjectOnPlane(Vector3.forward, direction);
            }

            return Quaternion.LookRotation(flat, -direction);
        }

        /// <summary>Tip straight down, flat turned toward the player, as a blade rests.</summary>
        private Quaternion Upright(Vector3 at)
        {
            Vector3 toward = player.position - at;
            toward.y = 0f;

            return Quaternion.LookRotation(toward.sqrMagnitude > 0.0001f ? toward : Vector3.forward, Vector3.up);
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
