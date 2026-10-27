using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Dissolves an enemy into a storm of runes that swarms through the air to
    /// somewhere else, where it re-forms, standing on the floor.
    /// </summary>
    /// <remarks>
    /// In order, from <see cref="Teleport"/>:
    ///   1. The enemy dissolves away over <see cref="Settings.fadeOutTime"/>
    ///      into a <see cref="RuneSwarm"/>: a storm of runes bursting off it
    ///      that swarms through the air into the destination.
    ///   2. Once gone it is moved there, out of sight.
    ///   3. It re-forms over <see cref="Settings.fadeInTime"/>, finishing just
    ///      as the last rune arrives -- <see cref="Settings.travelTime"/>
    ///      after it finished dissolving.
    ///
    /// It lands standing exactly as high as it stood before: the bottom of
    /// the colliders on its root, measured at the start, ends up on the floor
    /// under the destination.
    ///
    /// Throughout, the enemy is invulnerable, every collider on it is off and
    /// its Rigidbody is kinematic, so nothing touches it and gravity has no
    /// say; all go back as they were once it has re-formed. It dissolves
    /// through _Fade, so a part whose material lacks it just switches off,
    /// and back on, at the halfway point.
    /// </remarks>
    public static class RuneBlink
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Seconds the enemy takes to dissolve into runes.")]
            [Min(0.01f)] public float fadeOutTime = 0.6f;

            [Tooltip("Seconds from fully dissolved to the last rune arriving at the destination.")]
            [Min(0f)] public float travelTime = 1.2f;

            [Tooltip("Seconds the enemy takes to re-form. It is whole just as the last rune arrives.")]
            [Min(0.01f)] public float fadeInTime = 0.5f;

            [Tooltip("What counts as floor at the destination. Leave the Player and Enemy layers out.")]
            public LayerMask groundMask = 1;

            [Tooltip("The storm of runes it dissolves into, which swarms to the destination.")]
            public RuneSwarm.Settings runes = new RuneSwarm.Settings();
        }

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        /// <summary>Dissolves <paramref name="enemy"/> into runes and re-forms it on the floor under <paramref name="destination"/>.</summary>
        public static IEnumerator Teleport(Enemy enemy, Vector3 destination, Settings settings)
        {
            Transform root = enemy.transform;
            Rigidbody body = enemy.GetComponent<Rigidbody>();
            bool wasKinematic = body != null && body.isKinematic;
            Vector3? velocityBefore = enemy.VelocityOverride;

            enemy.VelocityOverride = Vector3.zero;
            enemy.Health.Invulnerable = true;

            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }

                body.isKinematic = true;
            }

            float standingHeight = HeightAboveFeet(enemy);
            List<Collider> switchedOff = new List<Collider>();

            foreach (Collider part in enemy.GetComponentsInChildren<Collider>())
            {
                if (part.enabled)
                {
                    part.enabled = false;
                    switchedOff.Add(part);
                }
            }

            Vector3 floor = FindFloor(destination, settings.groundMask);
            Vector3 arrival = floor + Vector3.up * standingHeight;
            Vector3 startFeet = root.position + Vector3.down * standingHeight;

            float total = settings.fadeOutTime + settings.travelTime;
            RuneSwarm.Create(startFeet, floor, settings.fadeOutTime, total, settings.runes);

            Fade fade = new Fade(enemy);
            float clock = 0f;

            while (clock < settings.fadeOutTime)
            {
                yield return null;
                clock += Time.deltaTime;
                fade.Apply(1f - Mathf.Clamp01(clock / settings.fadeOutTime));
            }

            fade.Apply(0f);
            root.position = arrival;

            // Faces the player from the moment it starts to re-form.
            if (enemy.HasTarget)
            {
                Vector3 toTarget = enemy.Target.position - arrival;
                toTarget.y = 0f;

                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    enemy.TurnModelToward(toTarget, 360f);
                }
            }

            float formFrom = Mathf.Max(settings.fadeOutTime, total - settings.fadeInTime);

            while (clock < total)
            {
                yield return null;
                clock += Time.deltaTime;

                float formIn = total - formFrom;
                fade.Apply(formIn > 0f ? Mathf.Clamp01((clock - formFrom) / formIn) : 0f);
            }

            fade.Apply(1f);

            foreach (Collider part in switchedOff)
            {
                if (part != null)
                {
                    part.enabled = true;
                }
            }

            if (body != null)
            {
                body.isKinematic = wasKinematic;

                if (!wasKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                }
            }

            enemy.Health.Invulnerable = false;
            enemy.VelocityOverride = velocityBefore;
        }

        /// <summary>The renderers that were showing at the start, faded together.</summary>
        private sealed class Fade
        {
            private readonly List<Renderer> renderers = new List<Renderer>();
            private readonly List<bool> canFade = new List<bool>();
            private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

            public Fade(Enemy enemy)
            {
                foreach (Renderer part in enemy.GetComponentsInChildren<Renderer>())
                {
                    if (!part.enabled)
                    {
                        continue;
                    }

                    bool fades = true;

                    foreach (Material material in part.sharedMaterials)
                    {
                        if (material == null || !material.HasProperty(FadeId))
                        {
                            fades = false;
                            break;
                        }
                    }

                    renderers.Add(part);
                    canFade.Add(fades);
                }
            }

            public void Apply(float visibility)
            {
                for (int i = 0; i < renderers.Count; i++)
                {
                    Renderer part = renderers[i];

                    if (part == null)
                    {
                        continue;
                    }

                    if (!canFade[i])
                    {
                        part.enabled = visibility >= 0.5f;
                        continue;
                    }

                    part.GetPropertyBlock(block);
                    block.SetFloat(FadeId, visibility);
                    part.SetPropertyBlock(block);
                }
            }
        }

        /// <summary>
        /// How far the enemy's pivot is above the bottom of the solid
        /// colliders on its root, which are what it stands on. 0 if it has none.
        /// </summary>
        private static float HeightAboveFeet(Enemy enemy)
        {
            float lowest = float.MaxValue;

            foreach (Collider part in enemy.GetComponents<Collider>())
            {
                if (part.enabled && !part.isTrigger)
                {
                    lowest = Mathf.Min(lowest, part.bounds.min.y);
                }
            }

            return lowest == float.MaxValue ? 0f : enemy.transform.position.y - lowest;
        }

        /// <summary>The floor under <paramref name="point"/>, or the point itself if there is none.</summary>
        private static Vector3 FindFloor(Vector3 point, LayerMask groundMask)
        {
            // From well above, so a raised floor still counts. The enemy's own
            // colliders are already off, so it never finds itself.
            if (Physics.Raycast(point + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f,
                                groundMask, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }

            return point;
        }
    }
}
