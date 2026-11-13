using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoriGame.Tutorial
{
    /// <summary>场景切换淡入淡出：黑幕宿主 DontDestroyOnLoad 持久化，黑场贯穿异步加载全程；
    /// 加载完成并等到新场景主相机后由 TransitionRoutine 统一淡入，不再依赖各场景自行收尾。
    /// StartScene/Tutorial 的 PlayFadeIn 保留给冷启动，transitioning 防止与切换淡入形成双协程。</summary>
    public static class SceneLoader
    {
        private const float FadeSeconds = 0.6f;
        private static SceneFader active;
        private static bool transitioning;

        public static void FadeTo(string sceneName)
        {
            if (transitioning)
            {
                return;
            }
            var fader = EnsureFader();
            transitioning = true;
            fader.StartCoroutine(TransitionRoutine(fader, sceneName));
        }

        /// <summary>冷启动淡入；场景切换由 TransitionRoutine 统一收尾，旧场景启动调用自动去重</summary>
        public static void PlayFadeIn()
        {
            if (transitioning)
            {
                return;
            }
            var fader = EnsureFader();
            fader.StartCoroutine(FadeInRoutine(fader));
        }

        private static SceneFader EnsureFader()
        {
            if (active != null)
            {
                return active;
            }
            var go = new GameObject("SceneFadePersistent");
            UnityEngine.Object.DontDestroyOnLoad(go);
            active = go.AddComponent<SceneFader>();
            return active;
        }

        private static IEnumerator TransitionRoutine(SceneFader fader, string sceneName)
        {
            fader.EnsureOverlay();
            fader.SetOverlayAlpha(0f);
            var t = 0f;
            while (t < FadeSeconds)
            {
                t += Time.unscaledDeltaTime;
                fader.SetOverlayAlpha(Mathf.Clamp01(t / FadeSeconds));
                yield return null;
            }
            fader.SetOverlayAlpha(1f);
            var op = SceneManager.LoadSceneAsync(sceneName);
            while (op != null && !op.isDone)
            {
                yield return null;
            }
            yield return null;
            while (Camera.main == null)
            {
                yield return null;
            }
            yield return FadeInRoutine(fader);
            transitioning = false;
        }

        private static IEnumerator FadeInRoutine(SceneFader fader)
        {
            fader.EnsureOverlay();
            fader.SetOverlayAlpha(1f);
            var t = FadeSeconds;
            while (t > 0f)
            {
                t -= Time.unscaledDeltaTime;
                fader.SetOverlayAlpha(Mathf.Clamp01(t / FadeSeconds));
                yield return null;
            }
            fader.HideOverlay();
        }
    }

    /// <summary>黑幕宿主：持久 GameObject + **世界空间黑幕四边形**（每帧吸附当前场景主相机前方 0.25 米）。
    /// 2026-11-12 用户实测所有场景切换无黑屏——Screen Space Overlay Canvas 在 XR 单通道实例化渲染下不可靠，
    /// 世界空间四边形所有 VR 渲染路径稳定（透明队列里最近者最后画，盖过一切 UI 与物体）。</summary>
    public sealed class SceneFader : MonoBehaviour
    {
        private const float Distance = 0.25f;
        private Transform quad;
        private MeshRenderer quadRenderer;
        private Material mat;

        public void EnsureOverlay()
        {
            if (mat != null)
            {
                return;
            }
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad).transform;
            var quadCollider = quad.GetComponent<Collider>();
            if (Application.isPlaying)
            {
                Destroy(quadCollider);
            }
            else
            {
                DestroyImmediate(quadCollider);
            }
            quad.name = "SceneFadeQuad";
            quad.SetParent(transform, false);
            quad.localScale = new Vector3(3f, 3f, 1f); // 0.25 米外覆盖 ≥120° 视场，任何头显 FOV 都罩满
            mat = new Material(Shader.Find("Sprites/Default")) { color = Color.black, renderQueue = 5000 };
            quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = mat;
            quadRenderer.sortingOrder = short.MaxValue;
            quadRenderer.enabled = false;
        }

        public void SetOverlayAlpha(float a)
        {
            EnsureOverlay();
            quadRenderer.enabled = true;
            var c = mat.color;
            c.a = a;
            mat.color = c;
        }

        public void HideOverlay()
        {
            if (quadRenderer != null)
            {
                quadRenderer.enabled = false;
            }
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || quadRenderer == null || !quadRenderer.enabled)
            {
                return;
            }
            quad.position = cam.transform.position + cam.transform.forward * Distance;
            quad.rotation = cam.transform.rotation * Quaternion.Euler(0f, 180f, 0f); // Quad 正面 +z 背向相机，翻转朝向玩家
        }
    }
}
