// 奶蛙二维嘴眼表情贴片（P5-B）。Built-in 管线，透明混合，不写深度，不投影。
//
// 贴片网格贴在脸前 1mm 左右，侧面陡坡和嘴缝处仍会被皮肤从下面"顶穿"。
// 顶点在视空间里朝相机前移 _ViewOffset 米，再乘以 max(0, dot(脸前方, 视线))：
// 正对时前移最多，35° 约 0.82 倍，侧视 90° 不前移，避免轮廓外伸。
// 数值与 Blender 预览（Tools/Blender/build_pet01_face_overlay.py 的 VIEW_OFFSET_M / facing）一致。
//
// 表情由 _MainTex_ST 选图集格子，表现层用 MaterialPropertyBlock 按渲染器单独设置，
// 四只宠物共用同一个材质也不会互相串表情。
Shader "ZQXNXS/FaceDecal"
{
    Properties
    {
        _MainTex ("Atlas (RGBA)", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _ViewOffset ("View Offset (m)", Float) = 0.01
        _FaceForward ("Face Forward (object space)", Vector) = (0, 0, 1, 0)
        _Ambient ("Ambient Floor", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 100

        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            ZWrite Off
            ZTest LEqual
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _ViewOffset;
            float4 _FaceForward;
            half _Ambient;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half light : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert (appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                float3 viewPos = UnityObjectToViewPos(v.vertex);
                // 视空间里相机在原点、看向 -Z：朝相机前移 = 沿"顶点→原点"方向移动。
                float3 toCamera = normalize(-viewPos);
                float3 forwardView = normalize(mul((float3x3)UNITY_MATRIX_MV, _FaceForward.xyz));
                float facing = saturate(dot(forwardView, toCamera));
                viewPos += toCamera * (_ViewOffset * facing);
                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1.0));

                o.uv = TRANSFORM_TEX(v.uv, _MainTex);

                float3 worldNormal = UnityObjectToWorldNormal(v.normal);
                half ndl = saturate(dot(worldNormal, _WorldSpaceLightPos0.xyz));
                o.light = lerp(_Ambient, 1.0h, ndl);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.rgb *= i.light * _LightColor0.rgb + UNITY_LIGHTMODEL_AMBIENT.rgb * (1.0h - i.light);
                return c;
            }
            ENDCG
        }
    }

    Fallback Off
}
