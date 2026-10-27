using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A potion conjured out of runes high over the arena, which drops
    /// straight down and shatters into a <see cref="PotionPool"/>.
    /// </summary>
    /// <remarks>
    /// In order, from <see cref="Conjure"/>:
    ///   1. Forming: for <see cref="Settings.formTime"/> seconds runes are
    ///      written on a shell round the spot and sink into it, while the
    ///      potion grows out of nothing in the middle, turning slowly.
    ///   2. Once whole it hangs for <see cref="Settings.hangTime"/>, then is
    ///      dropped as a <see cref="LobbedPotion"/>: it falls straight down,
    ///      hits whoever it lands on and shatters into a pool, exactly as a
    ///      thrown one does.
    ///
    /// The potion art comes from the <see cref="ArtPool"/> and goes back to
    /// it on shattering. Lives on its own once started, like a fired
    /// projectile, so it finishes even if whoever conjured it dies.
    /// </remarks>
    public sealed class RunePotion : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Forming")]
            [Tooltip("Seconds from the first rune to the potion being whole.")]
            [Min(0.1f)] public float formTime = 2f;

            [Tooltip("Seconds the whole potion hangs before it drops.")]
            [Min(0f)] public float hangTime = 0.25f;

            [Tooltip("Degrees per second the potion turns while it forms.")]
            public float formSpin = 90f;

            [Header("Runes")]
            [Tooltip("Runes written a second while the potion forms.")]
            [Min(0f)] public float runesPerSecond = 16f;

            [Tooltip("How far from the potion runes are written, before they sink in.")]
            [Min(0.1f)] public float shellRadius = 2f;

            [Tooltip("Shortest and longest seconds a rune takes to sink in.")]
            public Vector2 runeLife = new Vector2(0.5f, 0.8f);

            [Tooltip("Seconds each rune takes to be written.")]
            [Min(0.01f)] public float writeTime = 0.12f;

            [Tooltip("Smallest and largest height of a rune, in metres.")]
            public Vector2 runeSize = new Vector2(0.25f, 0.45f);

            [Tooltip("Width of the runes' lines, in metres.")]
            [Min(0.01f)] public float lineWidth = 0.04f;

            [Tooltip("HDR: above 1 blooms, which is what makes it glow.")]
            [ColorUsage(false, true)] public Color runeColour = new Color(3f, 0.15f, 0.1f, 1f);

            [Tooltip("Material to draw the runes with. Empty uses a shared NeoNoir/Flat one. Needs a _Fade property to dissolve.")]
            public Material runeMaterial;
        }

        private sealed class Rune
        {
            public Transform transform;
            public RuneDrawing drawing;
            public Vector3 direction;
            public float age;
            public float life;
            public float roll;
        }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private readonly List<Rune> runes = new List<Rune>();
        private readonly List<Renderer> art = new List<Renderer>();
        private readonly List<bool> artFades = new List<bool>();

        private Settings settings;
        private LobbedPotion.Settings potion;
        private PotionPool.Settings pool;
        private PotionShatter.Settings shatter;
        private Transform owner;
        private MaterialPropertyBlock block;

        private GameObject potionArt;
        private Vector3 artScale;
        private Vector3 floor;
        private float clock;
        private float owed;
        private int runeIndex;
        private bool dropped;

        /// <summary>
        /// Starts a potion of <paramref name="prefab"/> forming at
        /// <paramref name="position"/>, to drop onto <paramref name="floorPoint"/>
        /// below. <paramref name="conjurer"/>'s colliders are passed through.
        /// </summary>
        public static RunePotion Conjure(GameObject prefab, Vector3 position, Vector3 floorPoint, Transform conjurer,
                                         Settings settings, LobbedPotion.Settings potion,
                                         PotionPool.Settings pool, PotionShatter.Settings shatter)
        {
            GameObject host = new GameObject("RunePotion");
            host.transform.position = position;

            RunePotion conjured = host.AddComponent<RunePotion>();
            conjured.Begin(prefab, floorPoint, conjurer, settings, potion, pool, shatter);
            return conjured;
        }

        private void Begin(GameObject prefab, Vector3 floorPoint, Transform conjurer, Settings potionSettings,
                           LobbedPotion.Settings lobbed, PotionPool.Settings poolSettings,
                           PotionShatter.Settings shatterSettings)
        {
            settings = potionSettings;
            potion = lobbed;
            pool = poolSettings;
            shatter = shatterSettings;
            owner = conjurer;
            floor = floorPoint;
            block = new MaterialPropertyBlock();

            potionArt = ArtPool.Take(prefab, transform.position, UnityEngine.Random.rotationUniform);
            artScale = potionArt.transform.localScale;

            foreach (Renderer part in potionArt.GetComponentsInChildren<Renderer>())
            {
                bool fades = true;

                foreach (Material material in part.sharedMaterials)
                {
                    if (material == null || !material.HasProperty(FadeId))
                    {
                        fades = false;
                        break;
                    }
                }

                art.Add(part);
                artFades.Add(fades);
            }

            ShowArt(0f);
        }

        private void Update()
        {
            if (settings == null)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            clock += deltaTime;

            if (!dropped)
            {
                Form(deltaTime);
            }

            UpdateRunes();

            if (dropped && runes.Count == 0)
            {
                Destroy(gameObject);
            }
        }

        private void Form(float deltaTime)
        {
            float formed = Mathf.Clamp01(clock / settings.formTime);

            // Stops writing runes in time for the last to have sunk in as it is whole.
            if (clock < settings.formTime - settings.runeLife.y)
            {
                owed += settings.runesPerSecond * deltaTime;

                while (owed >= 1f)
                {
                    owed -= 1f;
                    Gather();
                }
            }

            potionArt.transform.Rotate(Vector3.up, settings.formSpin * deltaTime, Space.World);
            ShowArt(formed * formed * (3f - 2f * formed));

            if (clock >= settings.formTime + settings.hangTime)
            {
                Drop();
            }
        }

        private void Drop()
        {
            dropped = true;
            ShowArt(1f);
            LobbedPotion.Throw(potionArt, floor, owner, potion, pool, shatter);
            potionArt = null;
        }

        /// <summary>Grows and dissolves the potion in, 0 nothing to 1 whole.</summary>
        private void ShowArt(float amount)
        {
            potionArt.transform.localScale = artScale * Mathf.Max(0.01f, amount);

            for (int i = 0; i < art.Count; i++)
            {
                Renderer part = art[i];

                if (part == null)
                {
                    continue;
                }

                if (!artFades[i])
                {
                    part.enabled = amount >= 0.5f;
                    continue;
                }

                // Back to exactly 1 when whole: the art goes on to be reused.
                part.GetPropertyBlock(block);
                block.SetFloat(FadeId, amount);
                part.SetPropertyBlock(block);
            }
        }

        /// <summary>Writes a rune somewhere on the shell round the potion, to sink in.</summary>
        private void Gather()
        {
            GameObject host = new GameObject("Rune");
            host.transform.SetParent(transform, false);

            RuneDrawing drawing = new RuneDrawing(host, settings.runeMaterial != null ? settings.runeMaterial : FlatMaterial.Shared);
            drawing.AddRune(RuneGlyphs.Rune(runeIndex++), Vector3.zero, Vector3.forward,
                            UnityEngine.Random.Range(settings.runeSize.x, settings.runeSize.y),
                            settings.lineWidth, 0f, 1f);
            drawing.Progress = 0f;
            drawing.SetColour(settings.runeColour);

            runes.Add(new Rune
            {
                transform = host.transform,
                drawing = drawing,
                direction = UnityEngine.Random.onUnitSphere,
                life = Mathf.Max(0.05f, UnityEngine.Random.Range(settings.runeLife.x, settings.runeLife.y)),
                roll = UnityEngine.Random.Range(0f, 360f),
            });
        }

        /// <summary>
        /// Each on its way in from the shell to the middle, picking up speed
        /// as it goes, and dissolving into the potion at the end.
        /// </summary>
        private void UpdateRunes()
        {
            Quaternion facing = RuneDrawing.FacingCamera();
            float deltaTime = Time.deltaTime;

            for (int i = runes.Count - 1; i >= 0; i--)
            {
                Rune rune = runes[i];
                rune.age += deltaTime;

                if (rune.age >= rune.life)
                {
                    rune.drawing.Release();
                    Destroy(rune.transform.gameObject);
                    runes.RemoveAt(i);
                    continue;
                }

                float t = rune.age / rune.life;
                float reach = Mathf.Lerp(settings.shellRadius, 0.15f, t * t);

                // The drawing faces the camera along its local up, so a roll
                // about that tilts it on screen.
                rune.transform.SetPositionAndRotation(transform.position + rune.direction * reach,
                                                      facing * Quaternion.AngleAxis(rune.roll, Vector3.up));
                rune.transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.4f, t);
                rune.drawing.Progress = rune.age / settings.writeTime;
                rune.drawing.SetFade(1f - Mathf.InverseLerp(0.7f, 1f, t));
            }
        }

        private void OnDestroy()
        {
            foreach (Rune rune in runes)
            {
                rune.drawing.Release();
            }

            // Never got to drop: the art goes back unused, unless the whole scene is going.
            if (potionArt != null && gameObject.scene.isLoaded)
            {
                ShowArt(1f);
                ArtPool.Return(potionArt);
            }
        }
    }
}
