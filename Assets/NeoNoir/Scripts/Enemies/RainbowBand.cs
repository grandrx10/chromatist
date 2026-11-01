using System;
using UnityEngine;

namespace LightAndShadow
{
    /// <summary>
    /// One flat band of colour on the floor, a ring between two radii round a
    /// centre: shown faintly, and flashing bright in its colour when it
    /// explodes.
    /// </summary>
    /// <remarks>
    /// Builds its own mesh, so it needs no prefab, and destroys it with
    /// itself. Drawn in NeoNoir/Flat, faint through its dithered _Fade rather
    /// than transparency, coloured through a property block. An inner radius
    /// of 0 makes a full disc. Whoever creates it calls <see cref="FadeOut"/>
    /// when done; <see cref="DestroyWhen"/> covers an owner that might stop
    /// before getting the chance.
    /// </remarks>
    public sealed class RainbowBand : MonoBehaviour
    {
        private const int Segments = 96;

        private static readonly int FadeId = Shader.PropertyToID("_Fade");

        private MeshRenderer band;
        private Mesh mesh;
        private MaterialPropertyBlock block;
        private Color colour;
        private float faint;

        // 0 to 1: fading in at the start, out at the end.
        private float shown;
        private float showTarget = 1f;
        private float showSpeed;

        private float flashIntensity;
        private float flashTime;
        private float flashAge = float.PositiveInfinity;

        private Func<bool> expired;

        /// <summary>
        /// Lays a band from <paramref name="inner"/> to <paramref name="outer"/>
        /// round <paramref name="centre"/>, flat at its height, fading in to
        /// <paramref name="faintness"/> (0 to 1) over <paramref name="fadeIn"/> seconds.
        /// </summary>
        public static RainbowBand Create(Vector3 centre, float inner, float outer, Color colour,
                                         float faintness, float fadeIn)
        {
            GameObject host = new GameObject("RainbowBand");
            host.transform.position = centre;

            RainbowBand rainbow = host.AddComponent<RainbowBand>();
            rainbow.Begin(inner, outer, colour, faintness, fadeIn);
            return rainbow;
        }

        /// <summary>Flashes the band bright, <paramref name="intensity"/> times its colour, easing back to faint over <paramref name="seconds"/>.</summary>
        public void Flash(float intensity, float seconds)
        {
            flashIntensity = intensity;
            flashTime = Mathf.Max(0.01f, seconds);
            flashAge = 0f;
            Draw();
        }

        /// <summary>Fades the band away over <paramref name="seconds"/>, then destroys it.</summary>
        public void FadeOut(float seconds)
        {
            showTarget = 0f;
            showSpeed = seconds > 0f ? 1f / seconds : float.PositiveInfinity;
        }

        /// <summary>Destroys the band by itself the first frame <paramref name="condition"/> is true.</summary>
        public void DestroyWhen(Func<bool> condition)
        {
            expired = condition;
        }

        private void Begin(float inner, float outer, Color bandColour, float faintness, float fadeIn)
        {
            colour = bandColour;
            faint = Mathf.Clamp01(faintness);
            showSpeed = fadeIn > 0f ? 1f / fadeIn : float.PositiveInfinity;
            shown = float.IsPositiveInfinity(showSpeed) ? 1f : 0f;

            mesh = Annulus(Mathf.Max(0f, inner), Mathf.Max(inner + 0.01f, outer));
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

            band = gameObject.AddComponent<MeshRenderer>();
            band.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            band.receiveShadows = false;
            band.sharedMaterial = FlatMaterial.Shared;

            block = new MaterialPropertyBlock();
            Draw();
        }

        private void Update()
        {
            if (expired != null && expired())
            {
                Destroy(gameObject);
                return;
            }

            if (shown != showTarget)
            {
                shown = Mathf.MoveTowards(shown, showTarget, showSpeed * Time.deltaTime);
            }

            if (shown <= 0f && showTarget <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            flashAge += Time.deltaTime;
            Draw();
        }

        private void Draw()
        {
            // Bright and solid at the flash, easing back to faint.
            float flash = flashAge < flashTime ? 1f - flashAge / flashTime : 0f;
            flash *= flash;

            Color tint = colour * Mathf.Lerp(1f, flashIntensity, flash);
            tint.a = 1f;

            block.SetColor(FlatMaterial.BaseColorId, tint);
            block.SetFloat(FadeId, Mathf.Lerp(faint, 1f, flash) * shown);
            band.SetPropertyBlock(block);
        }

        private void OnDestroy()
        {
            if (mesh != null)
            {
                Destroy(mesh);
            }
        }

        /// <summary>A flat ring from <paramref name="inner"/> to <paramref name="outer"/>, facing up.</summary>
        private static Mesh Annulus(float inner, float outer)
        {
            Vector3[] vertices = new Vector3[(Segments + 1) * 2];
            Vector3[] normals = new Vector3[vertices.Length];
            int[] triangles = new int[Segments * 6];

            for (int i = 0; i <= Segments; i++)
            {
                float angle = i * Mathf.PI * 2f / Segments;
                Vector3 outward = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));

                vertices[i * 2] = outward * inner;
                vertices[i * 2 + 1] = outward * outer;
                normals[i * 2] = Vector3.up;
                normals[i * 2 + 1] = Vector3.up;
            }

            for (int i = 0; i < Segments; i++)
            {
                int v = i * 2;
                int t = i * 6;

                // Clockwise seen from above, so the face points up.
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 3;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 3;
                triangles[t + 5] = v + 2;
            }

            Mesh annulus = new Mesh { name = "RainbowBand" };
            annulus.vertices = vertices;
            annulus.normals = normals;
            annulus.triangles = triangles;
            annulus.RecalculateBounds();
            return annulus;
        }
    }
}
