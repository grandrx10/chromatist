// NeoNoirDarkness
//
// The room gone dark round what is kept lit: run by NeoNoirPostFeature after
// the edges and threshold, while NeoNoirDarkness.Amount is above 0.
//
//   - Pass 0 (mask) draws the kept renderers flat white into a mask, as an
//     override material, so anything can be kept, skinned meshes included.
//   - Pass 1 (composite) is fullscreen: it darkens every pixel by
//     _DarknessAmount but where the mask is, widened by a couple of pixels
//     so the outlines round what is kept stay too.
//
// Lives in a Resources folder so builds always include it: nothing
// references it but NeoNoirPostFeature, through Shader.Find.

Shader "Hidden/NeoNoir/Darkness"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "NeoNoirDarknessMask"

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(1.0h, 1.0h, 1.0h, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "NeoNoirDarkness"

            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_NeoNoirKeepMask);
            float _DarknessAmount;

            float Kept(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_NeoNoirKeepMask, sampler_PointClamp, saturate(uv)).r;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float3 colour = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // Widened by a ring of taps, so the outline drawn just
                // outside what is kept is kept with it.
                float2 tap = _ScreenSize.zw * 2.0;
                float kept = Kept(uv);
                kept = max(kept, Kept(uv + float2( tap.x, 0.0)));
                kept = max(kept, Kept(uv + float2(-tap.x, 0.0)));
                kept = max(kept, Kept(uv + float2(0.0,  tap.y)));
                kept = max(kept, Kept(uv + float2(0.0, -tap.y)));
                kept = max(kept, Kept(uv + tap));
                kept = max(kept, Kept(uv - tap));
                kept = max(kept, Kept(uv + float2(tap.x, -tap.y)));
                kept = max(kept, Kept(uv + float2(-tap.x, tap.y)));

                float dark = saturate(_DarknessAmount) * (1.0 - kept);
                return half4(colour * (1.0 - dark), 1.0h);
            }
            ENDHLSL
        }
    }
}
