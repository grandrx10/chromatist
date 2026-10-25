// NeoNoirBarrier
//
// A see-through energy shell that survives the two-tone threshold.
//
// Real alpha blending cannot work in this game: a half-transparent white over
// a black floor is grey, and the fullscreen threshold crushes grey to either
// nothing or a solid white that hides whatever is inside. So nothing here is
// ever partly transparent. Every pixel is either drawn at full _BaseColor or
// not drawn at all, and the "transparency" is in how many are drawn:
//
//   Edges   The border of every panel is always solid. Read from UV0.x, which
//           the mesh sets to 0 at each panel's centre and 1 on its border.
//   Rim     Toward the silhouette the faces fill with a dithered stipple, so
//           the shell reads as a sphere and not a flat hex grid.
//   Hit     _Hit lights up the panels around one direction, for a parry.
//   Faces   _FaceFill stipples the whole face. 0 leaves them fully clear.
//
// Made for HexSphere.obj, whose UV0.y holds a random value per panel. _Reveal
// shows only the panels below it, so animating it 0 to 1 flickers the shell
// in panel by panel. _Fade is the same dithered dissolve as NeoNoir/Flat.
//
// Drawn in the transparent queue with no depth write and both sides, so the
// back of the shell shows through the front and the player inside is never
// hidden. It writes no depth or normals, so the outline pass ignores it.
//
// _BaseColor is HDR. Above 1.0 the lines bloom.

Shader "NeoNoir/Barrier"
{
    Properties
    {
        [HDR] _BaseColor ("Base Colour", Color) = (2, 2, 2, 1)

        [Header(Panels)][Space(4)]
        _EdgeWidth ("Edge Width", Range(0, 0.5)) = 0.1
        _FaceFill ("Face Fill", Range(0, 1)) = 0

        [Header(Rim)][Space(4)]
        _RimFill ("Rim Fill", Range(0, 1)) = 0.6
        _RimPower ("Rim Power", Range(0.5, 8)) = 3

        [Header(Hit flash)][Space(4)]
        _HitAngle ("Hit Angle (deg)", Range(1, 180)) = 40

        [Header(Rendering)][Space(4)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0

        // Driven per object from script (ParryBarrier) rather than set here.
        _Reveal ("Reveal", Range(0, 1)) = 1
        _Fade ("Fade", Range(0, 1)) = 1
        _Hit ("Hit (xyz direction, w strength)", Vector) = (0, 1, 0, 0)
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
            Name "NeoNoirBarrier"
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
                half   _FaceFill;
                half   _RimFill;
                half   _RimPower;
                half   _HitAngle;
                half   _Cull;
                half   _Reveal;
                half   _Fade;
                float4 _Hit;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // The same interleaved gradient noise as NeoNoir/Flat's fade, so
            // the barrier's stipple matches the blades' dissolve. Kept inside
            // (0, 1) so a fill of 0 draws nothing and 1 draws everything.
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
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float edge = input.uv.x;
                float panel = input.uv.y;

                // Never thinner than about a pixel and a half, or distant
                // edges break up into dots once the threshold crushes them.
                // Taken before any clip, so the derivative is always valid.
                float width = max(_EdgeWidth, fwidth(edge) * 1.5);
                float border = step(1.0 - width, edge);

                // Panels flicker in in the random order baked into the mesh.
                clip(_Reveal - panel);

                float threshold = StippleThreshold(input.positionCS);

                float3 normal = normalize(input.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));

                // abs, so the back of the shell rims the same as the front.
                float facing = abs(dot(normal, view));
                float rim = pow(1.0 - facing, _RimPower) * _RimFill;

                // The normal is radial, so it doubles as the direction of
                // this point from the centre.
                float hitCos = cos(radians(_HitAngle));
                float toHit = dot(normal, normalize(_Hit.xyz + float3(0.0, 1e-5, 0.0)));
                float hit = _Hit.w * saturate((toHit - hitCos) / max(1e-4, 1.0 - hitCos));

                float fill = max(max(rim, _FaceFill), hit);
                float covered = max(border, step(threshold, fill));
                clip(covered - 0.5);

                // The dissolve, run last so it thins edges and fill alike.
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
