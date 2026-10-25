using System;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A short-lived shell around the player that nullifies every
    /// <see cref="EnemyProjectile"/> touching it while it guards. Luna's
    /// secondary ability.
    /// </summary>
    /// <remarks>
    /// Added at spawn time to whatever art the barrier should look like
    /// (HexBarrier.prefab), so the prefab stays pure art with no behaviour baked
    /// into it, the same as the rising cubes. Any mesh works; on NeoNoir/Barrier
    /// it also flickers in panel by panel and lights up where it is hit.
    ///
    /// The catch is not a physics trigger. Projectiles ask
    /// <see cref="TryCatch"/> with the path they are about to travel this
    /// frame, and the barrier answers with a swept sphere test, so a fast
    /// bullet can never step over the shell between two frames.
    ///
    /// Guards for exactly <see cref="Settings.guardTime"/>, then dissolves and
    /// destroys itself. <see cref="Finished"/> is raised however it goes, so
    /// whoever spawned it is never left waiting.
    ///
    /// Follows its target without being parented to it, and never rotates: the
    /// player turns to face its movement, and the panels would spin with it.
    /// </remarks>
    public sealed class ParryBarrier : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            [Tooltip("Seconds the barrier nullifies projectiles.")]
            [Min(0.01f)] public float guardTime = 0.5f;

            [Tooltip("Size the barrier is drawn at, as a multiple of the radius it catches projectiles at. Only changes the look; sparks start on the drawn shell.")]
            [Min(0.01f)] public float visualScale = 1f;

            [Tooltip("Seconds the panels take to flicker in. Projectiles are caught from the very first frame regardless.")]
            [Min(0f)] public float appearTime = 0.06f;

            [Tooltip("Size the barrier pops in from, as a fraction of its full radius.")]
            [Range(0f, 1f)] public float popScale = 0.85f;

            [Tooltip("Seconds the barrier takes to dissolve once the guard is over. Nothing is caught while it dissolves.")]
            [Min(0f)] public float fadeTime = 0.15f;

            [Tooltip("Seconds a parried projectile lights up the panels around where it hit.")]
            [Min(0f)] public float hitFlashTime = 0.2f;
        }

        private static readonly int RevealId = Shader.PropertyToID("_Reveal");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int HitId = Shader.PropertyToID("_Hit");

        private static readonly List<ParryBarrier> active = new List<ParryBarrier>();

        private Settings settings;
        private Transform follow;
        private Vector3 offset;
        private float radius;
        private float age;

        private Vector3 hitDirection = Vector3.up;
        private float hitAge = float.PositiveInfinity;

        private Renderer[] renderers;
        private MaterialPropertyBlock block;
        private bool finished;

        /// <summary>
        /// Raised for each projectile nullified, with the point on the shell it
        /// hit, the shell's outward normal there, and the velocity it came in at.
        /// </summary>
        public event Action<Vector3, Vector3, Vector3> Parried;

        /// <summary>Raised once, when the barrier is gone for good.</summary>
        public event Action<ParryBarrier> Finished;

        /// <summary>True while projectiles touching the barrier are nullified.</summary>
        public bool IsGuarding => settings != null && age < settings.guardTime;

        public float Radius => radius;

        /// <summary>World centre of the shell this frame.</summary>
        public Vector3 Centre => follow != null ? follow.position + offset : transform.position;

        /// <summary>
        /// Starts guarding around <paramref name="target"/>, centred
        /// <paramref name="centreOffset"/> from its pivot.
        /// </summary>
        public void Play(Transform target, Vector3 centreOffset, float shellRadius, Settings playSettings)
        {
            settings = playSettings;
            follow = target;
            offset = centreOffset;
            radius = Mathf.Max(0.01f, shellRadius);
            age = 0f;
            hitAge = float.PositiveInfinity;

            transform.SetPositionAndRotation(Centre, Quaternion.identity);
            Apply();
        }

        /// <summary>
        /// Checks a projectile's movement this frame against every guarding
        /// barrier. If one catches it, that barrier reacts and this returns
        /// true: the projectile must remove itself without dealing damage.
        /// </summary>
        /// <param name="from">Where the projectile is now.</param>
        /// <param name="to">Where it is about to move to.</param>
        /// <param name="projectileRadius">Its own size, added to the shell's.</param>
        /// <param name="velocity">Its velocity, passed on to the spark effect.</param>
        public static bool TryCatch(Vector3 from, Vector3 to, float projectileRadius, Vector3 velocity)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                ParryBarrier barrier = active[i];

                if (barrier.IsGuarding && barrier.Intersects(from, to, projectileRadius, out Vector3 normal))
                {
                    barrier.Catch(normal, velocity);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Swept sphere test. Anything already inside counts too -- a shot
        /// that was point blank when the barrier went up is still parried.
        /// </summary>
        private bool Intersects(Vector3 from, Vector3 to, float projectileRadius, out Vector3 normal)
        {
            Vector3 centre = Centre;
            float reach = radius + Mathf.Max(0f, projectileRadius);

            Vector3 start = from - centre;
            normal = start.sqrMagnitude > 0.000001f ? start.normalized : Vector3.up;

            if (start.sqrMagnitude <= reach * reach)
            {
                return true;
            }

            Vector3 path = to - from;
            float a = Vector3.Dot(path, path);

            if (a < 0.0000001f)
            {
                return false;
            }

            float b = 2f * Vector3.Dot(path, start);
            float c = start.sqrMagnitude - reach * reach;
            float discriminant = b * b - 4f * a * c;

            if (discriminant < 0f)
            {
                return false;
            }

            float t = (-b - Mathf.Sqrt(discriminant)) / (2f * a);

            if (t < 0f || t > 1f)
            {
                return false;
            }

            normal = (start + path * t).normalized;
            return true;
        }

        private void Catch(Vector3 normal, Vector3 velocity)
        {
            hitDirection = normal;
            hitAge = 0f;

            // On the drawn shell rather than the catch radius, so the sparks
            // come off the surface the player can see.
            Parried?.Invoke(Centre + normal * (radius * settings.visualScale), normal, velocity);
        }

        private void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>(true);
            block = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
        }

        private void OnDestroy()
        {
            if (finished)
            {
                return;
            }

            finished = true;
            Finished?.Invoke(this);
        }

        private void LateUpdate()
        {
            // LateUpdate for the same reason as the blades: the player's
            // interpolated position is final by now.
            if (settings == null || follow == null)
            {
                Destroy(gameObject);
                return;
            }

            float deltaTime = Time.deltaTime;
            age += deltaTime;
            hitAge += deltaTime;

            if (age >= settings.guardTime + settings.fadeTime)
            {
                Destroy(gameObject);
                return;
            }

            transform.SetPositionAndRotation(Centre, Quaternion.identity);
            Apply();
        }

        private void Apply()
        {
            float appear = settings.appearTime > 0f ? Mathf.Clamp01(age / settings.appearTime) : 1f;
            float eased = 1f - (1f - appear) * (1f - appear) * (1f - appear);
            transform.localScale = Vector3.one * (radius * settings.visualScale * Mathf.Lerp(settings.popScale, 1f, eased));

            float fade = settings.fadeTime > 0f
                ? 1f - Mathf.Clamp01((age - settings.guardTime) / settings.fadeTime)
                : (age < settings.guardTime ? 1f : 0f);

            float flash = settings.hitFlashTime > 0f ? 1f - Mathf.Clamp01(hitAge / settings.hitFlashTime) : 0f;

            foreach (Renderer target in renderers)
            {
                if (target == null)
                {
                    continue;
                }

                target.GetPropertyBlock(block);
                block.SetFloat(RevealId, appear);
                block.SetFloat(FadeId, fade);
                block.SetVector(HitId, new Vector4(hitDirection.x, hitDirection.y, hitDirection.z, flash));
                target.SetPropertyBlock(block);
            }
        }
    }
}
