using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Every one of the Puppeteer's eyes charges up, stares down at the
    /// ground at its feet and fires a laser, then slowly lifts its gaze to
    /// the horizon, dragging the laser out across the arena.
    /// </summary>
    /// <remarks>
    /// Each live <see cref="PuppeteerEye"/> plays out the same, all at once:
    ///   1. Charge: power gathers at its pupil for <see cref="Settings.chargeTime"/>
    ///      seconds. For the first <see cref="Settings.trackTime"/> of them it
    ///      keeps staring at the player; for the rest it tips down until it
    ///      looks at the ground <see cref="Settings.startAngle"/> degrees off
    ///      straight down, toward where the player was.
    ///   2. Sweep: a <see cref="SweepBeam"/> fires from its pupil and, over
    ///      <see cref="Settings.sweepTime"/> seconds, the eye lifts at a
    ///      steady rate until it looks dead level, the laser running out
    ///      across the floor toward and past the player. It burns the player
    ///      standing in it and leaves <see cref="LaserScorch"/> marks wherever
    ///      it touches.
    ///   3. The laser fizzles out and the attack is over.
    /// An eye destroyed meanwhile has its laser fizzle out at once and goes
    /// on dying as usual; the rest carry on.
    ///
    /// The beam's Wall Mask must include the floor, or the laser runs
    /// through it, leaving no marks.
    ///
    /// The boss needs <see cref="PuppeteerEyes"/>. Eyes are topped up just
    /// before the attack begins, so every one fires.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Eyeball Laser", fileName = "EyeballLaser")]
    public sealed class EyeballLaserPattern : AttackPattern
    {
        [Serializable]
        public sealed class Settings
        {
            [Header("Charge")]
            [Tooltip("Seconds from the start of the attack to the laser firing.")]
            [Min(0f)] public float chargeTime = 1f;

            [Tooltip("Seconds of the charge spent still staring at the player. The rest is spent tipping down.")]
            [Min(0f)] public float trackTime = 0.7f;

            [Tooltip("Distance from the eye's pivot, along its gaze, where power gathers and the laser starts. About the eye's radius.")]
            [Min(0f)] public float muzzleDistance = 0.5f;

            public SceptorCharge.Settings charge = new SceptorCharge.Settings();

            [Header("Sweep")]
            [Tooltip("Degrees off straight down the eye looks as the laser fires.")]
            [Range(0f, 90f)] public float startAngle = 15f;

            [Tooltip("Seconds the eye takes to lift its gaze from there to dead level, firing all the while.")]
            [Min(0.1f)] public float sweepTime = 4f;

            public SweepBeam.Settings beam = new SweepBeam.Settings
            {
                radius = 0.35f,
                singleHit = true,
                hitDamage = 20f,
                breaksGiftBoxes = true,
            };

            public LaserScorch.Settings marks = new LaserScorch.Settings();
        }

        [SerializeField] private Settings laser = new Settings();

        // Beyond the eyes' own time, in case one never reports back.
        private const float Grace = 1f;

        public override IEnumerator Perform(Enemy enemy)
        {
            PuppeteerEyes eyes = enemy.GetComponent<PuppeteerEyes>();

            if (eyes == null)
            {
                Debug.LogWarning($"[EyeballLaser] {enemy.name} has no PuppeteerEyes to fire lasers from.", enemy);
                yield break;
            }

            List<PuppeteerEye> firing = new List<PuppeteerEye>();

            foreach (PuppeteerEye eye in eyes.Eyes)
            {
                if (eye != null && !eye.Health.IsDead && eye.FireLaser(laser, enemy))
                {
                    firing.Add(eye);
                }
            }

            float limit = Mathf.Max(laser.chargeTime, laser.trackTime) + laser.sweepTime + Grace;
            float clock = 0f;

            while (clock < limit && firing.Exists(eye => eye != null && eye.IsLasering))
            {
                yield return null;
                clock += Time.deltaTime;
            }
        }

        private void Reset()
        {
            delayAfter = 2f;
            Movement.type = MovementType.StandStill;
        }
    }
}
