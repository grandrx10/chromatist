// NeoNoirString
//
// A puppet string: a thin thread drawn by a LineRenderer, fading out toward
// its top so it seems to rise into the dark rather than end, with glints of
// light running up it as if it were being tugged from above.
//
// Expects the line's texture mode on Stretch, with its first point at the
// joint and its last at the top, so uv.x runs 0 at the joint to 1 at the top
// and uv.y 0 to 1 across. _Length is the line's length in world units, set
// per string through a property block, so glints keep the same spacing and
// speed however long the string is. _Alpha fades the whole string, for when
// it is cut.
//
// Transparent and writes no depth, so the NeoNoirEdges outline pass, which
// works from depth and normals, never sees it: the string draws no outline
// and cuts no lines through what is behind it. Keep the colour above 1 so it
// blooms, and white or strongly saturated so the edges threshold keeps it.

Shader "NeoNoir/String"
{
    Properties
    {
        [HDR] _BaseColor ("Colour", Color) = (2, 2, 2, 1)
        _FadeStart ("Fade Start", Range(0, 1)) = 0.35
        _Softness ("Edge Softness", Range(0, 1)) = 0.5

        [Header(Glints)]
        [HDR] _GlintColor ("Glint Colour", Color) = (6, 6, 6, 1)
        _GlintSpacing ("Glint Spacing", Float) = 6
        _GlintSpeed ("Glint Speed", Float) = 4
        _GlintSize ("Glint Size", Range(0.001, 1)) = 0.12

        // Set per string by PuppetStrings; these are only fallbacks.
        [HideInInspector] _Length ("Length", Float) = 10
        [HideInInspector] _Alpha ("Alpha", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "NeoNoirString"
            Tags { "LightMode" = "UniversalForward" }

            // Double sided: a camera-facing line's winding can come out
            // either way as it turns.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _FadeStart;
                half _Softness;
                half4 _GlintColor;
                float _GlintSpacing;
                float _GlintSpeed;
                half _GlintSize;
                float _Length;
                half _Alpha;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float along = input.uv.x;

                // Gone into the dark by the top.
                half alpha = 1.0h - smoothstep(_FadeStart, 1.0h, along);

                // Brightest down the middle, soft at the sides, so a thread
                // only a pixel or two wide does not shimmer as it moves.
                half across = 1.0h - abs(input.uv.y * 2.0h - 1.0h);
                alpha *= lerp(1.0h, saturate(across * 2.0h), _Softness);

                // Glints climb the string, evenly spaced in world units.
                float metres = along * _Length;
                float phase = frac(metres / max(_GlintSpacing, 0.01) - _Time.y * _GlintSpeed / max(_GlintSpacing, 0.01));
                half glint = smoothstep(1.0h - _GlintSize, 1.0h, phase);

                half3 colour = lerp(_BaseColor.rgb, _GlintColor.rgb, glint);
                alpha *= _BaseColor.a * input.color.a * _Alpha;

                return half4(colour * input.color.rgb, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
