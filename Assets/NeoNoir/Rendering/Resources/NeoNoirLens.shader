// NeoNoirLens
//
// Space bent round a point on the floor, as a white hole bends it: a
// fullscreen pass, run by NeoNoirPostFeature after the edges and threshold,
// so everything in the frame bends together, outlines included, and bloom
// still runs after.
//
// The lens is an ellipse on screen: one unit of its radius along the
// camera's right (_LensAxes.xy) and along its flattened forward
// (_LensAxes.zw), in viewport units, so it lies on the floor the way the
// hole does. Working in those units, r = 1 is the lens's edge, where
// nothing is bent, so it has no seam. Further in:
//
//   - pull: each pixel shows what lies further out, by _LensShape.y at the
//     white middle, easing off to nothing at the edge, so the world looks
//     squeezed in toward the middle;
//   - swirl: it is turned round the middle by _LensShape.z radians at the
//     white, easing off the same way, with ripples of _LensWave.x radians,
//     _LensWave.y of them across the lens, running inward at _LensWave.z;
//   - chroma: red reads a little further out than green, blue a little
//     nearer, by _LensShape.w, so edges split into colour fringes;
//   - pulse: the pull throbs by _LensWave.w.
//
// Inside _LensShape.x of the radius is the white middle, drawn flat in
// _LensCore.
//
// Lives in a Resources folder so builds always include it: nothing
// references it but NeoNoirPostFeature, through Shader.Find.

Shader "Hidden/NeoNoir/Lens"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "NeoNoirLens"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _LensCentre;
            float4 _LensAxes;
            float4 _LensShape;
            float4 _LensWave;
            float4 _LensCore;

            float3 SampleFrame(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, saturate(uv)).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float2 right = _LensAxes.xy;
                float2 forward = _LensAxes.zw;

                // Into lens units: solve offset = right * q.x + forward * q.y.
                float2 offset = uv - _LensCentre.xy;
                float det = right.x * forward.y - forward.x * right.y;

                if (abs(det) < 1e-8)
                {
                    return half4(SampleFrame(uv), 1.0h);
                }

                float2 q = float2(forward.y * offset.x - forward.x * offset.y,
                                  right.x * offset.y - right.y * offset.x) / det;
                float r = length(q);

                if (r >= 1.0)
                {
                    return half4(SampleFrame(uv), 1.0h);
                }

                float core = _LensShape.x;
                float s = saturate((r - core) / max(1.0 - core, 1e-4));
                float closeness = (1.0 - s) * (1.0 - s);

                float pulse = 1.0 + _LensWave.w * sin(_Time.y * 3.0);
                float reach = r + _LensShape.y * pulse * closeness;

                float angle = _LensShape.z * closeness
                              + _LensWave.x * (1.0 - s) * sin(s * _LensWave.y * 6.2831853 + _Time.y * _LensWave.z);

                float2 way = r > 1e-5 ? q / r : float2(1.0, 0.0);
                float sine = sin(angle);
                float cosine = cos(angle);
                way = float2(way.x * cosine - way.y * sine, way.x * sine + way.y * cosine);

                float spread = _LensShape.w * closeness;
                float2 toRed = way * (reach * (1.0 + spread));
                float2 toGreen = way * reach;
                float2 toBlue = way * (reach * (1.0 - spread));

                float3 colour;
                colour.r = SampleFrame(_LensCentre.xy + right * toRed.x + forward * toRed.y).r;
                colour.g = SampleFrame(_LensCentre.xy + right * toGreen.x + forward * toGreen.y).g;
                colour.b = SampleFrame(_LensCentre.xy + right * toBlue.x + forward * toBlue.y).b;

                // A hard edge to the white, a hair soft so it does not crawl.
                float white = 1.0 - smoothstep(core * 0.97, core, r);
                colour = lerp(colour, _LensCore.rgb, white);

                return half4(colour, 1.0h);
            }
            ENDHLSL
        }
    }
}
