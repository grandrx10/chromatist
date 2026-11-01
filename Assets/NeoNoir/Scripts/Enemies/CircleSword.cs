using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One of the swords of <see cref="LunaSwordCirclePattern"/>: it rises
    /// out of the floor handle first in a spray of dust and hangs there,
    /// white, until its turn, then takes on a colour of its own, tips its
    /// point over to aim level at the player, and fires a laser of that
    /// colour before fading away.
    /// </summary>
    /// <remarks>
    /// In order, its times counted from when it was made:
    ///   1. Rise: from fully under the floor up to hang with its middle at
    ///      <see cref="Settings.hoverHeight"/>, point down, over
    ///      <see cref="Settings.riseTime"/>, dust thrown up where it breaks
    ///      the floor all the way.
    ///   2. Waits, white, until its activation time.
    ///   3. Activation: over <see cref="Settings.turnTime"/> it turns its
    ///      colour and tips from point down to point level, the point
    ///      following the player round. It keeps following until
    ///      <see cref="Settings.trackTime"/> after activation, then holds still.
    ///      From activation it glistens in its colour, a
    ///      <see cref="BladeGlisten"/> building to the laser firing.
    ///   4. <see cref="Settings.holdTime"/> later a <see cref="SweepBeam"/>
    ///      of its colour fires from its point along where it aims, for
    ///      <see cref="Settings.fireTime"/>.
    ///   5. It fades away over <see cref="Settings.fadeTime"/> and is gone.
    /// A sword made not to track instead tips its point inward, toward the
    /// middle of its ring, and fires there.
    /// If its owner dies, is destroyed or is disabled first, any laser stops
    /// and it fades away at once.
    ///
    /// The sword model must stand upright, point down, like the swords of
    /// <see cref="LunaSwordRainPattern"/>, with NeoNoir/Flat materials so it
    /// takes the colour. It turns about the middle of its bounds; how far its
    /// point reaches from there is measured, so the laser starts at the point.
    /// Materials with _Fade dissolve away; others shrink.
    /// </remarks>
    public sealed class CircleSword : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Sword")]
            [Tooltip("The sword model, upright, point down, with NeoNoir/Flat materials. Empty uses a plain box.")]
            public GameObject swordPrefab;

            [Tooltip("Multiplies the prefab's own scale.")]
            [Min(0.01f)] public float swordScale = 1f;

            [Tooltip("Colour the swords rise in and keep until they activate. Above 1 blooms.")]
            [ColorUsage(false, true)] public Color restColour = new Color(2f, 2f, 2f, 1f);

            [Tooltip("Height above the floor of the swords' middles, so of the lasers. About the player's chest.")]
            public float hoverHeight = 1.4f;

            [Header("Rise")]
            [Tooltip("Seconds to rise out of the floor.")]
            [Min(0.01f)] public float riseTime = 0.8f;

            [Tooltip("Dust puffs a second thrown up round each sword as it rises.")]
            [Min(0f)] public float dustPerSecond = 40f;

            [Tooltip("Dust puffs thrown up as each sword first breaks the floor.")]
            [Min(0)] public int dustOnBreak = 12;

            [Tooltip("How far out from the blade the puffs start.")]
            [Min(0f)] public float dustRadius = 0.3f;

            [Tooltip("Speed the puffs fly outward.")]
            [Min(0f)] public float dustSpeed = 2.5f;

            public DustTrail.Settings dust = new DustTrail.Settings
            {
                size = 0.3f,
                lifetime = 0.6f,
                rise = 1.2f,
                colour = new Color(0.8f, 0.8f, 0.8f),
            };

            [Header("Activation")]
            [Tooltip("Seconds to tip from point down to point level. The change of colour takes the Glisten's Glow Fade Time.")]
            [Min(0.01f)] public float turnTime = 0.25f;

            [Tooltip("Seconds from activation the point follows the player for.")]
            [Min(0f)] public float trackTime = 0.7f;

            [Tooltip("Seconds from the point stopping to the laser firing.")]
            [Min(0f)] public float holdTime = 0.3f;

            [Tooltip("Seconds the laser fires for.")]
            [Min(0f)] public float fireTime = 0.3f;

            [Tooltip("Seconds to fade away once the laser stops.")]
            [Min(0.01f)] public float fadeTime = 0.4f;

            [Tooltip("The glisten from activation to the laser firing. Its Colour is replaced by the sword's; its glow brings the sword from white to that colour.")]
            public BladeGlisten.Settings glisten = new BladeGlisten.Settings { glowFadeTime = 0.25f };

            [Header("Laser")]
            [Tooltip("The laser. Its Colour is replaced by the sword's.")]
            public SweepBeam.Settings laser = new SweepBeam.Settings
            {
                length = 60f,
                radius = 0.35f,
                singleHit = true,
                hitDamage = 20f,
            };
        }

        private enum Stage { Rising, Waiting, Aiming, Firing, Fading }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private readonly List<Renderer> renderers = new List<Renderer>();

        private Settings settings;
        private Enemy owner;
        private Transform art;
        private DustTrail.Cloud dust;
        private SweepBeam beam;
        private BladeGlisten shine;
        private MaterialPropertyBlock block;
        private Color colour;
        private Color glint;
        private Vector3 home;
        private Vector3 sunk;
        private Vector3 floorPoint;
        private Vector3 fullScale;
        private float restYaw;
        private float aimYaw;
        private float tipReach;
        private float activateAt;
        private float age;
        private float stageAge;
        private float dustWait;
        private bool hasFade;
        private bool tracks;
        private Stage stage;

        /// <summary>
        /// A sword rising out of the floor at <paramref name="floor"/>,
        /// facing out along <paramref name="outward"/>, that activates
        /// <paramref name="activateAt"/> seconds from now in
        /// <paramref name="swordColour"/>, glistening in
        /// <paramref name="glistenColour"/>, aiming at
        /// <paramref name="owner"/>'s target if it <paramref name="tracks"/>,
        /// otherwise back in along <paramref name="outward"/>.
        /// </summary>
        public static CircleSword Create(Enemy owner, Vector3 floor, Vector3 outward, float activateAt,
                                         Color swordColour, Color glistenColour, bool tracks, Settings settings)
        {
            CircleSword sword = new GameObject("CircleSword").AddComponent<CircleSword>();
            sword.tracks = tracks;
            sword.Begin(owner, floor, outward, activateAt, swordColour, glistenColour, settings);
            return sword;
        }

        private void Begin(Enemy enemy, Vector3 floor, Vector3 outward, float activation, Color swordColour,
                           Color glistenColour, Settings swordSettings)
        {
            owner = enemy;
            settings = swordSettings;
            activateAt = activation;
            colour = swordColour;
            glint = glistenColour;
            floorPoint = floor;
            restYaw = Quaternion.LookRotation(outward, Vector3.up).eulerAngles.y;
            aimYaw = tracks ? restYaw : Mathf.Repeat(restYaw + 180f, 360f);
            block = new MaterialPropertyBlock();

            MakeArt();

            home = floor + Vector3.up * settings.hoverHeight;
            sunk = floor + Vector3.down * (tipReach + 0.1f);
            transform.SetPositionAndRotation(sunk, Quaternion.Euler(0f, restYaw, 0f));

            dust = DustTrail.Cloud.Create("CircleSwordDust");
            Paint(1f);
            stage = Stage.Rising;
        }

        private void MakeArt()
        {
            GameObject made;

            if (settings.swordPrefab != null)
            {
                made = Instantiate(ArtTemplate.Of(settings.swordPrefab));
                fullScale = settings.swordPrefab.transform.localScale * settings.swordScale;
            }
            else
            {
                made = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(made.GetComponent<Collider>());
                made.GetComponent<MeshRenderer>().sharedMaterial = FlatMaterial.Shared;
                fullScale = new Vector3(0.4f, 3f, 0.12f) * settings.swordScale;
            }

            made.name = "Art";
            art = made.transform;
            art.SetParent(transform, false);
            art.localPosition = Vector3.zero;
            art.localRotation = settings.swordPrefab != null ? settings.swordPrefab.transform.rotation : Quaternion.identity;
            art.localScale = fullScale;

            made.GetComponentsInChildren(true, renderers);

            foreach (Renderer part in renderers)
            {
                part.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hasFade |= part.sharedMaterial != null && part.sharedMaterial.HasProperty(FadeId);
            }

            // Centred on its bounds, so it turns about its middle, and the
            // point reaches half its height from there.
            if (renderers.Count > 0)
            {
                Bounds bounds = renderers[0].bounds;

                for (int i = 1; i < renderers.Count; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                art.position -= bounds.center - transform.position;
                tipReach = bounds.extents.y;
            }
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            age += deltaTime;
            stageAge += deltaTime;

            if (stage != Stage.Fading && OwnerGone())
            {
                StartFading();
            }

            switch (stage)
            {
                case Stage.Rising:
                    Rise(deltaTime);
                    break;

                case Stage.Waiting:
                    if (age >= activateAt)
                    {
                        Activate();
                    }

                    break;

                case Stage.Aiming:
                    Aim();
                    break;

                case Stage.Firing:
                    Fire();
                    break;

                case Stage.Fading:
                    Fade();
                    break;
            }
        }

        private void Rise(float deltaTime)
        {
            float t = Mathf.Clamp01(stageAge / settings.riseTime);
            float eased = 1f - (1f - t) * (1f - t);
            transform.position = Vector3.Lerp(sunk, home, eased);

            if (stageAge - deltaTime <= 0f)
            {
                Puff(settings.dustOnBreak, 1.5f);
            }

            dustWait -= deltaTime;

            while (settings.dustPerSecond > 0f && dustWait <= 0f)
            {
                dustWait += 1f / settings.dustPerSecond;
                Puff(1, 1f);
            }

            if (t >= 1f)
            {
                if (age >= activateAt)
                {
                    Activate();
                }
                else
                {
                    Advance(Stage.Waiting);
                }
            }
        }

        private void Activate()
        {
            BladeGlisten.Settings glisten = JsonUtility.FromJson<BladeGlisten.Settings>(JsonUtility.ToJson(settings.glisten));
            glisten.colour = glint;
            shine = BladeGlisten.Begin(art, glisten, settings.trackTime + settings.holdTime);
            Advance(Stage.Aiming);
        }

        private void Aim()
        {
            if (tracks && stageAge <= settings.trackTime)
            {
                Track();
            }

            shine.Tick(stageAge);

            float turned = Smooth(stageAge / settings.turnTime);
            transform.rotation = Quaternion.Slerp(Quaternion.Euler(0f, restYaw, 0f), Pointing(), turned);

            if (stageAge >= settings.trackTime + settings.holdTime)
            {
                transform.rotation = Pointing();

                SweepBeam.Settings laser = JsonUtility.FromJson<SweepBeam.Settings>(JsonUtility.ToJson(settings.laser));
                laser.colour = colour;
                beam = SweepBeam.Fire(Tip(), Direction(), owner != null ? owner.transform : null, laser);
                Advance(Stage.Firing);
            }
        }

        private void Fire()
        {
            shine.Tick(settings.trackTime + settings.holdTime + stageAge);

            if (beam != null)
            {
                beam.Aim(Tip(), Direction());
            }

            if (stageAge >= settings.fireTime)
            {
                StartFading();
            }
        }

        private void Fade()
        {
            float left = 1f - Mathf.Clamp01(stageAge / settings.fadeTime);

            if (hasFade)
            {
                Paint(left);
            }
            else
            {
                art.localScale = fullScale * left;
            }

            if (left <= 0f)
            {
                Destroy(gameObject);
            }
        }

        private void StartFading()
        {
            if (beam != null)
            {
                beam.Stop();
                beam = null;
            }

            Advance(Stage.Fading);
        }

        private void Advance(Stage next)
        {
            stage = next;
            stageAge = 0f;
        }

        /// <summary>Turns the point toward the player, level.</summary>
        private void Track()
        {
            Transform target = owner != null ? owner.Target : null;

            if (target == null)
            {
                return;
            }

            Vector3 toward = target.position - transform.position;
            toward.y = 0f;

            if (toward.sqrMagnitude > 0.0001f)
            {
                aimYaw = Quaternion.LookRotation(toward, Vector3.up).eulerAngles.y;
            }
        }

        // Point down tipped forward 90 degrees: point level along the yaw.
        private Quaternion Pointing() => Quaternion.Euler(0f, aimYaw, 0f) * Quaternion.Euler(-90f, 0f, 0f);

        private Vector3 Direction() => Quaternion.Euler(0f, aimYaw, 0f) * Vector3.forward;

        private Vector3 Tip() => transform.position + transform.rotation * Vector3.down * tipReach;

        /// <summary>
        /// Sets how far faded in it is, and, until it glistens, its colour.
        /// Once it glistens, the glow holds the colour, and would put back a
        /// colour set under it.
        /// </summary>
        private void Paint(float visibility)
        {
            foreach (Renderer part in renderers)
            {
                if (part == null)
                {
                    continue;
                }

                part.GetPropertyBlock(block);

                if (shine == null)
                {
                    block.SetColor(FlatMaterial.BaseColorId, settings.restColour);
                }

                if (hasFade)
                {
                    block.SetFloat(FadeId, visibility);
                }

                part.SetPropertyBlock(block);
            }
        }

        /// <summary>Dust flung out round the blade where it comes out of the floor.</summary>
        private void Puff(int count, float strength)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 velocity = outward * (settings.dustSpeed * strength * UnityEngine.Random.Range(0.5f, 1f))
                                   + Vector3.up * (settings.dust.rise * strength * UnityEngine.Random.Range(0.6f, 1.2f));
                dust.Emit(floorPoint + outward * settings.dustRadius, velocity, settings.dust);
            }
        }

        private bool OwnerGone()
        {
            return owner == null || owner.Health.IsDead || !owner.isActiveAndEnabled;
        }

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private void OnDestroy()
        {
            // The laser and dust are their own objects: never left behind.
            if (beam != null)
            {
                beam.Stop();
            }

            if (dust != null)
            {
                dust.Release();
            }
        }
    }
}
