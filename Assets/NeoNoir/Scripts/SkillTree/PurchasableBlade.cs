using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace LightAndShadow
{
    /// <summary>
    /// A blade stuck in a stone in the shrine, which the player can walk up
    /// to and acquire. One node of the skill tree.
    /// </summary>
    /// <remarks>
    /// Walking within range of a blade that is not yet acquired, and whose
    /// prerequisites all are, shows an "E" prompt. Pressing it locks the
    /// player's controls and plays <see cref="dialogue"/> in the
    /// <see cref="DialogueBox"/>. The last line offers Acquire and Leave.
    ///
    ///   Acquire  the blade is recorded in <see cref="BladeUnlocks"/>, the
    ///            stone changes colour and the blade starts to glow
    ///            (<see cref="bladeGlow"/>) while the blade's
    ///            <see cref="BladeAcquireEffect"/> plays (if it has one), and
    ///            once the effect's hold time is up, any
    ///            <see cref="afterAcquire"/> lines play. Then the player is
    ///            free.
    ///   Leave    any <see cref="afterLeave"/> lines play and the player is
    ///            free. Nothing changes; they can come back.
    ///
    /// The tree is the <see cref="requires"/> lists: a blade can be
    /// interacted with only once every blade listed there is acquired. Luna
    /// requires nothing; Scarlett, Lavender and Rose each require Luna. The
    /// glowing lines between them are <see cref="SkillTreeLink"/>s.
    ///
    /// Acquiring is free for now. Cost goes in <see cref="TryAcquire"/> when
    /// there is a currency to pay it with.
    ///
    /// Acquired state lives in <see cref="BladeUnlocks"/>, not here, so a
    /// blade acquired on an earlier visit loads with its stone already
    /// changed.
    /// </remarks>
    public sealed class PurchasableBlade : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>The blade showing the prompt right now, if any. Only the nearest one in range does.</summary>
        private static PurchasableBlade focused;

        [SerializeField]
        [Tooltip("The blade this acquires.")]
        private BladeDefinition blade;

        [SerializeField]
        [Tooltip("Blades that must all be acquired before this one can be interacted with. Empty for the root of the tree (Luna).")]
        private List<PurchasableBlade> requires = new List<PurchasableBlade>();

        [Header("Scene parts")]
        [SerializeField]
        [Tooltip("The blade model stuck in the stone. Effects play around it, and the prompt shows above it. Empty uses this object.")]
        private Transform bladeModel;

        [SerializeField]
        [Tooltip("Renderers of the stone beneath the blade, which change colour on acquiring.")]
        private Renderer[] stoneRenderers = new Renderer[0];

        [Header("Interaction")]
        [SerializeField]
        [Tooltip("How close (in metres, ignoring height) the player must be to interact.")]
        [Min(0f)]
        private float interactRadius = 2.5f;

        [SerializeField] private string promptText = "E  Interact";

        [SerializeField]
        [Tooltip("Where the prompt shows, relative to the top of the blade model.")]
        private Vector3 promptOffset = new Vector3(0f, 0.3f, 0f);

        [Header("Dialogue")]
        [SerializeField]
        [Tooltip("Played on interacting. The last line offers the choice; with no lines at all, the choice shows on its own.")]
        private List<DialogueLine> dialogue = new List<DialogueLine>();

        [SerializeField] private string acquireLabel = "Acquire";
        [SerializeField] private string leaveLabel = "Leave";

        [SerializeField]
        [Tooltip("Played after the acquire effect. Optional.")]
        private List<DialogueLine> afterAcquire = new List<DialogueLine>();

        [SerializeField]
        [Tooltip("Played on choosing Leave. Optional.")]
        private List<DialogueLine> afterLeave = new List<DialogueLine>();

        [Header("Acquiring")]
        [SerializeField]
        [Tooltip("Colour the stone becomes. HDR: above 1 blooms.")]
        [ColorUsage(false, true)]
        private Color acquiredStoneColour = Color.white;

        [SerializeField]
        [Tooltip("Seconds after choosing Acquire before the stone starts to change. Lets it land with the effect.")]
        [Min(0f)]
        private float stoneDelay;

        [SerializeField]
        [Tooltip("Seconds the stone takes to change colour.")]
        [Min(0f)]
        private float stoneChangeTime = 0.6f;

        [SerializeField]
        [Tooltip("How brightly the blade glows once acquired: its own colour times this. Above 1 blooms; 1 is no glow.")]
        [Min(1f)]
        private float bladeGlow = 3f;

        [SerializeField]
        [Tooltip("Seconds the player is held still after acquiring, when there is no effect to set it.")]
        [Min(0f)]
        private float holdWithoutEffect = 0.8f;

        [SerializeField]
        [Tooltip("Raised the moment this blade is acquired, before the effect.")]
        private UnityEvent acquired = new UnityEvent();

        private Transform player;
        private PlayerInputReader input;
        private BladeAcquireEffect effect;
        private MaterialPropertyBlock block;
        private Color[] stoneStartColours = new Color[0];
        private Renderer[] bladeRenderers = new Renderer[0];
        private Color[] bladeStartColours = new Color[0];
        private bool busy;

        /// <summary>Raised by any blade the moment it is acquired.</summary>
        public static event Action<PurchasableBlade> AnyAcquired;

        public BladeDefinition Blade => blade;

        public bool IsAcquired => BladeUnlocks.IsAcquired(blade);

        /// <summary>True once every blade in <see cref="requires"/> has been acquired.</summary>
        public bool IsUnlocked
        {
            get
            {
                foreach (PurchasableBlade prerequisite in requires)
                {
                    if (prerequisite != null && !prerequisite.IsAcquired)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>Can be walked up to and interacted with right now.</summary>
        public bool IsAvailable => blade != null && !IsAcquired && IsUnlocked;

        public IReadOnlyList<PurchasableBlade> Requires => requires;

        public Transform BladeModel => bladeModel != null ? bladeModel : transform;

        /// <summary>World bounds of the blade model's renderers, for effects to play around.</summary>
        public Bounds BladeBounds
        {
            get
            {
                Renderer[] renderers = BladeModel.GetComponentsInChildren<Renderer>();
                Bounds bounds = new Bounds(BladeModel.position, Vector3.zero);
                bool any = false;

                foreach (Renderer target in renderers)
                {
                    if (Array.IndexOf(stoneRenderers, target) >= 0)
                    {
                        continue;
                    }

                    if (any)
                    {
                        bounds.Encapsulate(target.bounds);
                    }
                    else
                    {
                        bounds = target.bounds;
                        any = true;
                    }
                }

                return bounds;
            }
        }

        /// <summary>The blade model's own material, for effects that match its colour.</summary>
        public Material BladeMaterial
        {
            get
            {
                foreach (Renderer target in BladeModel.GetComponentsInChildren<Renderer>())
                {
                    if (Array.IndexOf(stoneRenderers, target) < 0)
                    {
                        return target.sharedMaterial;
                    }
                }

                return null;
            }
        }

        /// <summary>The middle of the top face of the stone, or the blade's foot without one.</summary>
        public Vector3 StoneTop
        {
            get
            {
                Bounds stone = default;
                bool any = false;

                foreach (Renderer target in stoneRenderers)
                {
                    if (target == null)
                    {
                        continue;
                    }

                    if (any)
                    {
                        stone.Encapsulate(target.bounds);
                    }
                    else
                    {
                        stone = target.bounds;
                        any = true;
                    }
                }

                if (any)
                {
                    return new Vector3(stone.center.x, stone.max.y, stone.center.z);
                }

                Bounds model = BladeBounds;
                return new Vector3(model.center.x, model.min.y, model.center.z);
            }
        }

        private void Awake()
        {
            effect = GetComponent<BladeAcquireEffect>();
            block = new MaterialPropertyBlock();

            stoneStartColours = new Color[stoneRenderers.Length];

            for (int i = 0; i < stoneRenderers.Length; i++)
            {
                Material material = stoneRenderers[i] != null ? stoneRenderers[i].sharedMaterial : null;
                stoneStartColours[i] = ColourOf(material);
            }

            // The blade glows in its own colour, so remember each renderer's.
            List<Renderer> blades = new List<Renderer>();

            foreach (Renderer target in BladeModel.GetComponentsInChildren<Renderer>())
            {
                if (Array.IndexOf(stoneRenderers, target) < 0)
                {
                    blades.Add(target);
                }
            }

            bladeRenderers = blades.ToArray();
            bladeStartColours = new Color[bladeRenderers.Length];

            for (int i = 0; i < bladeRenderers.Length; i++)
            {
                bladeStartColours[i] = ColourOf(bladeRenderers[i].sharedMaterial);
            }

            if (blade == null)
            {
                Debug.LogWarning($"[PurchasableBlade] {name} has no blade assigned, so it can never be acquired.", this);
            }
        }

        private void Start()
        {
            // Acquired on an earlier visit: already changed, no effect.
            if (IsAcquired)
            {
                SetStone(1f);
            }
        }

        private void OnDisable()
        {
            ReleaseFocus();
        }

        private void Update()
        {
            if (busy)
            {
                return;
            }

            if (!FindPlayer() || !IsAvailable || DialogueBox.Instance.IsOpen)
            {
                ReleaseFocus();
                return;
            }

            float distance = PlanarDistance();

            if (distance > interactRadius)
            {
                ReleaseFocus();
                return;
            }

            // The nearest available blade in range takes the prompt.
            if (focused != this && focused != null && focused.isActiveAndEnabled
                && focused.PlanarDistance() <= distance)
            {
                return;
            }

            focused = this;

            Bounds bounds = BladeBounds;
            DialogueBox.Instance.ShowPrompt(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z) + promptOffset, promptText);

            if (input.InteractPressed)
            {
                Interact();
            }
        }

        /// <summary>Starts the dialogue, as pressing E in range does.</summary>
        public void Interact()
        {
            if (busy || !IsAvailable)
            {
                return;
            }

            busy = true;
            ReleaseFocus();

            if (input != null)
            {
                input.Locked = true;
            }

            // Every line but the last is said; the last one asks.
            Play(dialogue, 0, dialogue.Count - 1, Ask);
        }

        /// <summary>Plays <paramref name="lines"/> up to but not including <paramref name="end"/>, then calls <paramref name="done"/>.</summary>
        private void Play(List<DialogueLine> lines, int index, int end, Action done)
        {
            if (lines == null || index >= end || index >= lines.Count)
            {
                done();
                return;
            }

            DialogueBox.Instance.Say(lines[index], () => Play(lines, index + 1, end, done));
        }

        private void Play(List<DialogueLine> lines, Action done)
        {
            Play(lines, 0, lines != null ? lines.Count : 0, done);
        }

        private void Ask()
        {
            DialogueLine question = dialogue.Count > 0 ? dialogue[dialogue.Count - 1] : null;
            DialogueBox.Instance.Ask(question, acquireLabel, leaveLabel, OnChosen);
        }

        private void OnChosen(int choice)
        {
            if (choice == 0 && TryAcquire())
            {
                DialogueBox.Instance.Close();
                StartCoroutine(AcquireSequence());
            }
            else
            {
                Play(afterLeave, Finish);
            }
        }

        /// <summary>Pays for and records the blade. Free for now.</summary>
        private bool TryAcquire()
        {
            if (!BladeUnlocks.Acquire(blade))
            {
                return false;
            }

            acquired.Invoke();
            AnyAcquired?.Invoke(this);
            return true;
        }

        private IEnumerator AcquireSequence()
        {
            if (effect != null)
            {
                effect.Play(this);
            }

            StartCoroutine(ChangeStone());

            float hold = effect != null ? effect.HoldTime : holdWithoutEffect;

            if (hold > 0f)
            {
                yield return new WaitForSeconds(hold);
            }

            Play(afterAcquire, Finish);
        }

        private IEnumerator ChangeStone()
        {
            if (stoneDelay > 0f)
            {
                yield return new WaitForSeconds(stoneDelay);
            }

            float clock = 0f;

            while (clock < stoneChangeTime)
            {
                clock += Time.deltaTime;
                float t = Mathf.Clamp01(clock / stoneChangeTime);
                SetStone(t * t * (3f - 2f * t));
                yield return null;
            }

            SetStone(1f);
        }

        private void Finish()
        {
            DialogueBox.Instance.Close();

            if (input != null)
            {
                input.Locked = false;
            }

            busy = false;
        }

        private void SetStone(float t)
        {
            for (int i = 0; i < stoneRenderers.Length; i++)
            {
                Renderer target = stoneRenderers[i];

                if (target == null)
                {
                    continue;
                }

                SetColour(target, Color.Lerp(stoneStartColours[i], acquiredStoneColour, t));
            }

            // The blade lights up with the stone.
            for (int i = 0; i < bladeRenderers.Length; i++)
            {
                if (bladeRenderers[i] == null)
                {
                    continue;
                }

                Color start = bladeStartColours[i];
                Color glow = start * bladeGlow;
                glow.a = start.a;
                SetColour(bladeRenderers[i], Color.Lerp(start, glow, t));
            }
        }

        private void SetColour(Renderer target, Color colour)
        {
            target.GetPropertyBlock(block);
            block.SetColor(BaseColorId, colour);
            block.SetColor(ColorId, colour);
            target.SetPropertyBlock(block);
        }

        private static Color ColourOf(Material material)
        {
            if (material == null)
            {
                return Color.black;
            }

            if (material.HasProperty(BaseColorId))
            {
                return material.GetColor(BaseColorId);
            }

            return material.HasProperty(ColorId) ? material.GetColor(ColorId) : Color.black;
        }

        private bool FindPlayer()
        {
            if (player == null)
            {
                PlayerMotor motor = FindFirstObjectByType<PlayerMotor>(FindObjectsInactive.Exclude);

                if (motor == null)
                {
                    return false;
                }

                player = motor.transform;
                input = motor.GetComponent<PlayerInputReader>();
            }

            return input != null;
        }

        private float PlanarDistance()
        {
            if (player == null)
            {
                return float.PositiveInfinity;
            }

            Vector3 offset = player.position - BladeModel.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        private void ReleaseFocus()
        {
            if (focused != this)
            {
                return;
            }

            focused = null;

            // The box may already be gone when the scene is unloading.
            DialogueBox box = FindFirstObjectByType<DialogueBox>(FindObjectsInactive.Include);

            if (box != null)
            {
                box.HidePrompt();
            }
        }

        // With domain reload off, statics carry over between play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            focused = null;
            AnyAcquired = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Vector3 centre = bladeModel != null ? bladeModel.position : transform.position;
            Gizmos.DrawWireSphere(centre, interactRadius);

            Gizmos.color = Color.cyan;

            foreach (PurchasableBlade prerequisite in requires)
            {
                if (prerequisite != null)
                {
                    Gizmos.DrawLine(centre, prerequisite.BladeModel.position);
                }
            }
        }
    }
}
