using UnityEngine;

namespace MoriGame.Ui
{
    /// <summary>命中伤害数字池：世界空间 TextMesh，上飘 + 微横漂 + 淡出；
    /// 普通/弱点统一血条红，弱点保留 1.3 倍强调；同槽复用丢最旧，
    /// 跨场景由持久 runner 宿主，场景卸载后的残留槽自动重建。</summary>
    public static class DamageNumbers
    {
        private const int Capacity = 24;
        private const float LifeSeconds = 0.75f;
        private const float RiseMeters = 0.8f;

        private struct Item
        {
            public TextMesh Text;
            public Color BaseColor;
            public Vector3 Origin;
            public Vector3 Drift;
            public float Born;
        }

        private static Item[] items;
        private static int next;
        private static Runner runner;

        public static void Spawn(Vector3 position, int amount, bool weakpoint)
        {
            if (items == null)
            {
                items = new Item[Capacity];
            }
            if (runner == null)
            {
                var host = new GameObject("DamageNumbers");
                if (Application.isPlaying)
                {
                    Object.DontDestroyOnLoad(host); // EditMode 测试无 play 模式不持久化
                }
                runner = host.AddComponent<Runner>();
            }
            var slot = next;
            next = (next + 1) % Capacity;
            var text = items[slot].Text;
            if (text == null)
            {
                var go = new GameObject("DamageNumber");
                go.transform.SetParent(runner.transform, false);
                var renderer = go.AddComponent<MeshRenderer>();
                text = go.AddComponent<TextMesh>();
                text.fontSize = 42;
                text.characterSize = 0.056f;
                text.anchor = TextAnchor.MiddleCenter;
                text.alignment = TextAlignment.Center;
                text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                renderer.sharedMaterial = text.font.material;
            }
            items[slot].Text = text;
            items[slot].BaseColor = UiTheme.AccentRed;
            items[slot].Origin = position + new Vector3(
                Random.Range(-0.08f, 0.08f), Random.Range(0f, 0.1f), Random.Range(-0.08f, 0.08f));
            items[slot].Drift = new Vector3(Random.Range(-0.2f, 0.2f), 0f, Random.Range(-0.2f, 0.2f));
            items[slot].Born = Time.unscaledTime;
            text.gameObject.SetActive(true);
            text.color = items[slot].BaseColor;
            text.text = "+" + amount;
            text.transform.localScale = Vector3.one * (weakpoint ? 1.3f : 1f);
            text.transform.position = items[slot].Origin;
        }

        private sealed class Runner : MonoBehaviour
        {
            private void LateUpdate()
            {
                if (items == null)
                {
                    return;
                }
                var cam = Camera.main;
                var camRot = cam != null ? cam.transform.rotation : Quaternion.identity;
                for (var i = 0; i < items.Length; i++)
                {
                    var item = items[i];
                    if (item.Text == null || !item.Text.gameObject.activeSelf)
                    {
                        continue;
                    }
                    var age = Time.unscaledTime - item.Born;
                    if (age >= LifeSeconds)
                    {
                        item.Text.gameObject.SetActive(false);
                        continue;
                    }
                    var t = age / LifeSeconds;
                    item.Text.transform.position = item.Origin + item.Drift * age +
                                                   Vector3.up * (RiseMeters * (1f - (1f - t) * (1f - t)));
                    item.Text.transform.rotation = camRot; // billboard 朝向玩家（用户 2026-11-12：数字不朝人）
                    var c = item.BaseColor;
                    c.a = 1f - t * t;
                    item.Text.color = c;
                }
            }
        }
    }
}
