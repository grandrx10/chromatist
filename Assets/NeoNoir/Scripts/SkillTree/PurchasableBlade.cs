using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

namespace LightAndShadow
{
    /// <summary>
    /// A blade stuck in a stone in the Afterworld, which the player can walk
    /// up to, activate and upgrade. One node of the skill tree.
    /// </summary>
    /// <remarks>
    /// Walking within range of a blade whose prerequisites are all owned
    /// shows an "E" prompt. Pressing it locks the player's controls and plays
    /// the blade's <see cref="stats"/> in the <see cref="DialogueBox"/>, then:
    ///
    ///   Not owned       asks <see cref="activateQuestion"/>, Yes or No.
    ///   Owned           asks <see cref="upgradeQuestion"/>, Yes or No,
    ///                   while there is a next level in <see cref="levels"/>.
    ///   Fully levelled  nothing more; the stats were all there was to say.
    ///
    /// No ends the conversation. Yes costs the next level's
    /// <see cref="BladeLevel.cost"/> in <see cref="ChromaPoints"/>; without
    /// enough, <see cref="notEnoughChroma"/> is said and nothing changes.
    /// With enough, the spent orbs fly off the player's ring into the blade
    /// (<see cref="ChromaOrbs"/>), and once they land:
    ///
    ///   Activating  the blade becomes level 1 in <see cref="BladeUnlocks"/>,
    ///               the stone lights up and the blade starts to glow
    ///               (<see cref="bladeGlow"/>), the blade's
    ///               <see cref="BladeAcquireEffect"/> plays (if it has one),
    ///               the floor lines to the blades it unlocks grow
    ///               (<see cref="SkillTreeLines"/>), and the level 1 dialogue
    ///               plays.
    ///   Upgrading   the blade goes up a level, a
    ///               <see cref="BladeUpgradeBurst"/> plays round it and it
    ///               flares, and that level's dialogue plays.
    ///
    /// The tree is the <see cref="requires"/> lists: a blade can be
    /// interacted with only once every blade listed there is owned.
    ///
    /// Owned state lives in <see cref="BladeUnlocks"/>, not here, so a blade
    /// activated on an earlier visit loads with its stone already lit.
    /// </remarks>
    public sealed class PurchasableBlade : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>The blade showing the prompt right now, if any. Only the nearest one in range does.</summary>
        private static PurchasableBlade focused;

        [Serializable]
        public sealed class BladeLevel
        {
            [Tooltip("Chroma it costs to reach this level.")]
            [Min(0)] public int cost = 1;

            [Tooltip("Played on reaching this level.")]
            public List<DialogueLine> dialogue = new List<DialogueLine>();
        }

        [SerializeField]
        [Tooltip("The blade this activates.")]
        private BladeDefinition blade;

        [SerializeField]
        [Tooltip("Blades that must all be owned before this one can be interacted with. Empty for the root of the tree (Luna).")]
        private List<PurchasableBlade> requires = new List<PurchasableBlade>();

        [Header("Scene parts")]
        [SerializeField]
        [Tooltip("The blade model stuck in the stone. Effects play around it, and the prompt shows above it. Empty uses this object.")]
        private Transform bladeModel;

        [SerializeField]
        [Tooltip("Renderers of the stone beneath the blade, which light up on activating.")]
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
        [FormerlySerializedAs("dialogue")]
        [Tooltip("Played every time the blade is interacted with: what it does. Rich text allowed.")]
        private List<DialogueLine> stats = new List<DialogueLine>();

        [SerializeField]
        [Tooltip("Asked after the stats when the blade is not owned. Leave its text empty to put the choice under the last stats line instead.")]
        private DialogueLine activateQuestion = new DialogueLine { text = "Activate?" };

        [SerializeField]
        [Tooltip("Asked after the stats when the blade is owned and has a next level. Leave its text empty to put the choice under the last stats line instead.")]
        private DialogueLine upgradeQuestion = new DialogueLine { text = "Upgrade?" };

        [SerializeField] private string yesLabel = "Yes";
        [SerializeField] private string noLabel = "No";

        [SerializeField]
        [Tooltip("Said on choosing Yes without the Chroma to pay for it.")]
        private DialogueLine notEnoughChroma = new DialogueLine { text = "Not enough chroma." };

        [SerializeField]
        [Tooltip("Level 1 is activating the blade; each one after is an upgrade. Each has its Chroma cost and the dialogue played on reaching it.")]
        private List<BladeLevel> levels = new List<BladeLevel> { new BladeLevel() };

        [Header("Activating")]
        [SerializeField]
        [Tooltip("Colour the stone becomes. HDR: above 1 blooms.")]
        [ColorUsage(false, true)]
        private Color acquiredStoneColour = new Color(4f, 4f, 4f, 1f);

        [SerializeField]
        [Tooltip("Seconds after the orb lands before the stone starts to light. Lets it land with the effect.")]
        [Min(0f)]
        private float stoneDelay;

        [SerializeField]
        [Tooltip("Seconds the stone takes to light.")]
        [Min(0f)]
        private float stoneChangeTime = 0.6f;

        [SerializeField]
        [Tooltip("How brightly the blade glows once owned: its own colour times this. Above 1 blooms; 1 is no glow.")]
        [Min(1f)]
        private float bladeGlow = 3f;

        [SerializeField]
        [Tooltip("Seconds the player is held still after activating, when there is no effect to set it.")]
        [Min(0f)]
        private float holdWithoutEffect = 0.8f;

        [SerializeField]
        [Tooltip("Raised the moment this blade is activated, before the effect.")]
        private UnityEvent acquired = new UnityEvent();

        [Header("Upgrading")]
        [SerializeField]
        private BladeUpgradeBurst.Settings upgradeEffect = new BladeUpgradeBurst.Settings();

        [SerializeField]
        [Tooltip("How much brighter than its glow the blade flares on upgrading, before settling back.")]
        [Min(1f)]
        private float upgradeFlare = 3f;

        [SerializeField]
        [Tooltip("Seconds the flare takes to settle.")]
        [Min(0.01f)]
        private float upgradeFlareTime = 0.8f;

        [SerializeField]
        [Tooltip("Rings on the floor round the stone, one per upgrade: a white outline filled with the blade's colour.")]
        private BladeUpgradeRings.Settings upgradeRings = new BladeUpgradeRings.Settings();

        [SerializeField]
        [Tooltip("Seconds the player is held still after upgrading, before that level's dialogue.")]
        [Min(0f)]
        private float upgradeHold = 1f;

        private Transform player;
        private PlayerInputReader input;
        private BladeAcquireEffect effect;
        private MaterialPropertyBlock block;
        private Color[] stoneStartColours = new Color[0];
        private Renderer[] bladeRenderers = new Renderer[0];
        private Color[] bladeStartColours = new Color[0];
        private float flare = 1f;
        private bool busy;
        private BladeUpgradeRings rings;

        /// <summary>Raised by any blade the moment it is activated.</summary>
        public static event Action<PurchasableBlade> AnyAcquired;

        public BladeDefinition Blade => blade;

        public bool IsAcquired => BladeUnlocks.IsAcquired(blade);

        /// <summary>0 if not owned, 1 once activated, and one more for each upgrade.</summary>
        public int Level => BladeUnlocks.LevelOf(blade);

        /// <summary>The highest level there is: one per entry in the levels list, and never less than 1.</summary>
        public int MaxLevel => Mathf.Max(1, levels.Count);

        /// <summary>True once every blade in <see cref="requires"/> is owned.</summary>
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
        public bool IsAvailable => blade != null && IsUnlocked;

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

        /// <summary>How far the stone reaches out from its middle across the floor, or half a metre without one.</summary>
        public float StoneRadius
        {
            get
            {
                Vector3 centre = StoneTop;
                float radius = 0f;

                foreach (Renderer target in stoneRenderers)
                {
                    if (target == null)
                    {
                        continue;
                    }

                    Bounds bounds = target.bounds;
                    radius = Mathf.Max(radius,
                                       Mathf.Abs(bounds.max.x - centre.x), Mathf.Abs(bounds.min.x - centre.x),
                                       Mathf.Abs(bounds.max.z - centre.z), Mathf.Abs(bounds.min.z - centre.z));
                }

                return radius > 0f ? radius : 0.5f;
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
                Debug.LogWarning($"[PurchasableBlade] {name} has no blade assigned, so it can never be activated.", this);
            }
        }

        private void Start()
        {
            // Only a scene with blades to buy shows the Chroma and the lines.
            _ = ChromaOrbs.Instance;
            _ = SkillTreeLines.Instance;

            // Activated on an earlier visit: already lit, no effect.
            if (IsAcquired)
            {
                SetStone(1f);
            }

            // Upgrades bought on earlier visits are already ringed.
            if (blade != null)
            {
                rings = BladeUpgradeRings.Create(this, upgradeRings, BladeColour);
                rings.Show(Mathf.Max(0, Level - 1));
            }
        }

        /// <summary>The blade model's own colour, before any glow.</summary>
        private Color BladeColour => bladeStartColours.Length > 0 ? bladeStartColours[0] : Color.white;

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

            if (!IsAcquired)
            {
                AskAfterStats(activateQuestion, OnActivateChosen);
            }
            else if (Level < MaxLevel)
            {
                AskAfterStats(upgradeQuestion, OnUpgradeChosen);
            }
            else
            {
                Play(stats, Finish);
            }
        }

        /// <summary>
        /// Plays the stats, then asks <paramref name="question"/>. A question
        /// with no text asks with the last stats line instead, so the stats
        /// stay up while the player decides.
        /// </summary>
        private void AskAfterStats(DialogueLine question, Action<int> chosen)
        {
            bool onLastLine = (question == null || string.IsNullOrWhiteSpace(question.text)) && stats.Count > 0;

            if (onLastLine)
            {
                Play(stats, 0, stats.Count - 1, () => DialogueBox.Instance.Ask(stats[stats.Count - 1], yesLabel, noLabel, chosen));
            }
            else
            {
                Play(stats, () => DialogueBox.Instance.Ask(question, yesLabel, noLabel, chosen));
            }
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

        private void OnActivateChosen(int choice)
        {
            if (choice == 0 && Pay())
            {
                StartCoroutine(AcquireSequence());
            }
            else if (choice != 0)
            {
                Finish();
            }
        }

        private void OnUpgradeChosen(int choice)
        {
            if (choice == 0 && Pay())
            {
                StartCoroutine(UpgradeSequence());
            }
            else if (choice != 0)
            {
                Finish();
            }
        }

        /// <summary>Chroma it costs to reach the next level.</summary>
        private int NextCost => CostOf(Level + 1);

        private int CostOf(int level)
        {
            int index = level - 1;
            return index >= 0 && index < levels.Count && levels[index] != null ? levels[index].cost : 1;
        }

        /// <summary>Takes the next level's cost, or says there is not enough and ends the conversation.</summary>
        private bool Pay()
        {
            if (!ChromaPoints.TrySpend(NextCost))
            {
                DialogueBox.Instance.Say(notEnoughChroma, Finish);
                return false;
            }

            DialogueBox.Instance.Close();
            return true;
        }

        /// <summary>Flies the spent orbs into the blade and waits for them to land.</summary>
        private IEnumerator AbsorbOrbs(int cost)
        {
            if (cost <= 0)
            {
                yield break;
            }

            bool landed = false;
            ChromaOrbs.Instance.Absorb(cost, BladeBounds.center, () => landed = true);

            while (!landed)
            {
                yield return null;
            }
        }

        private IEnumerator AcquireSequence()
        {
            yield return AbsorbOrbs(CostOf(1));

            if (!BladeUnlocks.Acquire(blade))
            {
                Finish();
                yield break;
            }

            acquired.Invoke();
            AnyAcquired?.Invoke(this);

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

            Play(LevelDialogue(1), Finish);
        }

        private IEnumerator UpgradeSequence()
        {
            yield return AbsorbOrbs(CostOf(Level + 1));

            int level = BladeUnlocks.Upgrade(blade);
            BladeUpgradeBurst.Play(this, upgradeEffect);
            StartCoroutine(Flare());

            if (rings != null && rings.Count < level - 1)
            {
                rings.Form();
            }

            if (upgradeHold > 0f)
            {
                yield return new WaitForSeconds(upgradeHold);
            }

            Play(LevelDialogue(level), Finish);
        }

        private List<DialogueLine> LevelDialogue(int level)
        {
            int index = level - 1;
            return index >= 0 && index < levels.Count && levels[index] != null ? levels[index].dialogue : null;
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

        /// <summary>The blade blazes past its glow, then settles back.</summary>
        private IEnumerator Flare()
        {
            float clock = 0f;

            while (clock < upgradeFlareTime)
            {
                clock += Time.deltaTime;
                float t = Mathf.Clamp01(clock / upgradeFlareTime);
                flare = Mathf.Lerp(upgradeFlare, 1f, t * t);
                SetStone(1f);
                yield return null;
            }

            flare = 1f;
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
                Color glow = start * (bladeGlow * flare);
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
