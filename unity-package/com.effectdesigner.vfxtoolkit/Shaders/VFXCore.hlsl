// Effect Designer VFX core library (URP).
// Shared building blocks for effect shaders written by the Shader Artist agent. Include from
// a URP shader after declaring the material CBUFFER:
//   #include "Packages/com.effectdesigner.vfxtoolkit/Shaders/VFXCore.hlsl"
#ifndef EFFECT_DESIGNER_VFX_CORE_INCLUDED
#define EFFECT_DESIGNER_VFX_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

// Set by vfx_capture_timeline while rendering, so captures are deterministic.
float _VFXToolkitTime;
float _VFXToolkitCapture;

// Seconds for scrolling/pulsing: the capture's time during captures, _Time.y otherwise.
float VFXTime()
{
    return lerp(_Time.y, _VFXToolkitTime, saturate(_VFXToolkitCapture));
}

float2 VFXScrollUV(float2 uv, float2 speed)
{
    return uv + speed * VFXTime();
}

// x = angle around the centre in [0, 1), y = distance from the centre (0 at centre, 1 at the edge of a 0-1 quad).
float2 VFXPolarUV(float2 uv, float2 center)
{
    float2 d = uv - center;
    return float2(atan2(d.y, d.x) / TWO_PI + 0.5, length(d) * 2.0);
}

// Fades the particle out where it intersects opaque geometry. Needs Depth Texture enabled
// on the URP asset; screenPos comes from ComputeScreenPos(positionCS).
float VFXSoftParticle(float4 screenPos, float fadeDistance)
{
    if (fadeDistance <= 0.0)
        return 1.0;
    float2 uv = screenPos.xy / screenPos.w;
    float sceneEye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
    return saturate((sceneEye - screenPos.w) / fadeDistance);
}

// Dissolve. Returns the surviving alpha; edge is 1 on the rim that is about to burn away.
// softness 0 gives hard, cel-style cuts.
float VFXErode(float mask, float erosion, float softness, float edgeWidth, out float edge)
{
    float s = max(softness, 1e-4);
    float alive = smoothstep(erosion, erosion + s, mask);
    float inner = smoothstep(erosion + edgeWidth, erosion + edgeWidth + s, mask);
    edge = saturate(alive - inner);
    return alive;
}

// Quantizes 0..1 into flat bands (steps >= 2): stylized, painted-looking falloffs.
float VFXPosterize(float value, float steps)
{
    if (steps < 2.0)
        return value;
    return floor(saturate(value) * (steps - 0.001)) / (steps - 1.0);
}

float VFXFresnel(float3 normalWS, float3 viewDirWS, float power)
{
    return pow(1.0 - saturate(dot(normalize(normalWS), normalize(viewDirWS))), power);
}

#endif // EFFECT_DESIGNER_VFX_CORE_INCLUDED
