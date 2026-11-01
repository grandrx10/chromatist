using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// A puppet string that has just been cut: it springs up to where it hung
    /// from, whipping side to side, fades as it goes, and is removed. Added
    /// by <see cref="PuppetStrings"/> to the string's own LineRenderer, since
    /// the enemy may be gone before it finishes.
    /// </summary>
    /// <remarks>
    /// The loose end rushes up fast and slows toward the top. The whip is a
    /// wave along the string, still at both ends, swinging about a random
    /// sideways axis and dying away as the string pulls in.
    /// </remarks>
    public sealed class CutPuppetString : MonoBehaviour
    {
        private static readonly int LengthId = Shader.PropertyToID("_Length");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        /// <summary>Half-waves along the string as it whips.</summary>
        private const float Waves = 3f;

        /// <summary>Full swings a second.</summary>
        private const float WhipRate = 6f;

        private LineRenderer line;
        private MaterialPropertyBlock block;
        private Vector3 loose;
        private Vector3 top;
        private Vector3 whipAxis;
        private float duration;
        private float wobble;
        private float startAlpha = 1f;
        private float age;

        /// <summary>
        /// Starts the recoil from <paramref name="cutAt"/> up to
        /// <paramref name="hungFrom"/>, over <paramref name="time"/> seconds,
        /// whipping up to <paramref name="whip"/> units aside, drawn with
        /// <paramref name="points"/> points.
        /// </summary>
        public void Begin(Vector3 cutAt, Vector3 hungFrom, float time, float whip, int points)
        {
            Vector2 sideways = Random.insideUnitCircle.normalized;
            Begin(cutAt, hungFrom, time, whip, points, new Vector3(sideways.x, 0f, sideways.y), 1f);
        }

        /// <summary>
        /// As the other overload, whipping about <paramref name="axis"/>, so
        /// several lines drawn as one string whip together, and fading from
        /// <paramref name="alpha"/> rather than from fully solid.
        /// </summary>
        public void Begin(Vector3 cutAt, Vector3 hungFrom, float time, float whip, int points, Vector3 axis, float alpha)
        {
            line = GetComponent<LineRenderer>();
            block = new MaterialPropertyBlock();
            loose = cutAt;
            top = hungFrom;
            duration = Mathf.Max(0.05f, time);
            wobble = whip;
            whipAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.right;
            startAlpha = Mathf.Clamp01(alpha);

            line.positionCount = Mathf.Max(2, points);
            Update();
        }

        private void Update()
        {
            if (line == null)
            {
                return;
            }

            age += Time.deltaTime;
            float t = age / duration;

            if (t >= 1f)
            {
                Destroy(gameObject);
                return;
            }

            // Fast off the mark, easing in to the top.
            float pulled = 1f - (1f - t) * (1f - t) * (1f - t);
            Vector3 end = Vector3.Lerp(loose, top, pulled);

            int count = line.positionCount;
            float swing = Mathf.Sin(age * WhipRate * 2f * Mathf.PI) * wobble * (1f - t);

            for (int i = 0; i < count; i++)
            {
                float s = (float)i / (count - 1);

                // Still at both ends, whipping in between.
                float bend = Mathf.Sin(s * Mathf.PI) * Mathf.Sin(s * Waves * Mathf.PI + age * 10f);
                line.SetPosition(i, Vector3.Lerp(end, top, s) + whipAxis * (swing * bend));
            }

            line.GetPropertyBlock(block);
            block.SetFloat(LengthId, Vector3.Distance(end, top));
            block.SetFloat(AlphaId, startAlpha * (1f - t));
            line.SetPropertyBlock(block);
        }
    }
}
