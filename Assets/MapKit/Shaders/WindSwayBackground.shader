// 随风摇摆（精灵森林背景 + 抓握藤共用）。越往上晃得越多，地面那一段不动。
// 修正（2026-11-25）：
//  1. 旧版把整张图按 8px 重新采样，画面被降成 200×96 的马赛克，位移又小于 8px，几乎看不出在动。
//     现在只把"位移量"按像素对齐，图本身保持原清晰度。
//  2. 风的相位改用世界坐标 x，背景和藤蔓吃同一股风，阵风从左往右扫过整个区域。
//  3. 时间取全局 _MapKitWindTime（编辑器下由 MapKitWindEditorTicker 驱动），不点 Play 也能在 Scene/Game 窗口看到摇摆；
//     运行时它为 0，自动改用 _Time.y。
Shader "MapKit/WindSwayBackground"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _Amp ("摇摆幅度(贴图宽度的比例)", Range(0, 0.05)) = 0.0045
        _GroundV ("不动区域高度(V, 0=底)", Range(0, 0.5)) = 0.083
        _PixelStep ("位移按几个像素对齐(0=不对齐)", Float) = 4
        _TexSize ("贴图像素尺寸", Vector) = (1600, 768, 0, 0)
        _ZoneWidth ("区域宽度(单位)", Float) = 25
        _LeafJitter ("树冠细颤强度", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Amp, _GroundV, _PixelStep, _ZoneWidth, _LeafJitter;
            float4 _TexSize;
            float _MapKitWindTime; // 全局，由脚本设置；0 表示用 _Time.y

            struct a2v { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float wx : TEXCOORD1; };

            v2f vert (a2v v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                o.wx = mul(unity_ObjectToWorld, v.vertex).x;
                return o;
            }

            float gust(float x, float t) { float c = fmod(t * 0.12, 1.6) - 0.3; float d = x - c; return 0.9 * exp(-d * d * 18.0); }
            float wind(float x, float t) { return sin(t * 0.9 + x * 5.0) * 0.6 + sin(t * 2.1 + x * 13.0) * 0.25 + gust(x, t); }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = _MapKitWindTime > 0 ? _MapKitWindTime : _Time.y;
                float x = frac(i.wx / max(_ZoneWidth, 0.001));     // 区域内 0~1
                float h = saturate((i.uv.y - _GroundV) / max(1.0 - _GroundV, 0.001));
                float amp = pow(h, 1.6);
                float leaf = smoothstep(0.6, 1.0, i.uv.y) * sin(t * 3.1 + x * 40.0 + i.uv.y * 25.0) * _LeafJitter;

                // 位移以"贴图像素"计算，再按 _PixelStep 对齐，保证像素风不糊
                float2 offPx = float2((wind(x, t) + leaf) * amp * _Amp * _TexSize.x,
                                      sin(t * 1.7 + x * 21.0) * 0.0012 * amp * _TexSize.x);
                if (_PixelStep > 0) offPx = round(offPx / _PixelStep) * _PixelStep;

                float2 uv = clamp(i.uv + offPx / _TexSize.xy, 0.0005, 0.9995);
                return tex2D(_MainTex, uv) * i.color;
            }
            ENDCG
        }
    }
    Fallback "Sprites/Default"
}
