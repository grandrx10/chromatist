// NeoNoirTear
//
// Scarlett's claw marks. Not a fill: a window. Everything behind the tear
// still shows through it as one of two flat colours, never a mix: anything
// behind whose strongest channel is above _BehindCut -- white surfaces,
// spot-colour enemies -- shows in the overshadowed colour (_BehindColor),
// and the black background in the slash's own colour (_BaseColor). So
// shapes and edges stay readable inside the mark.
//
// Reads the camera opaque texture, so the pipeline asset must have Opaque
// Texture on (with no downsampling, or what shows through is blurred). Draws
// in the transparent queue, after that texture is captured.
//
// Writes no depth and has no DepthNormals pass, deliberately. The outline
// pass in NeoNoirEdges works from depth and normals, so it never sees the
// tear: the outlines of everything behind it carry on straight through.
// The cost is that the tear gets no outline from that pass either, so it
// draws its own: the mesh includes a thin strip round its edge, flagged with
// uv.x = 1, which is filled with _OutlineColor instead of the window.
// uv.x = 2 is drawn solid in _BloodColor, for the blood ScarlettBlood spews.
// Its own colour, since the blood sprays along the tear and would vanish
// against it in the slash colour.
//
// Keep both colours strongly saturated: that is what lets the window survive
// the NeoNoirEdges threshold as a spot colour instead of being crushed to
// black or white.

Shader "NeoNoir/Tear"
{
    Properties
    {
        [HDR] _BaseColor ("Slash", Color) = (1, 0.04, 0.08, 1)
        [HDR] _BehindColor ("Overshadowed", Color) = (1, 0.35, 0.4, 1)
        _BehindCut ("Overshadowed Cut", Range(0, 1)) = 0.35
        [HDR] _OutlineColor ("Outline", Color) = (1, 1, 1, 1)
        [HDR] _BloodColor ("Blood", Color) = (1.2, 0.45, 0.62, 1)
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
            Name "NeoNoirTear"
            Tags { "LightMode" = "UniversalForward" }

            // Opaque output is fine here: the colour already contains the
            // scene behind it. Culling one side matters, though -- the mesh
            // is built with both windings, and without culling every pixel
            // would be drawn twice.
            // ZTest Always: the mark is clawed across the picture, so nothing
            // may stand in front of it. With a depth test, anything taller
            // than the lift -- a raised enemy, a rising cube -- covered the
            // tear and showed in its own colours instead of being recoloured.
            // Nothing is lost by drawing over it: the window already shows
            // whatever is in front, read from the opaque texture.
            Blend Off
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _BehindColor;
                half _BehindCut;
                half4 _OutlineColor;
                half4 _BloodColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float outline : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.outline = input.uv.x;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // uv.x picks what this part of the mesh is: 0 the window,
                // 1 the outline, 2 solid blood colour (ScarlettBlood's drops).
                if (input.outline > 1.5)
                {
                    return half4(_BloodColor.rgb, 1.0h);
                }

                if (input.outline > 0.5)
                {
                    return half4(_OutlineColor.rgb, 1.0h);
                }

                float2 uv = input.screenPos.xy / input.screenPos.w;
                half3 scene = SampleSceneColor(uv);

                // A hard switch, not a blend: each pixel is either
                // overshadowed or not. Tested on the strongest channel rather
                // than luminance, so the question is "is anything there"
                // rather than "is it bright": a spot-colour enemy authored
                // pure red has a luminance of only about 0.2 and would read
                // as empty background, but its red channel is near full.
                half strength = max(scene.r, max(scene.g, scene.b));
                half3 colour = strength > _BehindCut ? _BehindColor.rgb : _BaseColor.rgb;

                return half4(colour, 1.0h);
            }
            ENDHLSL
        }
    }
}
