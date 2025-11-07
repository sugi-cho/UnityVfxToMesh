Shader "VfxToMesh/SdfSliceDebug"
{
    Properties
    {
        _SdfVolume("SDF Volume", 3D) = "" {}
        _SliceAxis("Slice Axis", Int) = 2
        _SliceDepth("Slice Depth", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Slice"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLINCLUDE
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE3D(_SdfVolume);
            SAMPLER(sampler_SdfVolume);

            float4 _BaseColor;
            float _SliceDepth;
            int _SliceAxis;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(float4(input.positionOS,1));
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 uvw = float3(input.uv, _SliceDepth);
                if (_SliceAxis == 0)
                {
                    uvw = float3(_SliceDepth, input.uv);
                }
                else if (_SliceAxis == 1)
                {
                    uvw = float3(input.uv.x, _SliceDepth, input.uv.y);
                }

                float sdf = SAMPLE_TEXTURE3D(_SdfVolume, sampler_SdfVolume, uvw).r;
                float signedColor = saturate(0.5 - sdf * 0.1);
                float3 color = lerp(float3(1, 0.25, 0.25), float3(0.25, 0.6, 1), signedColor);
                float alpha = 0.35 + smoothstep(-0.02, 0.02, -sdf) * 0.35;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
