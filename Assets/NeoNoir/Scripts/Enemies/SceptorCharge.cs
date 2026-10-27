using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Power gathering at a point: streaks rushing in toward it from every
    /// side, and a red glow there that swells and pulses as it builds.
    /// </summary>
    /// <remarks>
    /// Follows the point without being parented to it, so the streaks keep
    /// flying straight in however the animation swings the sceptre. Moved in
    /// LateUpdate, after the animator has posed it.
    ///
    /// It builds over the charge time given to <see cref="Create"/>: the glow
    /// grows from nothing to <see cref="Settings.glowSize"/>, and the streaks
    /// come thicker and faster toward the end. It lasts until destroyed, and
    /// destroys itself if the point, or the enemy it was made for, goes away
    /// or dies.
    ///
    /// Each streak starts <see cref="Settings.lineDistance"/> out, flies in at
    /// <see cref="Settings.lineSpeed"/>, and shrinks into the centre as it
    /// arrives, so none ever pokes out the far side. Nothing fades, as with
    /// the other effects.
    /// </remarks>
    public sealed class SceptorCharge : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Glow")]
            [Tooltip("Size the glow reaches by the end of the charge.")]
            [Min(0f)] public float glowSize = 0.7f;

            [Tooltip("How far the glow pulses in and out, as a fraction of its size.")]
            [Range(0f, 1f)] public float glowPulse = 0.15f;

            [Tooltip("Pulses a second.")]
            [Min(0f)] public float glowPulseSpeed = 8f;

            [Header("Streaks")]
            [Tooltip("Streaks a second by the end of the charge. They start at a third of this.")]
            [Min(0f)] public float linesPerSecond = 30f;

            [Tooltip("How far out each streak starts.")]
            [Min(0.1f)] public float lineDistance = 2.5f;

            [Tooltip("Speed the streaks fly inward.")]
            [Min(0.1f)] public float lineSpeed = 6f;

            [Tooltip("Length of each streak.")]
            [Min(0.01f)] public float lineLength = 0.7f;

            [Tooltip("Thickness of each streak.")]
            [Min(0.005f)] public float lineWidth = 0.05f;

            [Tooltip("Colour of the glow and streaks. Above 1 blooms. Keep it strongly saturated.")]
            [ColorUsage(false, true)] public Color colour = new Color(3f, 0.15f, 0.1f, 1f);
        }

        private sealed class Line
        {
            public Transform transform;
            public Vector3 direction;
            public float distance;
        }

        private readonly List<Line> lines = new List<Line>();

        private Settings settings;
        private Transform point;
        private Enemy owner;
        private float chargeTime;
        private float age;
        private float lineDue;

        private Transform glow;
        private MaterialPropertyBlock block;

        /// <summary>
        /// Starts gathering at <paramref name="gatherPoint"/>, building to
        /// full over <paramref name="seconds"/>. Goes if
        /// <paramref name="enemy"/> dies.
        /// </summary>
        public static SceptorCharge Create(Transform gatherPoint, Enemy enemy, float seconds, Settings settings)
        {
            GameObject host = new GameObject("SceptorCharge");
            host.transform.position = gatherPoint.position;

            SceptorCharge charge = host.AddComponent<SceptorCharge>();
            charge.settings = settings;
            charge.point = gatherPoint;
            charge.owner = enemy;
            charge.chargeTime = Mathf.Max(0.01f, seconds);
            charge.block = new MaterialPropertyBlock();

            if (settings.glowSize > 0f)
            {
                charge.glow = charge.MakePiece("Glow", PrimitiveType.Sphere).transform;
                charge.glow.localScale = Vector3.zero;
            }

            return charge;
        }

        private GameObject MakePiece(string pieceName, PrimitiveType type)
        {
            GameObject piece = GameObject.CreatePrimitive(type);
            Destroy(piece.GetComponent<Collider>());

            piece.name = pieceName;
            piece.transform.SetParent(transform, false);

            MeshRenderer renderer = piece.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FlatMaterial.Shared;
            FlatMaterial.Tint(renderer, settings.colour, block);

            return piece;
        }

        private void LateUpdate()
        {
            if (point == null || owner == null || owner.Health.IsDead)
            {
                Destroy(gameObject);
                return;
            }

            float deltaTime = Time.deltaTime;
            age += deltaTime;
            transform.position = point.position;

            float built = Mathf.Clamp01(age / chargeTime);

            if (glow != null)
            {
                float pulse = 1f + settings.glowPulse * Mathf.Sin(age * Mathf.PI * 2f * settings.glowPulseSpeed);
                glow.localScale = Vector3.one * (settings.glowSize * built * pulse);
            }

            lineDue += settings.linesPerSecond * Mathf.Lerp(0.33f, 1f, built) * deltaTime;

            while (lineDue >= 1f)
            {
                lineDue -= 1f;
                lines.Add(new Line
                {
                    transform = MakePiece("Streak", PrimitiveType.Cube).transform,
                    direction = UnityEngine.Random.onUnitSphere,
                    distance = settings.lineDistance,
                });
            }

            // Faster toward the end, so the last streaks rush in.
            float speed = settings.lineSpeed * Mathf.Lerp(1f, 1.75f, built);

            for (int i = lines.Count - 1; i >= 0; i--)
            {
                Line line = lines[i];
                line.distance -= speed * deltaTime;

                // Distance is the streak's inner end; its tail trails one
                // length further out. The inner end stops at the centre, so
                // the streak shrinks into it as the tail catches up.
                float outer = line.distance + settings.lineLength;

                if (outer <= 0f)
                {
                    Destroy(line.transform.gameObject);
                    lines.RemoveAt(i);
                    continue;
                }

                float inner = Mathf.Max(0f, line.distance);
                float length = outer - inner;

                line.transform.position = transform.position + line.direction * ((inner + outer) * 0.5f);
                line.transform.rotation = Quaternion.LookRotation(line.direction);
                line.transform.localScale = new Vector3(settings.lineWidth, settings.lineWidth, length);
            }
        }
    }
}
