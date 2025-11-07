Shader "VfxToMesh/SurfaceNetIndirect"
{
    Properties
    {
        _BaseColor("Surface Color", Color) = (0.5, 0.8, 1, 1)
        _WireColor("Wire Color", Color) = (0.08, 0.08, 0.08, 1)
        _WireThickness("Wire Thickness", Range(0.1, 4.0)) = 1.0
    }

    SubShader
    {
        Tags{ "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        Pass
        {
            Name "Forward"
            Tags{ "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            StructuredBuffer<float3> _VertexBuffer;
            StructuredBuffer<float3> _NormalBuffer;
            StructuredBuffer<uint> _IndexBuffer;
            StructuredBuffer<float3> _BarycentricBuffer;

            float4 _BaseColor;
            float4 _WireColor;
            float _WireThickness;
            float3 _BoundsCenter;
            float3 _BoundsExtent;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 barycentric : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                uint vertexIndex = _IndexBuffer[input.vertexID];
                float3 positionWS = _VertexBuffer[vertexIndex];
                float3 normalWS = normalize(_NormalBuffer[vertexIndex]);

                output.positionWS = positionWS;
                output.normalWS = normalWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.barycentric = _BarycentricBuffer[input.vertexID];
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normalWS = SafeNormalize(input.normalWS);
                float3 viewDir = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float3 lightDir = SafeNormalize(_MainLightPosition.xyz);

                float ndotl = saturate(dot(normalWS, lightDir));
                float ndotv = saturate(dot(normalWS, viewDir));

                float lighting = 0.2 + ndotl * 0.8;
                float fresnel = pow(1.0 - ndotv, 4.0);

                float3 surface = _BaseColor.rgb * lighting + fresnel * 0.25;

                float3 bary = input.barycentric;
                float edge = min(min(bary.x, bary.y), bary.z);
                float wire = pow(saturate(edge * _WireThickness), 0.5);
                float wireMask = saturate(1.0 - wire);

                float3 color = lerp(surface, _WireColor.rgb, wireMask);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
