using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MoriGame.Tests.EditMode
{
    /// <summary>建筑落地视觉预制体契约（2026-11-12 正式模型）：
    /// 电网/机枪塔预制体可加载且含渲染体；机枪塔含 Muzzle 枪口子节点（迁移补建，开火特效出点面板可调）。</summary>
    public class PropVisualPrefabTests
    {
        [Test]
        public void 电网预制体可加载且含渲染体()
        {
            var fence = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/资产/NEW/预制体/Prop_电网.prefab");
            Assert.NotNull(fence, "Prop_电网.prefab 未找到");
            Assert.Greater(fence.GetComponentsInChildren<MeshRenderer>(true).Length, 0, "电网预制体无渲染体");
        }

        [Test]
        public void 机枪塔预制体含Muzzle枪口节点()
        {
            var turret = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/资产/NEW/预制体/Prop_机枪塔.prefab");
            Assert.NotNull(turret, "Prop_机枪塔.prefab 未找到");
            Assert.NotNull(turret.transform.Find("Muzzle"),
                "机枪塔预制体缺 Muzzle 枪口节点（重跑 migrate-realspace 触发补建）");
            Assert.Greater(turret.GetComponentsInChildren<MeshRenderer>(true).Length, 0, "机枪塔预制体无渲染体");
        }
    }
}
