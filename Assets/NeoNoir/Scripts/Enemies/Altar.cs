using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// An altar the player strikes until it lights: it takes damage like an
    /// enemy, and at zero health turns its glow parts to the lit material
    /// instead of dying.
    /// </summary>
    /// <remarks>
    /// Its hit points are an ordinary <see cref="EnemyHealth"/>, so every
    /// weapon, damage number and vulnerability works on it as on any enemy.
    /// It has no <see cref="Enemy"/> brain, so it never moves or attacks.
    /// The health is kept from destroying it whatever its own setting says.
    ///
    /// A <see cref="RuneCircle"/> on the floor around it writes itself in,
    /// dimly, as the altar loses health, and finishes and lights when the
    /// altar does.
    ///
    /// Once lit it stays lit, and being dead it shrugs off further blows.
    /// Put several under a <see cref="SummoningAltar"/> to have lighting them
    /// all summon a boss.
    ///
    /// Its <see cref="core"/>, a child object with a collider, is what the
    /// player breaks once the fight is on. <see cref="ArmCore"/>, called by
    /// the summoning altar when the boss fight starts, gives the core its own
    /// <see cref="EnemyHealth"/> -- <see cref="coreHealthMultiplier"/> times
    /// the altar's -- and from then on blows anywhere on the altar, its own
    /// colliders as well as the core's, land on the core rather than on the
    /// spent altar. At zero the core explodes and is switched off, and
    /// the altar goes dark: glow parts back to their own materials, rune
    /// circle erased. <see cref="RestoreCore"/> brings it back whole and
    /// relights the altar. Before it is armed the core is just part of the
    /// altar, and blows on it count toward lighting. Put no EnemyHealth on
    /// the core yourself.
    ///
    /// While lit with its core standing the altar <see cref="IsEmpowering"/>:
    /// what a <see cref="WitchHealPattern"/> draws on. An altar with no core
    /// empowers for as long as it is lit.
    /// </remarks>
    [RequireComponent(typeof(EnemyHealth))]
    public sealed class Altar : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Renderers that change to the lit material, usually a child object such as a gem or brazier. Every material slot on each is replaced.")]
        private Renderer[] glowParts = new Renderer[0];

        [SerializeField]
        [Tooltip("Material the glow parts change to once the altar is lit, e.g. RedGlow.")]
        private Material litMaterial;

        [Header("Rune circle")]
        [SerializeField]
        [Tooltip("Draw a rune circle on the floor around the altar, written in as it loses health and lit when it lights.")]
        private bool drawRuneCircle = true;

        [SerializeField] private RuneCircle.Settings runeCircle = new RuneCircle.Settings();

        [Header("Summoning")]
        [SerializeField]
        [Tooltip("Where the arc of runes to the summoning altar starts. Empty starts it just above the top of the altar.")]
        private Transform arcOrigin;

        [Header("Core")]
        [SerializeField]
        [Tooltip("Child object the player breaks once the boss fight starts. Needs a collider; do not put an EnemyHealth on it. Empty: the altar cannot be broken.")]
        private Transform core;

        [SerializeField]
        [Tooltip("The core's health, as a multiple of the altar's max health.")]
        [Min(0.01f)]
        private float coreHealthMultiplier = 1f;

        [SerializeField]
        [Tooltip("Burst where the core explodes.")]
        private EnergyBurst.Settings coreExplosion = new EnergyBurst.Settings
        {
            flashSize = 3f,
            flashTime = 0.3f,
            sparkCount = 28,
            sparkSize = 0.6f,
            sparkSpeed = new Vector2(8f, 18f),
            sparkLife = 0.6f,
        };

        [SerializeField]
        [Tooltip("Camera shake when the core explodes: strength, then seconds.")]
        private Vector2 coreExplosionShake = new Vector2(0.4f, 0.3f);

        [SerializeField]
        [Tooltip("Burst where the core reforms when restored.")]
        private EnergyBurst.Settings coreRestore = new EnergyBurst.Settings
        {
            flashSize = 1.5f,
            flashTime = 0.4f,
            sparkCount = 12,
        };

        private static readonly List<Altar> all = new List<Altar>();

        private EnemyHealth health;
        private EnemyHealth coreHealth;
        private RuneCircle circle;
        private Material[][] unlitMaterials;

        /// <summary>Every enabled altar.</summary>
        public static IReadOnlyList<Altar> All => all;

        /// <summary>True once the altar's health has been depleted.</summary>
        public bool IsLit { get; private set; }

        public EnemyHealth Health => health;

        /// <summary>The core's own health once armed, or null before then or with no core.</summary>
        public EnemyHealth CoreHealth => coreHealth;

        /// <summary>True once the core has been armed and since broken, until it is restored.</summary>
        public bool IsCoreBroken { get; private set; }

        /// <summary>Lit, with its core standing: what a <see cref="WitchHealPattern"/> draws on.</summary>
        public bool IsEmpowering => IsLit && !IsCoreBroken;

        /// <summary>The middle of the core, or the arc origin with no core.</summary>
        public Vector3 CorePoint
        {
            get
            {
                if (core == null)
                {
                    return ArcOrigin;
                }

                Renderer[] renderers = core.GetComponentsInChildren<Renderer>();

                if (renderers.Length == 0)
                {
                    return core.position;
                }

                Bounds bounds = renderers[0].bounds;

                foreach (Renderer part in renderers)
                {
                    bounds.Encapsulate(part.bounds);
                }

                return bounds.center;
            }
        }

        /// <summary>Where an arc of runes from this altar starts: the arc origin, or just above the top of the altar.</summary>
        public Vector3 ArcOrigin
        {
            get
            {
                if (arcOrigin != null)
                {
                    return arcOrigin.position;
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

        /// <summary>Raised once, when the altar lights.</summary>
        public event Action<Altar> Lit;

        /// <summary>Raised whenever the core is broken.</summary>
        public event Action<Altar> CoreBroken;

        /// <summary>Raised whenever a broken core is restored.</summary>
        public event Action<Altar> CoreRestored;

        private void Awake()
        {
            health = GetComponent<EnemyHealth>();
            health.DestroyOnDeath = false;

            if (glowParts.Length == 0)
            {
                Debug.LogWarning($"[Altar] {name} has no glow parts assigned, so lighting it will show nothing.", this);
            }

            unlitMaterials = new Material[glowParts.Length][];

            for (int i = 0; i < glowParts.Length; i++)
            {
                unlitMaterials[i] = glowParts[i] != null ? glowParts[i].sharedMaterials : null;
            }

            if (core == transform)
            {
                Debug.LogWarning($"[Altar] {name}'s core is the altar itself; it must be a child object.", this);
                core = null;
            }
        }

        private void Start()
        {
            EnsureCircle();
        }

        private void OnEnable()
        {
            all.Add(this);
            health.Died += OnDied;
            health.Damaged += OnDamaged;
        }

        private void OnDisable()
        {
            all.Remove(this);
            health.Died -= OnDied;
            health.Damaged -= OnDamaged;
        }

        private void OnDestroy()
        {
            if (coreHealth != null)
            {
                coreHealth.Died -= OnCoreDied;
            }
        }

        private void OnDamaged(EnemyHealth damaged, float amount)
        {
            EnsureCircle();

            if (circle != null)
            {
                circle.Charge(1f - health.CurrentHealth / health.MaxHealth);
            }
        }

        /// <summary>Makes the circle, empty, if it is wanted and not made yet.</summary>
        private void EnsureCircle()
        {
            if (drawRuneCircle && circle == null)
            {
                circle = RuneCircle.Create(RuneCircle.FloorBelow(transform.position, transform), runeCircle, false);
            }
        }

        private void OnDied(EnemyHealth died)
        {
            Light();
        }

        /// <summary>Lights the altar, if it is not lit already.</summary>
        public void Light()
        {
            if (IsLit)
            {
                return;
            }

            IsLit = true;
            Glow(true);
            Lit?.Invoke(this);
        }

        /// <summary>
        /// Makes the core breakable: gives it its own health, the altar's
        /// times the multiplier, full. Called when the boss fight starts.
        /// Does nothing without a core, or once armed.
        /// </summary>
        public void ArmCore()
        {
            if (core == null)
            {
                Debug.LogWarning($"[Altar] {name} has no core assigned, so it cannot be broken in the fight.", this);
                return;
            }

            if (coreHealth != null)
            {
                return;
            }

            if (core.GetComponentInChildren<Collider>() == null)
            {
                Debug.LogWarning($"[Altar] {name}'s core has no collider, so nothing can hit it.", this);
            }

            coreHealth = core.GetComponent<EnemyHealth>();

            if (coreHealth == null)
            {
                coreHealth = core.gameObject.AddComponent<EnemyHealth>();
            }

            coreHealth.DestroyOnDeath = false;
            coreHealth.SetMaxHealth(health.MaxHealth * coreHealthMultiplier);
            coreHealth.Died += OnCoreDied;

            // Blows anywhere on the altar now land on the core.
            health.RedirectTo = coreHealth;
        }

        /// <summary>
        /// Brings a broken core back, at full health, and relights the altar.
        /// Does nothing unless the core is broken.
        /// </summary>
        public void RestoreCore()
        {
            if (!IsCoreBroken)
            {
                return;
            }

            IsCoreBroken = false;
            core.gameObject.SetActive(true);
            coreHealth.SetMaxHealth(health.MaxHealth * coreHealthMultiplier);
            EnergyBurst.Create(CorePoint, coreRestore);

            if (IsLit)
            {
                Glow(true);
            }

            CoreRestored?.Invoke(this);
        }

        private void OnCoreDied(EnemyHealth died)
        {
            if (IsCoreBroken)
            {
                return;
            }

            IsCoreBroken = true;
            EnergyBurst.Create(CorePoint, coreExplosion);

            if (coreExplosionShake.x > 0f && coreExplosionShake.y > 0f)
            {
                TopDownCamera.Shake(coreExplosionShake.x, coreExplosionShake.y);
            }

            // Off, not destroyed, so it can be restored.
            core.gameObject.SetActive(false);
            Glow(false);
            CoreBroken?.Invoke(this);
        }

        /// <summary>Lights the glow parts and the circle, or puts them out and erases the circle.</summary>
        private void Glow(bool on)
        {
            for (int i = 0; i < glowParts.Length; i++)
            {
                Renderer part = glowParts[i];

                if (part == null)
                {
                    continue;
                }

                if (!on)
                {
                    part.sharedMaterials = unlitMaterials[i];
                    continue;
                }

                if (litMaterial == null)
                {
                    continue;
                }

                Material[] materials = part.sharedMaterials;

                for (int slot = 0; slot < materials.Length; slot++)
                {
                    materials[slot] = litMaterial;
                }

                part.sharedMaterials = materials;
            }

            if (!on)
            {
                if (circle != null)
                {
                    circle.Erase();
                    circle = null;
                }

                return;
            }

            EnsureCircle();

            if (circle != null)
            {
                circle.Ignite();
            }
        }
    }
}
