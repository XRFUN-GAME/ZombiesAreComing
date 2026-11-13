using System.Collections.Generic;
using System.IO;
using System.Reflection;
using MoriGame.Ui;
using MoriGame.Zombies;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace MoriGame.Tests.EditMode
{
    /// <summary>WW UI 命中反馈（2026-11-12 用户定版）：僵尸头顶血条（受击显示/比例缩放/隐没）
    /// 与伤害数字池（数字生成/超容量复用不抛错）。</summary>
    public class UiHitFeedbackTests
    {
        private GameObject zombie;

        [TearDown]
        public void TearDown()
        {
            if (zombie != null)
            {
                Object.DestroyImmediate(zombie);
            }
        }

        [Test]
        public void 血条_受击显示且比例随血量()
        {
            zombie = new GameObject("TestZombie");
            var col = zombie.AddComponent<CapsuleCollider>();
            col.height = 1.7f;
            col.center = new Vector3(0f, 0.85f, 0f);
            const float width = 0.42f;
            var bar = ZombieHealthBar.Create(zombie.transform, width, 2.0f);
            Assert.IsFalse(bar.gameObject.activeSelf, "未受击不显示");

            bar.OnHit(0.5f);
            Assert.IsTrue(bar.gameObject.activeSelf, "受击后显示");
            var fill = bar.transform.Find("Fill");
            Assert.AreEqual(width * 0.9f * 0.5f, fill.localScale.x, 0.0001f, "填充宽=原始宽度×血量比例");

            bar.OnHit(1f);
            Assert.AreEqual(width * 0.9f, fill.localScale.x, 0.0001f, "满血回满");
        }

        [Test]
        public void 血条_零血与负血量不显示红条()
        {
            zombie = new GameObject("TestZombie");
            const float width = 0.42f;
            var bar = ZombieHealthBar.Create(zombie.transform, width, 2.0f);
            var fill = bar.transform.Find("Fill");

            bar.OnHit(0f);
            Assert.IsFalse(fill.gameObject.activeSelf, "零血必须完全隐藏红条");
            Assert.AreEqual(0f, fill.localScale.x, 0.0001f);

            bar.OnHit(-0.5f);
            Assert.IsFalse(fill.gameObject.activeSelf, "负血量同样按空血显示");

            bar.OnHit(0.25f);
            Assert.IsTrue(fill.gameObject.activeSelf, "后续受击仍能正常显示红条");
            Assert.AreEqual(width * 0.9f * 0.25f, fill.localScale.x, 0.0001f);
        }

        [Test]
        public void 血条_红条始终在内框中且高度不被重置()
        {
            zombie = new GameObject("TestZombie");
            const float width = 0.42f;
            var bar = ZombieHealthBar.Create(zombie.transform, width, 2.0f);
            var bg = bar.transform.Find("Bg").GetComponent<MeshRenderer>();
            var fill = bar.transform.Find("Fill");
            var fillRenderer = fill.GetComponent<MeshRenderer>();

            foreach (var fraction in new[] { 1f, 0.5f, 0f })
            {
                bar.OnHit(fraction);
                var bgBounds = bg.bounds;
                var fillBounds = fillRenderer.bounds;
                Assert.AreEqual(width * 0.08f, fill.localScale.y, 0.0001f, "红条高度保持创建值");
                Assert.GreaterOrEqual(fillBounds.min.x, bgBounds.min.x - 0.0001f, "红条左缘不得越框");
                Assert.LessOrEqual(fillBounds.max.x, bgBounds.max.x + 0.0001f, "红条右缘不得越框");
                Assert.GreaterOrEqual(fillBounds.min.y, bgBounds.min.y - 0.0001f, "红条下缘不得越框");
                Assert.LessOrEqual(fillBounds.max.y, bgBounds.max.y + 0.0001f, "红条上缘不得越框");
            }
        }

        [Test]
        public void 血条_红色填充不变灰且重置后隐藏()
        {
            zombie = new GameObject("TestZombie");
            var bar = ZombieHealthBar.Create(zombie.transform, 0.42f, 2.0f);
            var fill = bar.transform.Find("Fill").GetComponent<MeshRenderer>();
            var bg = bar.transform.Find("Bg").GetComponent<MeshRenderer>();

            bar.OnHit(0.35f);
            var fillColor = fill.sharedMaterial.color;
            Assert.Less(Mathf.Abs(fillColor.r - UiTheme.AccentRed.r) +
                        Mathf.Abs(fillColor.g - UiTheme.AccentRed.g) +
                        Mathf.Abs(fillColor.b - UiTheme.AccentRed.b), 0.0005f,
                "红条材质颜色必须保持 AccentRed");
            Assert.Greater(bg.sharedMaterial.color.r, bg.sharedMaterial.color.g * 2f,
                "空血底槽使用深红色，不显示灰色");

            bar.ResetDisplay();
            Assert.IsFalse(bar.gameObject.activeSelf, "池化重生时清除上一条命的显示状态");
            Assert.IsTrue(fill.gameObject.activeSelf);
            Assert.AreEqual(0.42f * 0.9f, fill.transform.localScale.x, 0.0001f);
        }

        [Test]
        public void 血条_类型名与红条左缘对齐并放大()
        {
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                "Assets/_MoriGame/Art/Fonts/MoriZhCN_SDF.asset");
            Assert.NotNull(font, "中文类型名字体资产必须存在");
            zombie = new GameObject("TestZombie");
            const float width = 0.42f;
            var bar = ZombieHealthBar.Create(zombie.transform, width, 2.0f, font, "普通感染者");
            var label = bar.transform.Find("TypeName");
            var tmp = label.GetComponent<TMPro.TextMeshPro>();

            Assert.AreEqual(-width * 0.9f * 0.5f, label.localPosition.x, 0.0001f,
                "名称枢轴必须落在红条左缘");
            Assert.AreEqual(0f, tmp.rectTransform.pivot.x, 0.0001f, "左对齐文本使用左缘枢轴");
            Assert.AreEqual(TMPro.TextAlignmentOptions.Left, tmp.alignment);
            Assert.AreEqual(width * 1.8f, tmp.fontSize, 0.0001f, "名称字号提高以保证 VR 可读性");
            Assert.AreEqual(TMPro.FontStyles.Bold, tmp.fontStyle);
        }

        [Test]
        public void 血条_无字体不生成类型名标签()
        {
            zombie = new GameObject("TestZombie");
            var bar = ZombieHealthBar.Create(zombie.transform, 0.30f, 2.0f, null, "普通感染者");
            Assert.Null(bar.transform.Find("TypeName"), "字体未注入时静默跳过标签（字体由迁移注入）");
        }

        [Test]
        public void 血条_独立排序组且内部绘制顺序固定()
        {
            var font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(
                "Assets/_MoriGame/Art/Fonts/MoriZhCN_SDF.asset");
            Assert.NotNull(font, "中文类型名字体资产必须存在");
            zombie = new GameObject("TestZombie");
            var bar = ZombieHealthBar.Create(zombie.transform, 0.42f, 2.0f, font, "普通感染者");

            Assert.NotNull(bar.GetComponent<SortingGroup>(), "每条血条必须是独立透明排序单元");
            Assert.AreEqual(0, bar.transform.Find("Back").GetComponent<MeshRenderer>().sortingOrder);
            Assert.AreEqual(1, bar.transform.Find("Bg").GetComponent<MeshRenderer>().sortingOrder);
            Assert.AreEqual(2, bar.transform.Find("Fill").GetComponent<MeshRenderer>().sortingOrder);
            Assert.AreEqual(3, bar.transform.Find("TypeName").GetComponent<TMPro.TextMeshPro>().renderer.sortingOrder);
        }

        [Test]
        public void 血条_普通感染者34款模型底缘均高于实际顶部()
        {
            var excluded = new HashSet<string>
            {
                "TT_zombie_M_15_punk.prefab",
                "TT_zombie_M_16_punk.prefab",
                "TT_zombie_M_17_punk.prefab",
                "TT_zombie_F_15_punk.prefab",
                "TT_zombie_F_16_punk.prefab"
            };
            var paths = new List<string>();
            foreach (var dir in new[]
                     {
                         "Assets/ToonyTinyPeople/TT_Zombies/prefabs/zombie_male",
                         "Assets/ToonyTinyPeople/TT_Zombies/prefabs/zombie_female"
                     })
            {
                foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { dir }))
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (!excluded.Contains(Path.GetFileName(path)) && !paths.Contains(path))
                    {
                        paths.Add(path);
                    }
                }
            }
            paths.Sort();
            Assert.AreEqual(34, paths.Count, "普通感染者随机池必须覆盖排除五款朋克后的全部模型");

            var headMethod = typeof(ZombieAgent).GetMethod("HitFeedbackHeadLocalY",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(headMethod);
            foreach (var path in paths)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.NotNull(prefab, path);
                zombie = new GameObject("TestZombie_" + Path.GetFileNameWithoutExtension(path));
                var navAgent = zombie.AddComponent<UnityEngine.AI.NavMeshAgent>();
                navAgent.enabled = false;
                const float height = 1.7f * 0.6f;
                const float legHeight = height * 0.38f;
                const float bodyTop = height * 0.78f;
                var col = zombie.AddComponent<CapsuleCollider>();
                col.direction = 1;
                col.radius = 0.28f * 0.6f;
                col.height = bodyTop - legHeight;
                col.center = new Vector3(0f, (bodyTop + legHeight) * 0.5f, 0f);
                var agent = zombie.AddComponent<ZombieAgent>();
                var visual = Object.Instantiate(prefab, zombie.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * 0.6f;
                visual.transform.localPosition = new Vector3(0f,
                    ZombiePool.CalculateFootLocalY(visual.transform, zombie.transform, 0f), 0f);
                agent.visualRoot = visual.transform;
                Physics.SyncTransforms();

                var visualTopWorld = float.MinValue;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(false))
                {
                    if (renderer.enabled)
                    {
                        visualTopWorld = Mathf.Max(visualTopWorld, renderer.bounds.max.y);
                    }
                }
                Assert.Greater(visualTopWorld, float.MinValue, "模型没有启用的 Renderer：" + path);
                var visualTopLocal = visualTopWorld - zombie.transform.position.y;
                var headLocalY = (float)headMethod.Invoke(agent, null);
                Assert.GreaterOrEqual(headLocalY, visualTopLocal + 0.22f - 0.0001f,
                    "头顶高度必须覆盖当前模型实际渲染顶部：" + path);

                const float width = 0.42f * 0.6f;
                var bar = ZombieHealthBar.Create(zombie.transform, width, headLocalY + 0.16f);
                var barBottom = bar.transform.localPosition.y - width * 0.16f * 0.5f;
                Assert.GreaterOrEqual(barBottom, visualTopLocal - 0.0001f,
                    "血条底缘不得穿进模型：" + path);
                Object.DestroyImmediate(zombie);
                zombie = null;
            }
        }

        [Test]
        public void 伤害数字_半尺寸血条红且超容量不抛错()
        {
            var origin = new Vector3(1f, 1.5f, 1f);
            DamageNumbers.Spawn(origin, 8, false);
            DamageNumbers.Spawn(origin, 16, true);
            var numbers = Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None);
            var normal = System.Array.Find(numbers, text => text.text == "+8");
            var weakpoint = System.Array.Find(numbers, text => text.text == "+16");
            Assert.NotNull(normal);
            Assert.NotNull(weakpoint);
            Assert.AreEqual(0.056f, normal.characterSize, 0.00001f);
            Assert.AreEqual(0.056f, weakpoint.characterSize, 0.00001f);
            Assert.AreEqual(UiTheme.AccentRed.r, normal.color.r, 0.002f);
            Assert.AreEqual(UiTheme.AccentRed.g, normal.color.g, 0.002f);
            Assert.AreEqual(UiTheme.AccentRed.b, normal.color.b, 0.002f);
            Assert.AreEqual(UiTheme.AccentRed.r, weakpoint.color.r, 0.002f);
            Assert.AreEqual(UiTheme.AccentRed.g, weakpoint.color.g, 0.002f);
            Assert.AreEqual(UiTheme.AccentRed.b, weakpoint.color.b, 0.002f);
            Assert.AreEqual(1f, normal.transform.localScale.x, 0.00001f);
            Assert.AreEqual(1.3f, weakpoint.transform.localScale.x, 0.00001f);
            // 超容量（24 槽 ×3）复用最旧——只验证不抛错且场景内对象存在
            for (var i = 0; i < 72; i++)
            {
                DamageNumbers.Spawn(origin, i, i % 5 == 0);
            }
            Assert.Greater(Object.FindObjectsByType<TextMesh>(FindObjectsSortMode.None).Length, 0,
                "伤害数字 TextMesh 已生成");
        }
    }
}
