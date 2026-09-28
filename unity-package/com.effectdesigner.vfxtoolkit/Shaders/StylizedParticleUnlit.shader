// Stylized unlit particle shader for URP.
// Vertex stream contract (renderer.vertex_streams): ["Position", "Color", "UV", "Custom1X"]
//   TEXCOORD0.xy = UV, TEXCOORD0.z = Custom1.x = extra erosion per particle (0 = none, 1 = gone).
// Without the Custom1X stream, erosion comes from _Erosion only.
Shader "EffectDesigner/Particles/Stylized Unlit"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map (RGBA)", 2D) = "white" {}
        [HDR][MainColor] _TintColor ("Tint (HDR)", Color) = (1, 1, 1, 1)

        [Header(Erosion)]
        _NoiseMap ("Erosion Noise (R)", 2D) = "white" {}
        _NoiseScroll ("Noise Scroll (XY)", Vector) = (0, 0, 0, 0)
        _Erosion ("Erosion", Range(0, 1)) = 0
        _Softness ("Softness", Range(0, 0.5)) = 0.05
        _EdgeWidth ("Edge Width", Range(0, 0.3)) = 0
        [HDR] _EdgeColor ("Edge Color (HDR)", Color) = (1, 0.6, 0.2, 1)

        [Header(Stylize)]
        _PosterizeSteps ("Posterize Alpha Steps (0 = off)", Range(0, 8)) = 0

        [Header(Soft Particles)]
        [Toggle(_SOFTPARTICLES_ON)] _SoftParticles ("Soft Particles (needs Depth Texture)", Float) = 0
        _SoftParticleDistance ("Soft Distance", Float) = 0.5

        [Header(Blending)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "StylizedParticle"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _SOFTPARTICLES_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _TintColor;
                float4 _NoiseMap_ST;
                float4 _NoiseScroll;
                float _Erosion;
                float _Softness;
                float _EdgeWidth;
                half4 _EdgeColor;
                float _PosterizeSteps;
                float _SoftParticleDistance;
            CBUFFER_END

            #include "Packages/com.effectdesigner.vfxtoolkit/Shaders/VFXCore.hlsl"

            TEXTURE2D(_BaseMap);  SAMPLER(sampler_BaseMap);
            TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0; // xy = UV, z = Custom1.x
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float4 uv : TEXCOORD0;      // xy = base UV, zw = noise UV
                float erosion : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv.xy = TRANSFORM_TEX(input.uv.xy, _BaseMap);
                output.uv.zw = VFXScrollUV(TRANSFORM_TEX(input.uv.xy, _NoiseMap), _NoiseScroll.xy);
                output.erosion = saturate(_Erosion + input.uv.z);
                output.screenPos = ComputeScreenPos(output.positionCS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy);
                half4 color = baseColor * _TintColor * input.color;

                float edge = 0;
                // No erosion = the whole shape, even where the noise is 0 (a mask of 0 would otherwise
                // vanish from the first frame, and the edge color would outline it).
                if (input.erosion > 0.0)
                {
                    float noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, input.uv.zw).r;
                    color.a *= VFXErode(noise, input.erosion, _Softness, _EdgeWidth, edge);
                    color.rgb = lerp(color.rgb, _EdgeColor.rgb * input.color.rgb, edge);
                }

                color.a = VFXPosterize(color.a, _PosterizeSteps);

                #if defined(_SOFTPARTICLES_ON)
                color.a *= VFXSoftParticle(input.screenPos, _SoftParticleDistance);
                #endif

                return color;
            }
            ENDHLSL
        }
    }
}
