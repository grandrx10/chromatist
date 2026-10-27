using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A tinted hex shell around an enemy that makes it invulnerable while
    /// it stands, as the witch has while she heals.
    /// </summary>
    /// <remarks>
    /// Added at spawn time to whatever art it should look like
    /// (HexBarrier.prefab), as <see cref="ParryBarrier"/> is, but catches
    /// nothing: blows on the enemy are simply ignored through
    /// <see cref="EnemyHealth.Invulnerable"/>. On NeoNoir/Barrier it flickers
    /// in panel by panel and dissolves away, tinted by
    /// <see cref="Settings.colour"/> through the material's _BaseColor.
    ///
    /// Stands until <see cref="Dismiss"/>, then dissolves and destroys
    /// itself. The invulnerability ends the moment it is dismissed, or if the
    /// shield is destroyed any other way. Follows the enemy's body without
    /// being parented to it, and never rotates.
    /// </remarks>
    public sealed class HexShield : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Gap between the enemy's body and the shell. Fitted round its colliders.")]
            [Min(0f)] public float padding = 0.5f;

            [Tooltip("HDR: above 1 blooms. Replaces the material's Base Colour.")]
            [ColorUsage(false, true)] public Color colour = new Color(4f, 0.25f, 0.2f, 1f);

            [Tooltip("Seconds the panels take to flicker in.")]
            [Min(0f)] public float appearTime = 0.2f;

            [Tooltip("Size the shell pops in from, as a fraction of its full size.")]
            [Range(0f, 1f)] public float popScale = 0.85f;

            [Tooltip("Seconds the shell takes to dissolve once dismissed.")]
            [Min(0f)] public float fadeTime = 0.3f;
        }

        private static readonly int RevealId = Shader.PropertyToID("_Reveal");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int ColourId = Shader.PropertyToID("_BaseColor");

        private Settings settings;
        private EnemyHealth guarded;
        private Vector3 offset;
        private float radius;
        private float age;
        private float dismissedAt = -1f;

        private Renderer[] renderers;
        private MaterialPropertyBlock block;

        /// <summary>
        /// Spawns <paramref name="prefab"/> -- or nothing to see, if empty --
        /// round <paramref name="health"/>'s body and makes it invulnerable.
        /// </summary>
        public static HexShield Raise(GameObject prefab, EnemyHealth health, Settings settings)
        {
            Bounds body = health.BodyBounds();
            GameObject host = prefab != null
                ? Instantiate(prefab, body.center, Quaternion.identity)
                : new GameObject("HexShield");

            HexShield shield = host.AddComponent<HexShield>();
            shield.Begin(health, body, settings);
            return shield;
        }

        /// <summary>Ends the invulnerability and dissolves the shell.</summary>
        public void Dismiss()
        {
            if (dismissedAt >= 0f)
            {
                return;
            }

            dismissedAt = age;
            Release();
        }

        private void Begin(EnemyHealth health, Bounds body, Settings shieldSettings)
        {
            settings = shieldSettings;
            guarded = health;
            offset = body.center - health.transform.position;
            radius = Mathf.Max(body.extents.x, body.extents.y, body.extents.z) + settings.padding;
            renderers = GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();

            // Only the art: nothing on it should be hit, or block anything.
            foreach (Collider solid in GetComponentsInChildren<Collider>(true))
            {
                solid.enabled = false;
            }

            guarded.Invulnerable = true;
            Apply();
        }

        private void LateUpdate()
        {
            if (settings == null || guarded == null)
            {
                Destroy(gameObject);
                return;
            }

            age += Time.deltaTime;

            if (dismissedAt >= 0f && age - dismissedAt >= settings.fadeTime)
            {
                Destroy(gameObject);
                return;
            }

            Apply();
        }

        private void Apply()
        {
            float appear = settings.appearTime > 0f ? Mathf.Clamp01(age / settings.appearTime) : 1f;
            float eased = 1f - (1f - appear) * (1f - appear) * (1f - appear);
            float fade = dismissedAt < 0f ? 1f
                : settings.fadeTime > 0f ? 1f - Mathf.Clamp01((age - dismissedAt) / settings.fadeTime) : 0f;

            transform.SetPositionAndRotation(guarded.transform.position + offset, Quaternion.identity);
            transform.localScale = Vector3.one * (radius * Mathf.Lerp(settings.popScale, 1f, eased));

            foreach (Renderer target in renderers)
            {
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(block);
                block.SetFloat(RevealId, appear);
                block.SetFloat(FadeId, fade);
                block.SetColor(ColourId, settings.colour);
                target.SetPropertyBlock(block);
            }
        }

        private void Release()
        {
            if (guarded != null)
            {
                guarded.Invulnerable = false;
            }
        }

        private void OnDestroy()
        {
            if (dismissedAt < 0f)
            {
                Release();
            }
        }
    }
}
