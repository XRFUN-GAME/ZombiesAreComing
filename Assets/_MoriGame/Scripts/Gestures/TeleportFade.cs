using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace MoriGame.Gestures
{
    /// <summary>
    /// 传送黑幕（2026-11-02 传送整改）：Screen Space Overlay Canvas + 黑色 Image，
    /// 总 0.3 秒（淡出 0.15s → 黑屏峰值执行传送回调 → 淡入 0.15s），防瞬移眩晕。
    /// </summary>
    public class TeleportFade : MonoBehaviour
    {
        public static TeleportFade Instance { get; private set; }

        public Image fadeImage;
        public float fadeOutSeconds = 0.15f;
        public float fadeInSeconds = 0.15f;

        private Coroutine active;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>淡出 → onBlack（黑屏峰值执行传送）→ 淡入。重复调用会重启前先完成淡入，避免卡黑。</summary>
        public void FadeOutIn(System.Action onBlack)
        {
            if (active != null)
            {
                StopCoroutine(active);
            }
            active = StartCoroutine(FadeRoutine(onBlack));
        }

        private IEnumerator FadeRoutine(System.Action onBlack)
        {
            if (fadeImage == null)
            {
                onBlack?.Invoke();
                yield break;
            }
            yield return Fade(0f, 1f, fadeOutSeconds);
            onBlack?.Invoke();
            yield return Fade(1f, 0f, fadeInSeconds);
            active = null;
        }

        private IEnumerator Fade(float from, float to, float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.unscaledDeltaTime;
                var color = fadeImage.color;
                color.a = Mathf.Lerp(from, to, elapsed / seconds);
                fadeImage.color = color;
                yield return null;
            }
        }
    }
}
