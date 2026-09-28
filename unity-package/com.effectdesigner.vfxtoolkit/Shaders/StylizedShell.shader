// Stylized shell shader for URP mesh particles (domes, spheres, shockwave rings and walls, slashes).
// Flat toon fill whose color steps through a 4-stop ramp over the particle's life, a hard rim on
// the silhouette, darker back faces (the inside of a dome), and a dissolve that can break the shell
// into strips.
//
// Vertex stream contract (renderer.vertex_streams): ["Position", "Normal", "Color", "UV", "Custom1XY"]
//   TEXCOORD0.xy = mesh UV (u around, v across; see vfx_make_mesh)
//   TEXCOORD0.z  = Custom1.x = ramp position 0..1 (usually rises over life: hot -> cooled)
//   TEXCOORD0.w  = Custom1.y = extra erosion 0..1 (0 = whole, 1 = gone)
// Without the Custom1XY stream, the ramp stays at _RampOffset and erosion at _Erosion.
// Mesh particles need renderer.render_mode "mesh" and alignment "local" or "world".
//
// Two passes so a translucent shell sorts itself: the inside (back faces, _BackTint) is drawn
// first in the SRPDefaultUnlit pass, then the outside (front faces) in UniversalForward. A single
// Cull Off pass draws triangles in index order, so back faces could land on top of front faces.
Shader "EffectDesigner/Particles/Stylized Shell"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map (RGBA, optional)", 2D) = "white" {}

        [Header(Color Ramp over Custom1.x)]
        [HDR] _RampColor0 ("Ramp 0 (hot)", Color) = (1, 0.95, 0.8, 1)
        [HDR] _RampColor1 ("Ramp 1", Color) = (1, 0.8, 0.35, 1)
        [HDR] _RampColor2 ("Ramp 2", Color) = (1, 0.55, 0.25, 1)
        [HDR] _RampColor3 ("Ramp 3 (cooled)", Color) = (0.85, 0.35, 0.12, 1)
        _RampStops ("Ramp Stops (x,y,z for colors 1-3)", Vector) = (0.25, 0.5, 0.75, 0)
        _RampHard ("Hard Steps (0 smooth, 1 cel bands)", Range(0, 1)) = 1
        _RampOffset ("Ramp Offset", Range(0, 1)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1

        [Header(Rim)]
        [HDR] _RimColor ("Rim Color (HDR; alpha = rim opacity)", Color) = (1, 0.6, 0.2, 1)
        _RimWidth ("Rim Width", Range(0, 1)) = 0.25
        _RimSoftness ("Rim Softness", Range(0, 1)) = 0.05

        [Header(Back Faces)]
        _BackTint ("Back Face Tint (RGB x, A x; alpha 0 hides the inside)", Color) = (0.35, 0.3, 0.3, 0.8)

        [Header(Erosion)]
        _ErosionMap ("Erosion Mask (R; stripes break the shell into strips)", 2D) = "white" {}
        _ErosionScroll ("Erosion Scroll (XY)", Vector) = (0, 0, 0, 0)
        _Erosion ("Erosion", Range(0, 1)) = 0
        _Softness ("Softness", Range(0, 0.5)) = 0.02
        _EdgeWidth ("Edge Width", Range(0, 0.3)) = 0.04
        [HDR] _EdgeColor ("Edge Color (HDR)", Color) = (1, 0.5, 0.15, 1)

        [Header(Blending)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Sphere"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _RampColor0;
            half4 _RampColor1;
            half4 _RampColor2;
            half4 _RampColor3;
            float4 _RampStops;
            float _RampHard;
            float _RampOffset;
            float _Opacity;
            half4 _RimColor;
            float _RimWidth;
            float _RimSoftness;
            half4 _BackTint;
            float4 _ErosionMap_ST;
            float4 _ErosionScroll;
            float _Erosion;
            float _Softness;
            float _EdgeWidth;
            half4 _EdgeColor;
        CBUFFER_END

        #include "Packages/com.effectdesigner.vfxtoolkit/Shaders/VFXCore.hlsl"

        TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
        TEXTURE2D(_ErosionMap); SAMPLER(sampler_ErosionMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float4 uv : TEXCOORD0; // xy = UV, z = Custom1.x (ramp), w = Custom1.y (erosion)
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            half4 color : COLOR;
            float4 uv : TEXCOORD0;      // xy = base UV, zw = erosion UV
            float2 custom : TEXCOORD1;  // x = ramp position, y = erosion
            float3 normalWS : TEXCOORD2;
            float3 viewDirWS : TEXCOORD3;
        };

        Varyings vert(Attributes input)
        {
            Varyings output;
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.color = input.color;
            output.uv.xy = TRANSFORM_TEX(input.uv.xy, _BaseMap);
            output.uv.zw = VFXScrollUV(TRANSFORM_TEX(input.uv.xy, _ErosionMap), _ErosionScroll.xy);
            output.custom = float2(saturate(_RampOffset + input.uv.z), saturate(_Erosion + input.uv.w));
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            output.viewDirWS = GetWorldSpaceViewDir(positionWS);
            return output;
        }

        half4 ShellColor(Varyings input, bool backFace)
        {
            half4 ramp = VFXRamp4(input.custom.x, _RampColor0, _RampColor1, _RampColor2, _RampColor3, _RampStops.xyz, _RampHard);
            half4 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv.xy);
            half4 color = ramp * baseColor * input.color;
            color.a *= _Opacity;

            float3 normalWS = backFace ? -input.normalWS : input.normalWS;
            float rim = VFXToonRim(normalWS, input.viewDirWS, _RimWidth, _RimSoftness);
            color.rgb = lerp(color.rgb, _RimColor.rgb * input.color.rgb, rim);
            color.a = max(color.a, rim * _RimColor.a * input.color.a);

            if (backFace)
                color *= _BackTint;

            float edge = 0;
            if (input.custom.y > 0.0 || _EdgeWidth > 0.0)
            {
                float mask = SAMPLE_TEXTURE2D(_ErosionMap, sampler_ErosionMap, input.uv.zw).r;
                color.a *= VFXErode(mask, input.custom.y, _Softness, _EdgeWidth, edge);
                color.rgb = lerp(color.rgb, _EdgeColor.rgb * input.color.rgb, edge * step(0.0001, input.custom.y));
            }
            // Eroded-away pixels write nothing, so a shell with ZWrite on does not hide what is behind its gaps.
            clip(color.a - 0.001);
            return color;
        }
        ENDHLSL

        // Inside first: URP draws SRPDefaultUnlit before UniversalForward for each object.
        Pass
        {
            Name "StylizedShellInside"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragBack
            half4 fragBack(Varyings input) : SV_Target { return ShellColor(input, true); }
            ENDHLSL
        }

        Pass
        {
            Name "StylizedShell"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragFront
            half4 fragFront(Varyings input) : SV_Target { return ShellColor(input, false); }
            ENDHLSL
        }
    }
}
