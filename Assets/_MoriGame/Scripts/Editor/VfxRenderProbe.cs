using System.Collections;
using MoriGame.Vfx;
using UnityEditor;
using UnityEngine;

namespace MoriGame.Editor
{
    /// <summary>特效渲染探针（2026-11-11 诊断"真机全没特效"）：Play 模式在主相机前实例化 muzzleShot，
    /// 渲染一帧读 RT 像素 + isVisible + aliveParticles 三向证据——区分「没发射/被剔除/材质不渲染/平台剥离」。</summary>
    public static class VfxRenderProbe
    {
        public static IEnumerator Run(System.Action<string> log)
        {
            yield return null; // 等 Play 初始化/bootstrap Awake
            var bootstrap = MatchBootstrap.Instance;
            if (bootstrap == null || bootstrap.Vfx == null || bootstrap.vfxLibrary == null)
            {
                log("PROBE_FAIL bootstrap/Vfx/library 未就绪");
                VfxRenderProbeAudit.Finish(false, "bootstrap-not-ready");
                yield break;
            }
            var lib = bootstrap.vfxLibrary;
            log($"PROBE library muzzleShot={(lib.muzzleShot?.prefab != null ? lib.muzzleShot.prefab.name : "NULL")} scale={lib.muzzleShot?.scale}");

            var cam = Camera.main;
            if (cam == null)
            {
                log("PROBE_FAIL 无主相机");
                VfxRenderProbeAudit.Finish(false, "no-camera");
                yield break;
            }
            var spot = cam.transform.position + cam.transform.forward * 0.6f;

            var prefab = lib.muzzleShot != null ? lib.muzzleShot.prefab : null;
            if (prefab == null)
            {
                log("PROBE_FAIL muzzleShot prefab 空");
                VfxRenderProbeAudit.Finish(false, "no-prefab");
                yield break;
            }

            // 直连实例化（绕过池，排除池逻辑干扰）做渲染判定
            var probe = Object.Instantiate(prefab, spot, Quaternion.identity);
            probe.transform.localScale = Vector3.one * lib.muzzleShot.scale;
            foreach (var ps in probe.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Simulate(0f, true, true);
                ps.Play(true);
            }

            // 同时走池路径对比
            VfxBus.Play(l => l.muzzleShot, spot + Vector3.up * 0.3f);

            yield return null;
            yield return new WaitForSeconds(0.25f);

            var alive = 0;
            var playing = 0;
            var visible = false;
            foreach (var ps in probe.GetComponentsInChildren<ParticleSystem>(true))
            {
                alive += ps.particleCount;
                if (ps.isPlaying)
                {
                    playing++;
                }
            }
            foreach (var r in probe.GetComponentsInChildren<Renderer>(true))
            {
                if (r.isVisible)
                {
                    visible = true;
                }
                log($"PROBE renderer={r.GetType().Name} enabled={r.enabled} visible={r.isVisible} " +
                    $"shader={(r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "NULL")} " +
                    $"layer={r.gameObject.layer}");
            }

            // 渲染读像素：相机渲到小 RT，统计非背景色像素
            var rt = new RenderTexture(128, 128, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, 128, 128), 0, 0);
            tex.Apply();
            cam.targetTexture = null;
            RenderTexture.active = null;

            var baseColor = tex.GetPixel(2, 2);
            var nonBg = 0;
            for (var y = 16; y < 112; y++)
            {
                for (var x = 16; x < 112; x++)
                {
                    var c = tex.GetPixel(x, y);
                    if (Mathf.Abs(c.r - baseColor.r) + Mathf.Abs(c.g - baseColor.g) + Mathf.Abs(c.b - baseColor.b) > 0.25f)
                    {
                        nonBg++;
                    }
                }
            }
            log($"PROBE 渲染：aliveParticles={alive} playing={playing} isVisible={visible} 非背景像素={nonBg}/9216 基色={baseColor}");

            // 同池实例对照
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                if (ps.transform.root.name.StartsWith("FX_"))
                {
                    var r = ps.GetComponent<Renderer>();
                    log($"PROBE 池实例 {ps.transform.root.name} alive={ps.particleCount} visible={(r != null && r.isVisible)}");
                }
            }

            Object.Destroy(probe);
            Object.Destroy(tex);
            rt.Release();
            VfxRenderProbeAudit.Finish(true, "probe-complete");
        }
    }
}
