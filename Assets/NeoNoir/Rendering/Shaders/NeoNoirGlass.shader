// NeoNoirGlass
//
// A see-through bottle that survives the two-tone threshold.
//
// The same trick as NeoNoir/Barrier: real alpha blending would turn the glass
// into a grey the threshold crushes, so nothing here is partly transparent.
// Every pixel is either drawn at full _BaseColor or not drawn at all:
//
//   Edge    Where the surface turns away from the camera -- the silhouette
//           of the bottle -- is always solid. That band is what reads as glass.
//   Rim     Just inside it, a dithered stipple thins out toward the middle,
//           so the edge softens into the clear face instead of stopping dead.
//   Faces   _FaceFill stipples the whole face. 0 leaves them fully clear.
//
// Drawn in the transparent queue with no depth write, so whatever is inside
// -- the liquid -- always shows through. It writes no depth or normals, so
// the outline pass ignores it; the liquid inside keeps its own outline.
//
// _BaseColor is HDR. Above 1.0 the edge blooms. _Fade is the same
// dithered dissolve as NeoNoir/Flat.

Shader "NeoNoir/Glass"
{
    Properties
    {
        [HDR] _BaseColor ("Base Colour", Color) = (1.5, 1.5, 1.6, 1)

        [Header(Edge)][Space(4)]
        _EdgeWidth ("Edge Width", Range(0, 1)) = 0.25
        _RimFill ("Rim Fill", Range(0, 1)) = 0.5
        _RimWidth ("Rim Width", Range(0, 1)) = 0.25
        _FaceFill ("Face Fill", Range(0, 1)) = 0

        [Header(Rendering)][Space(4)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2

        // Driven per object from script rather than set here.
        _Fade ("Fade", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "NeoNoirGlass"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_Cull]
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                half   _EdgeWidth;
                half   _RimFill;
                half   _RimWidth;
                half   _FaceFill;
                half   _Cull;
                half   _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The same interleaved gradient noise as NeoNoir/Flat's fade and
            // the barrier's stipple, so they all dither alike.
            float StippleThreshold(float4 positionCS)
            {
                float noise = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
                return noise * 0.998 + 0.001;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = GetVertexNormalInputs(input.normalOS).normalWS;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float threshold = StippleThreshold(input.positionCS);

                float3 normal = normalize(input.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));

                // 0 facing the camera, 1 edge-on. abs, so back faces, if
                // drawn, rim the same as the front.
                float turned = 1.0 - abs(dot(normal, view));

                // Solid from 1 - _EdgeWidth out to the silhouette; the stipple
                // runs _RimWidth further in, thinning as it goes.
                float edgeStart = 1.0 - _EdgeWidth;
                float edge = step(edgeStart, turned);
                float rim = saturate((turned - (edgeStart - _RimWidth)) / max(1e-4, _RimWidth)) * _RimFill;

                float fill = max(rim, _FaceFill);
                float covered = max(edge, step(threshold, fill));
                clip(covered - 0.5);

                if (_Fade < 1.0)
                {
                    clip(_Fade - threshold);
                }

                return _BaseColor;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
