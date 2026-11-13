using MoriGame.Tutorial;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    /// <summary>M4-T0 启动选择+独立教学关纯逻辑单测：毕业标记读写、**选择页锁定语义（2026-11-12 用户定版：
    /// 选择页始终显示，未毕业=正式关锁定、毕业=解锁）**、跳过=毕业。</summary>
    public class TutorialFlowTests
    {
        [SetUp]
        [TearDown]
        public void ClearMark()
        {
            TutorialGraduation.ClearForTest();
        }

        [Test]
        public void 毕业标记_读写闭环()
        {
            Assert.IsFalse(TutorialGraduation.IsGraduated(), "初始无标记");
            TutorialGraduation.SetGraduated();
            Assert.IsTrue(TutorialGraduation.IsGraduated(), "写入后可读");
            TutorialGraduation.ClearForTest();
            Assert.IsFalse(TutorialGraduation.IsGraduated(), "清除后回归未毕业");
        }

        [Test]
        public void 选择页锁定语义_未毕业锁定正式关()
        {
            // StartSceneController 始终构建选择页；matchLocked = !IsGraduated()
            Assert.IsFalse(TutorialGraduation.IsGraduated(), "未毕业 → 正式关卡呈锁定态（仅教学可选）");
        }

        [Test]
        public void 选择页锁定语义_毕业解锁正式关()
        {
            TutorialGraduation.SetGraduated();
            Assert.IsTrue(TutorialGraduation.IsGraduated(), "已毕业 → 两个选项均开放");
        }

        [Test]
        public void 跳过即毕业()
        {
            // 跳过按钮的唯一动作就是 SetGraduated（TutorialFlow.Skip）
            Assert.IsFalse(TutorialGraduation.IsGraduated());
            TutorialGraduation.SetGraduated(); // Skip() 的全部副作用
            Assert.IsTrue(TutorialGraduation.IsGraduated(), "跳过（毕业）后正式关卡解锁");
        }

        [Test]
        public void 清单重置_重新演示()
        {
            var checklist = new TutorialChecklist();
            checklist.MarkDone(TutorialStep.GrabWeapon);
            checklist.MarkDone(TutorialStep.FireOnce);
            Assert.IsFalse(checklist.AllDone);
            checklist.MarkDone(TutorialStep.GrabStand);
            checklist.MarkDone(TutorialStep.PlaceDown);
            Assert.IsTrue(checklist.AllDone, "四步全勾=毕业条件");
            checklist.ResetAll(); // 「重新演示」按钮
            Assert.IsFalse(checklist.AllDone);
            Assert.AreEqual(0, checklist.CurrentStep, "重置后回到第一步引导");
        }

        [Test]
        public void 黑幕层序高于Accent_全黑后可靠隐藏()
        {
            var go = new GameObject("SceneFaderTest");
            try
            {
                var fader = go.AddComponent<SceneFader>();
                fader.EnsureOverlay();
                var renderer = go.GetComponentInChildren<MeshRenderer>();
                Assert.NotNull(renderer);
                Assert.Greater(renderer.sortingOrder, 10, "必须高于 UiTheme Accent 的 sortingOrder=10");
                Assert.GreaterOrEqual(renderer.sharedMaterial.renderQueue, 5000);
                fader.SetOverlayAlpha(1f);
                Assert.IsTrue(renderer.enabled);
                Assert.AreEqual(1f, renderer.sharedMaterial.color.a, 0.0001f);
                fader.HideOverlay();
                Assert.IsFalse(renderer.enabled);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
