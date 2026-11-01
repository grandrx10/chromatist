using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A ring of the Puppeteer's eyes round a point, each looking straight
    /// outward: lowered from the sky, then made to fire steady beams and
    /// turn, so the beams sweep the arena like the spokes of a wheel.
    /// </summary>
    /// <remarks>
    /// The eyes are evenly spaced on a circle of the given radius, their
    /// middles the given height above the floor. They cannot be hurt, and
    /// are not linked to the boss: breaking them is not the point.
    ///
    /// An eye is a heavy thing to make, so <see cref="Lower"/> makes them one
    /// a frame rather than all at once, each starting down as it appears: the
    /// ring comes down as a quick cascade, and no one frame stalls.
    ///
    /// Runs on its own object, steered by <see cref="EyeCirclePattern"/>:
    /// <see cref="Lower"/>, then <see cref="Fire"/>, <see cref="Spin"/>, and
    /// at last <see cref="Lift"/>, which hauls every eye away and removes the
    /// ring. If the boss dies first, the ring lifts itself.
    /// </remarks>
    public sealed class EyeRing : MonoBehaviour
    {
        private readonly List<PuppeteerEye> eyes = new List<PuppeteerEye>();

        // Each eye's own height above its place, as it comes down.
        private readonly List<float> lifts = new List<float>();

        private Enemy owner;
        private PuppeteerEye prefab;
        private int count;
        private Vector3 centre;
        private float radius;
        private float height;
        private float angle;
        private float spinSpeed;
        private bool lifted;

        // Looking straight out, until FireAtPlayer frees them.
        private bool gazeHeld = true;

        /// <summary>True once every eye has been made and has finished coming down.</summary>
        public bool IsLowered { get; private set; }

        /// <summary>
        /// Sets up a ring of <paramref name="eyeCount"/> eyes from
        /// <paramref name="eyePrefab"/> round <paramref name="ringCentre"/> (on
        /// the floor), for <paramref name="boss"/>. None are made yet: call
        /// <see cref="Lower"/>.
        /// </summary>
        public static EyeRing Create(Enemy boss, PuppeteerEye eyePrefab, int eyeCount, Vector3 ringCentre,
                                     float ringRadius, float hangHeight)
        {
            EyeRing ring = new GameObject("EyeRing").AddComponent<EyeRing>();
            ring.owner = boss;
            ring.prefab = eyePrefab;
            ring.count = Mathf.Max(1, eyeCount);
            ring.centre = ringCentre;
            ring.radius = ringRadius;
            ring.height = hangHeight;
            ring.angle = Random.Range(0f, 360f);
            return ring;
        }

        /// <summary>
        /// Makes the eyes, one a frame, each lowered from
        /// <paramref name="dropHeight"/> above its place over
        /// <paramref name="time"/> seconds, fast at first and settling gently.
        /// </summary>
        public void Lower(float dropHeight, float time)
        {
            StartCoroutine(Lowering(dropHeight, Mathf.Max(0.01f, time)));
        }

        private IEnumerator Lowering(float dropHeight, float time)
        {
            List<float> started = new List<float>();
            float clock = 0f;

            while (true)
            {
                // One new eye a frame until all are made.
                if (eyes.Count < count)
                {
                    eyes.Add(MakeEye());
                    lifts.Add(dropHeight);
                    started.Add(clock);
                }

                bool settled = eyes.Count == count;

                for (int i = 0; i < eyes.Count; i++)
                {
                    float left = 1f - Mathf.Clamp01((clock - started[i]) / time);
                    lifts[i] = dropHeight * left * left;
                    settled &= left <= 0f;
                }

                Place();

                if (settled)
                {
                    break;
                }

                yield return null;
                clock += Time.deltaTime;
            }

            IsLowered = true;
        }

        private PuppeteerEye MakeEye()
        {
            PuppeteerEye eye = Instantiate(prefab, centre, prefab.transform.rotation);
            eye.name = prefab.name;

            // The ring lowers and moves its eyes itself; an eye's own drop
            // would hide it and fight the ring for its place.
            PuppetDescent descent = eye.GetComponent<PuppetDescent>();

            if (descent != null)
            {
                descent.HandOver();
            }

            eye.Health.Invulnerable = true;
            return eye;
        }

        /// <summary>Every eye charges, then fires a steady beam straight outward.</summary>
        public void Fire(SweepBeam.Settings beam, SceptorCharge.Settings charge, float chargeTime, float muzzleDistance)
        {
            foreach (PuppeteerEye eye in eyes)
            {
                if (eye != null)
                {
                    eye.StartBeam(beam, charge, chargeTime, muzzleDistance, owner);
                }
            }
        }

        /// <summary>
        /// Stops the ring turning and every steady beam, frees every eye's
        /// gaze to find the player again, then has each fire a laser at them
        /// as <see cref="EyeballLaserPattern"/> eyes do: tracking, tipping
        /// down to the floor and sweeping up.
        /// </summary>
        public void FireAtPlayer(EyeballLaserPattern.Settings laser)
        {
            spinSpeed = 0f;
            gazeHeld = false;

            foreach (PuppeteerEye eye in eyes)
            {
                if (eye != null)
                {
                    eye.StopBeam();
                    eye.GazeOverride = null;
                    eye.FireLaser(laser, owner);
                }
            }
        }

        /// <summary>True while any eye is still charging or firing.</summary>
        public bool IsLasering => eyes.Exists(eye => eye != null && eye.IsLasering);

        /// <summary>Turns the ring at <paramref name="degreesPerSecond"/>; negative turns it the other way.</summary>
        public void Spin(float degreesPerSecond)
        {
            spinSpeed = degreesPerSecond;
        }

        /// <summary>Stops every beam, hauls every eye up and away, and removes the ring.</summary>
        public void Lift()
        {
            if (lifted)
            {
                return;
            }

            lifted = true;

            foreach (PuppeteerEye eye in eyes)
            {
                if (eye != null)
                {
                    eye.StopBeam();
                    eye.GazeOverride = null;
                    eye.LiftAway();
                }
            }

            eyes.Clear();
            lifts.Clear();
            Destroy(gameObject);
        }

        private void Update()
        {
            if (owner == null || owner.Health.IsDead)
            {
                Lift();
                return;
            }

            angle += spinSpeed * Time.deltaTime;
            Place();
        }

        private void Place()
        {
            for (int i = 0; i < eyes.Count; i++)
            {
                PuppeteerEye eye = eyes[i];

                if (eye == null)
                {
                    continue;
                }

                // Spaced for the whole ring, so eyes still to come leave their places free.
                Vector3 outward = Quaternion.Euler(0f, angle + i * 360f / count, 0f) * Vector3.forward;
                eye.transform.position = centre + outward * radius + Vector3.up * (height + lifts[i]);

                if (gazeHeld)
                {
                    eye.GazeOverride = outward;
                }
            }
        }
    }
}
