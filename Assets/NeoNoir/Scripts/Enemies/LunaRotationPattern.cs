using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// Luna's rotation: a row of swords drops in either side of her, in pairs
    /// of one colour, and the pairs sweep round her, each the other way from
    /// the one inside it, the outer ones faster, cutting hurting trails into
    /// the floor. Meanwhile circles close on the player one after another,
    /// each struck by a sword of its colour. At the end the swords stop,
    /// glisten and explode.
    /// </summary>
    /// <remarks>
    /// In order:
    ///   1. <see cref="pairs"/> pairs of <see cref="RotationSword"/>s drop in
    ///      along a line through her, square to the way to the player: the
    ///      first pair <see cref="firstDistance"/> either side of her, each
    ///      next pair <see cref="pairSpacing"/> further out. Both swords of a
    ///      pair take the same colour, each pair the next colour round the
    ///      hue wheel.
    ///   2. Landed, the pairs circle her for <see cref="spinTime"/>, both
    ///      swords of a pair together, so they stay either side of her. The
    ///      innermost pair goes a random way round, each pair out from it the
    ///      other way from the one inside. The innermost turns at
    ///      <see cref="innerSpeed"/> degrees a second and the outermost at
    ///      <see cref="outerSpeed"/>, those between in proportion. Each cuts
    ///      a trail of its colour into the floor, which hurts and fades as
    ///      <see cref="RotationSword"/> says; touching a sword hurts too.
    ///   3. From landing, every <see cref="circleInterval"/> seconds a circle
    ///      fills up to <see cref="circleScatter"/> any way from where the
    ///      player stands, at random, over the Warn Time, and a sword of
    ///      the circle's colour drops into it, bursting in that colour and
    ///      hurting the player still in it, as in
    ///      <see cref="SpearShotBase"/>. The last circle is laid so it strikes
    ///      by the time the swords stop.
    ///   4. The swords stop, glisten and explode.
    /// The attack ends once every sword has exploded and every circle struck.
    /// She stands where she is. Everything is laid on the floor under her,
    /// found on the Floor Mask; without one, at her pivot.
    ///
    /// The circles' swords are the Spear Prefab: set the sword model, as for
    /// the sword rain, and Spears Per Circle to 1. Without a Spear Prefab
    /// there are no circles, but the rotating swords still go.
    /// </remarks>
    [CreateAssetMenu(menuName = "NeoNoir/Attacks/Luna/Luna Rotation", fileName = "LunaRotation")]
    public sealed class LunaRotationPattern : SpearShotBase
    {
        private const float GoldenRatio = 0.618034f;

        [Header("Row")]
        [SerializeField]
        [Tooltip("Pairs of swords, one either side of her each.")]
        [Min(1)]
        private int pairs = 4;

        [SerializeField]
        [Tooltip("Distance from her of the first pair.")]
        [Min(0f)]
        private float firstDistance = 2.5f;

        [SerializeField]
        [Tooltip("How much further out each pair is than the one inside it.")]
        [Min(0f)]
        private float pairSpacing = 3f;

        [Header("Spin")]
        [SerializeField]
        [Tooltip("Seconds the pairs circle her, from landing to stopping.")]
        [Min(0f)]
        private float spinTime = 10f;

        [SerializeField]
        [Tooltip("Degrees a second the innermost pair turns.")]
        [Min(0f)]
        private float innerSpeed = 30f;

        [SerializeField]
        [Tooltip("Degrees a second the outermost pair turns.")]
        [Min(0f)]
        private float outerSpeed = 70f;

        [Header("Player Circles")]
        [SerializeField]
        [Tooltip("Seconds between one circle forming where the player stands and the next.")]
        [Min(0.05f)]
        private float circleInterval = 1f;

        [SerializeField]
        [Tooltip("Most each circle is laid off where the player stands, any way, at random. 0 lays it right on them.")]
        [Min(0f)]
        private float circleScatter = 4f;

        [Header("Colours")]
        [SerializeField]
        [Tooltip("Saturation of the swords', trails' and circles' colours.")]
        [Range(0f, 1f)]
        private float saturation = 1f;

        [SerializeField]
        [Tooltip("Brightness of the swords' and trails' colours. Above 1 blooms. The circles keep plain full colour.")]
        [Min(0f)]
        private float intensity = 2.5f;

        [SerializeField] private RotationSword.Settings sword = new RotationSword.Settings();

        private Transform player;
        private Transform boss;
        private float circleHue;

        public override IEnumerator Perform(Enemy enemy)
        {
            boss = enemy.transform;
            player = enemy.HasTarget ? enemy.Target : null;

            Vector3 centre = boss.position;

            if (FindFloor(centre, out float floor))
            {
                centre.y = floor;
            }

            List<RotationSword> swords = DropSwords(enemy, centre);

            yield return Circles(enemy);

            while (swords.Exists(s => s != null))
            {
                yield return null;
            }
        }

        // The sword rain's circles, from SpearShotBase, laid by PlanRings.
        private IEnumerator Circles(Enemy enemy) => base.Perform(enemy);

        private List<RotationSword> DropSwords(Enemy enemy, Vector3 centre)
        {
            // The row lies square to the way to the player.
            Vector3 toward = player != null ? player.position - centre : boss.forward;
            toward.y = 0f;

            if (toward.sqrMagnitude < 0.0001f)
            {
                toward = Vector3.forward;
            }

            Vector3 right = Vector3.Cross(Vector3.up, toward.normalized);
            float bearing = Mathf.Atan2(right.z, right.x) * Mathf.Rad2Deg;

            float hue = Random.value;
            float way = Random.value < 0.5f ? 1f : -1f;
            RotationSword.Group group = new RotationSword.Group();
            List<RotationSword> swords = new List<RotationSword>(pairs * 2);

            for (int i = 0; i < pairs; i++)
            {
                float distance = firstDistance + i * pairSpacing;
                float speed = way * Mathf.Lerp(innerSpeed, outerSpeed, pairs > 1 ? i / (pairs - 1f) : 0f);

                Color plain = Color.HSVToRGB(hue, saturation, 1f);
                Color colour = plain * intensity;
                colour.a = 1f;

                swords.Add(RotationSword.Drop(enemy, centre, bearing, distance, speed, spinTime, colour, plain, group, sword));
                swords.Add(RotationSword.Drop(enemy, centre, bearing + 180f, distance, speed, spinTime, colour, plain, group, sword));

                hue = Mathf.Repeat(hue + GoldenRatio, 1f);
                way = -way;
            }

            return swords;
        }

        protected override List<Ring> PlanRings()
        {
            circleHue = Random.value;
            List<Ring> rings = new List<Ring>();

            // One circle at a time, from landing, each to strike before the swords stop.
            float last = sword.dropTime + spinTime - warnTime;

            for (float time = sword.dropTime; time <= last + 0.0001f; time += circleInterval)
            {
                rings.Add(new Ring { time = time, distance = 0f, angle = 0f, circles = 1 });
            }

            return rings;
        }

        // Each lone circle is laid near where the player stands as it appears.
        protected override Vector3 NextCircleOffset()
        {
            if (player == null || boss == null)
            {
                return Vector3.zero;
            }

            Vector2 scatter = Random.insideUnitCircle * circleScatter;
            Vector3 offset = player.position - boss.position + new Vector3(scatter.x, 0f, scatter.y);
            offset.y = 0f;
            return offset;
        }

        protected override CircleColours? NextCircleColours()
        {
            Color colour = Color.HSVToRGB(circleHue, saturation, 1f);
            circleHue = Mathf.Repeat(circleHue + GoldenRatio, 1f);

            Color spear = colour * intensity;
            spear.a = 1f;
            return new CircleColours { warning = colour, spear = spear };
        }

        protected override void Reset()
        {
            base.Reset();
            delayAfter = 2f;
            spearsPerCircle = 1;
            warnTime = 3f;
        }
    }
}
