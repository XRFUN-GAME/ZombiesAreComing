using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoriGame.Ui
{
    /// <summary>僵尸头顶血条（2026-11-12 用户定版）：受击显示，4 秒无伤隐没；
    /// 米白框 + 深红底 + 红填充（左锚缩放，零血隐藏填充）；水平 billboard 朝主相机（VR 世界空间，不俯仰跟随防歪斜）；
    /// 宽度=类型体型比例；类型名 TMP 与红条左缘对齐，加粗显示。</summary>
    public class ZombieHealthBar : MonoBehaviour
    {
        private Transform fill;
        private float fillWidth;
        private float hideAt;
        private const float ShowSeconds = 4f;

        public static ZombieHealthBar Create(Transform zombieRoot, float width, float topY,
            TMP_FontAsset nameFont = null, string displayName = null)
        {
            var go = new GameObject("HealthBar");
            go.transform.SetParent(zombieRoot, false);
            go.transform.localPosition = new Vector3(0f, topY, 0f);
            go.AddComponent<SortingGroup>(); // 每条血条作为独立透明排序单元，避免不同僵尸的子层互相穿插
            UiTheme.Quad(go.transform, "Back", UiTheme.PanelBorder, new Vector2(width, width * 0.16f), 0.004f)
                .GetComponent<MeshRenderer>().sortingOrder = 0;
            UiTheme.Quad(go.transform, "Bg", new Color(0.12f, 0.02f, 0.015f, 1f),
                    new Vector2(width * 0.94f, width * 0.12f), 0.002f)
                .GetComponent<MeshRenderer>().sortingOrder = 1;
            var fillGo = UiTheme.Quad(go.transform, "Fill", UiTheme.AccentRed,
                new Vector2(width * 0.9f, width * 0.08f), 0f);
            fillGo.GetComponent<MeshRenderer>().sortingOrder = 2;
            var bar = go.AddComponent<ZombieHealthBar>();
            bar.fill = fillGo.transform;
            bar.fillWidth = width * 0.9f;
            if (nameFont != null && !string.IsNullOrEmpty(displayName))
            {
                var label = new GameObject("TypeName");
                label.transform.SetParent(go.transform, false);
                label.transform.localPosition = new Vector3(-bar.fillWidth * 0.5f, width * 0.17f + 0.015f, -0.004f);
                var tmp = label.AddComponent<TextMeshPro>();
                tmp.font = nameFont; // 中文字体图集（迁移注入；缺字形由迁移 TryAddCharacters 自愈）
                tmp.text = displayName;
                tmp.fontSize = width * 1.8f;
                tmp.color = UiTheme.TextMain;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Left;
                tmp.enableWordWrapping = false;
                tmp.rectTransform.pivot = new Vector2(0f, 0.5f);
                tmp.rectTransform.sizeDelta = new Vector2(bar.fillWidth, width * 0.3f);
                tmp.renderer.sortingOrder = 3;
            }
            go.SetActive(false);
            return bar;
        }

        /// <summary>受击刷新（fraction=0—1）；满血满盾调用也安全</summary>
        public void OnHit(float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            fill.gameObject.SetActive(fraction > 0f);
            var scale = fill.localScale;
            scale.x = fillWidth * fraction;
            fill.localScale = scale;
            fill.localPosition = new Vector3(-(1f - fraction) * fillWidth * 0.5f, 0f, 0f);
            hideAt = Time.time + ShowSeconds;
            gameObject.SetActive(true);
        }

        public void ResetDisplay()
        {
            hideAt = 0f;
            fill.localScale = new Vector3(fillWidth, fill.localScale.y, 1f);
            fill.localPosition = Vector3.zero;
            fill.gameObject.SetActive(true);
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (Time.time > hideAt)
            {
                gameObject.SetActive(false);
                return;
            }
            var cam = Camera.main;
            if (cam != null)
            {
                var e = cam.transform.eulerAngles;
                transform.rotation = Quaternion.Euler(0f, e.y, 0f);
            }
        }
    }
}
