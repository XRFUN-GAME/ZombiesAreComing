using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace MoriGame.Editor
{
    /// <summary>PolygonParticles 材质批量转 URP（2026-11-11 特效系统）：
    /// 抽查实证 41/50 材质挂内置 Standard 粒子 shader（URP 下渲染失败），9 个已是 URP/Particles/Unlit。
    /// 转换：保留主贴图/主色（经 mainTexture/color 属性走 [MainTexture]/[MainColor] 重映射到 _BaseMap/_BaseColor）；
    /// 混合模式按旧 keyword 推导：_ALPHAPREMULTIPLY_ON→Premultiply、_ALPHATEST_ON→Opaque+裁切、其余→Alpha；
    /// 触发通道 vfx-shader.trigger，幂等（已是 Universal 开头即跳过）。</summary>
    public static class VfxShaderConverter
    {
        public static void ConvertAll()
        {
            var urp = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (urp == null)
            {
                Debug.LogError("[MoriGame] 找不到 URP/Particles/Unlit，中止转换");
                return;
            }
            var converted = 0;
            var skipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/PolygonParticles/Materials" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null)
                {
                    continue;
                }
                if (mat.shader.name.StartsWith("Universal"))
                {
                    skipped++;
                    continue;
                }
                var keywords = new HashSet<string>(mat.shaderKeywords);
                var tex = mat.mainTexture;
                var color = mat.color;
                var alphatest = keywords.Contains("_ALPHATEST_ON");
                var premultiply = keywords.Contains("_ALPHAPREMULTIPLY_ON");

                mat.shader = urp;
                mat.mainTexture = tex;
                mat.color = color;

                // URP Particles/Unlit 表面参数（2026-11-11：keyword 推导，免逐材质手工设）
                mat.SetFloat("_Surface", alphatest ? 0f : 1f); // 0=Opaque 1=Transparent
                mat.SetFloat("_Blend", premultiply ? 1f : 0f); // 0=Alpha 1=Premultiply 2=Multiply 3=Additive
                mat.SetFloat("_Cutoff", 0.5f);
                if (alphatest)
                {
                    mat.EnableKeyword("_ALPHATEST_ON");
                    mat.SetOverrideTag("RenderType", "TransparentCutout");
                    mat.renderQueue = 2450;
                }
                else
                {
                    mat.DisableKeyword("_ALPHATEST_ON");
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.SetOverrideTag("RenderType", "Transparent");
                    mat.renderQueue = 3000;
                    mat.SetShaderPassEnabled("DepthOnly", false);
                }
                EditorUtility.SetDirty(mat);
                converted++;
                Debug.Log($"[MoriGame] 材质转 URP：{mat.name}（{(alphatest ? "Opaque+Cutoff" : premultiply ? "Premultiply" : "Alpha")}）");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[MoriGame] 特效材质转换完成：转 {converted} 跳过（已 URP）{skipped}");

            // FX prefab 深度清理（2026-11-11 P0-②③）：内置材质 Renderer 替换为库内 URP 材质 + Light 组件禁用
            var fallback = AssetDatabase.LoadAssetAtPath<Material>("Assets/PolygonParticles/Materials/PolygonParticleFX_Default.mat");
            var lightsDisabled = 0;
            var builtinMatFixed = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/PolygonParticles/Prefabs" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null)
                {
                    continue;
                }
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = renderer.sharedMaterials;
                    var changed = false;
                    for (var i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || m.shader == null || m.shader.name.StartsWith("Universal"))
                        {
                            continue;
                        }
                        // 内置 shader（Sprites/Default 等保底可用，但内置 Default-Material/粒子在 URP 下不可靠）统一替换
                        mats[i] = fallback;
                        changed = true;
                        builtinMatFixed++;
                    }
                    if (changed)
                    {
                        renderer.sharedMaterials = mats;
                        EditorUtility.SetDirty(renderer);
                    }
                }
                foreach (var light in prefab.GetComponentsInChildren<Light>(true))
                {
                    light.enabled = false;
                    EditorUtility.SetDirty(light);
                    lightsDisabled++;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[MoriGame] FX 深度清理：内置材质替换 {builtinMatFixed}，Light 禁用 {lightsDisabled}");

            // 抽查实例化验证：三个代表性 prefab 的材质应全部 Universal 开头
            foreach (var name in new[] { "FX_Gunshot_Repeating", "FX_Poison_Green_01", "FX_Explosion_Body_Bloody_01" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PolygonParticles/Prefabs/" + name + ".prefab");
                if (prefab == null)
                {
                    continue;
                }
                var bad = 0;
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        // fileID:0 空槽（未用子网格）不渲染不算粉
                        if (mat != null && (mat.shader == null || !mat.shader.name.StartsWith("Universal")))
                        {
                            bad++;
                        }
                    }
                }
                var systems = prefab.GetComponentsInChildren<ParticleSystem>(true).Length;
                Debug.Log($"[MoriGame] 抽查 {name}：ParticleSystem×{systems}，非 URP 材质 {bad}");
            }
        }
    }
}
