Shader "Skybox/Procedural Sky URP"
{
    Properties
    {
        [Header(Horizon)]
        _HorizonColor   ("Horizon Color",   Color)            = (0.82, 0.56, 0.36, 1)
        _HorizonBlur    ("Horizon Blur",     Range(0.01, 1))  = 0.25
        _HorizonRange   ("Horizon Range",    Range(0.01, 2))  = 0.5
        _HorizonOffset  ("Horizon Offset",   Range(-0.5, 0.5))= 0.0

        [Header(Zenith)]
        _ZenithColor     ("Zenith Color",      Color)           = (0.05, 0.18, 0.55, 1)
        _ZenithBlendColor("Zenith Blend Color", Color)          = (0.28, 0.44, 0.76, 1)
        _ZenithGradient  ("Zenith Gradient",   Range(0.01, 1)) = 0.45

        [Header(Ground)]
        _GroundColor     ("Ground Color",      Color)           = (0.12, 0.10, 0.07, 1)
        _GroundBlendColor("Ground Blend Color", Color)          = (0.38, 0.28, 0.18, 1)
        _GroundGradient  ("Ground Gradient",   Range(0.01, 1)) = 0.45

        [Header(Sun)]
        _SunDirection ("Sun Direction",  Vector)          = (0.3, 0.5, 0.8, 0)
        _SunSize      ("Sun Size",       Range(0.001, 0.2)) = 0.04
        _SunIntensity ("Sun Intensity",  Range(0, 5))     = 1.5
        _SunColor     ("Sun Color",      Color)           = (1.0, 0.95, 0.82, 1)

        [Header(Global)]
        _Exposure ("Exposure", Range(0.1, 8)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue"          = "Background"
            "RenderType"     = "Background"
            "PreviewType"    = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            Name "ProceduralSky"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            /* ── uniforms (SRP-Batcher compatible) ── */
            CBUFFER_START(UnityPerMaterial)
                half4  _HorizonColor;
                half   _HorizonBlur;
                half   _HorizonRange;
                half   _HorizonOffset;

                half4  _ZenithColor;
                half4  _ZenithBlendColor;
                half   _ZenithGradient;

                half4  _GroundColor;
                half4  _GroundBlendColor;
                half   _GroundGradient;

                float4 _SunDirection;
                half   _SunSize;
                half   _SunIntensity;
                half4  _SunColor;

                half   _Exposure;
            CBUFFER_END

            /* ── structs ── */
            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDir    : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            /* ── vertex ── */
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.viewDir    = IN.positionOS.xyz;
                return OUT;
            }

            /* ── fragment ── */
            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.viewDir);
                float  y   = dir.y - _HorizonOffset;

                /* ╔══════════════════════════════════════╗
                   ║  1. Horizon transition mask           ║
                   ║  Exponential falloff — no hard edge   ║
                   ║  at any Range / Blur extreme.         ║
                   ║  Range scales extent; Blur scales     ║
                   ║  softness proportionally.             ║
                   ╚══════════════════════════════════════╝ */
                float skyH = max(y, 0.0);
                float gndH = max(-y, 0.0);

                float invBlur = lerp(8.0, 0.5, _HorizonBlur);
                float rate    = invBlur / max(_HorizonRange, 0.001) * 3.0;

                float skyMask = 1.0 - exp(-skyH * rate);
                float gndMask = 1.0 - exp(-gndH * rate);

                /* ╔══════════════════════════════════════╗
                   ║  2. Zenith (sky) gradient             ║
                   ║  BlendColor = lower sky               ║
                   ║  ZenithColor = upper sky              ║
                   ╚══════════════════════════════════════╝ */
                float zPow    = lerp(0.5, 4.0, _ZenithGradient);
                float zenithT = pow(saturate(skyH), zPow);
                half3 skyCol  = lerp(_ZenithBlendColor.rgb, _ZenithColor.rgb, zenithT);
                half3 sky     = lerp(_HorizonColor.rgb, skyCol, skyMask);

                /* ╔══════════════════════════════════════╗
                   ║  3. Ground gradient                   ║
                   ║  BlendColor = near-horizon ground     ║
                   ║  GroundColor = nadir                  ║
                   ╚══════════════════════════════════════╝ */
                float gPow    = lerp(0.5, 4.0, _GroundGradient);
                float groundT = pow(saturate(gndH), gPow);
                half3 gndCol  = lerp(_GroundBlendColor.rgb, _GroundColor.rgb, groundT);
                half3 gnd     = lerp(_HorizonColor.rgb, gndCol, gndMask);

                // Both sides converge to _HorizonColor at y=0 — no seam
                half3 col = (y >= 0.0) ? sky : gnd;

                /* ╔══════════════════════════════════════╗
                   ║  4. Sun disk + atmospheric glow       ║
                   ╚══════════════════════════════════════╝ */
                float3 sunDir  = normalize(_SunDirection.xyz);
                float  sunDot  = dot(dir, sunDir);
                float  sunDist = 1.0 - sunDot;

                // Sharp-edged disk with thin soft fringe
                float diskR    = _SunSize * 0.1;
                float diskEdge = diskR * 0.15;
                float disk     = 1.0 - smoothstep(diskR - diskEdge, diskR + diskEdge, sunDist);

                // Glow: Rayleigh-style power falloff, width scales with SunSize
                float glowPow = 256.0 / max(_SunSize * 100.0, 1.0);
                float glow    = pow(max(sunDot, 0.0), glowPow);

                // Mie-like forward scatter (wider, subtler halo)
                float mie = pow(max(sunDot, 0.0), 8.0);

                // Controlled additive — keeps sun overlay gentle
                half3 sunC = _SunColor.rgb * _SunIntensity;
                col += sunC * disk;
                col += sunC * glow * 0.12;
                col += sunC * mie  * 0.04;

                /* ╔══════════════════════════════════════╗
                   ║  5. Exposure                          ║
                   ╚══════════════════════════════════════╝ */
                col *= _Exposure;

                return half4(col, 1.0);
            }

            ENDHLSL
        }
    }

    Fallback Off
}
