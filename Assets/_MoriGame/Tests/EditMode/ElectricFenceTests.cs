using System.Collections.Generic;
using System.Reflection;
using MoriGame.Build;
using MoriGame.Configs;
using MoriGame.Territory;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace MoriGame.Tests.EditMode
{
    public class ElectricFenceTests
    {
        private readonly List<GameObject> objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in objects)
            {
                if (go != null)
                {
                    Object.DestroyImmediate(go);
                }
            }
            objects.Clear();
        }

        [Test]
        public void 区域边界_四乘一格精确含边()
        {
            var center = new Vector3(2f, 0f, -1f);
            var extents = new Vector2(1f, 0.25f);
            Assert.IsTrue(ElectricFence.ContainsPoint(center, extents, center + new Vector3(1f, 5f, 0.25f), Quaternion.identity),
                "占地边界含，Y 不影响水平区域");
            Assert.IsFalse(ElectricFence.ContainsPoint(center, extents, center + new Vector3(1.001f, 0f, 0f), Quaternion.identity));
            Assert.IsFalse(ElectricFence.ContainsPoint(center, extents, center + new Vector3(0f, 0f, 0.251f), Quaternion.identity));
        }

        [Test]
        public void 区域边界_旋转九零度判定跟随()
        {
            var center = Vector3.zero;
            var extents = new Vector2(1f, 0.25f); // 2 米 ×0.5 米长条
            var rot90 = Quaternion.Euler(0f, 90f, 0f); // 转 90° 后长边朝 Z
            Assert.IsFalse(ElectricFence.ContainsPoint(center, extents, new Vector3(0.9f, 0f, 0f), rot90),
                "转 90° 后 X 向 0.9 不再在区域内（旧轴对齐判定会误含）");
            Assert.IsTrue(ElectricFence.ContainsPoint(center, extents, new Vector3(0f, 0f, 0.9f), rot90),
                "转 90° 后 Z 向 0.9 在区域内");
            Assert.IsTrue(ElectricFence.ContainsPoint(center, extents, new Vector3(0.9f, 0f, 0f), Quaternion.identity),
                "未旋转时 X 向 0.9 仍在区域内");
        }

        [Test]
        public void 正式模型_四档旋转与占地中心方向一致()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/资产/NEW/预制体/Prop_电网.prefab");
            var config = AssetDatabase.LoadAssetAtPath<PropConfig>("Assets/_MoriGame/Configs/Props/Prop_ElectricFence.asset");
            Assert.NotNull(prefab);
            Assert.NotNull(config);
            var managerGo = new GameObject("TestBuildManager");
            objects.Add(managerGo);
            var manager = managerGo.AddComponent<BuildManager>();
            manager.fenceVisualPrefab = prefab;
            var type = typeof(BuildManager);
            type.GetField("placing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, config);
            var create = type.GetMethod("CreateFenceEntity", BindingFlags.Instance | BindingFlags.NonPublic);
            var hitExtents = type.GetMethod("FenceHitHalfExtents", BindingFlags.Static | BindingFlags.NonPublic);
            var anchor = new CellCoord(0, 0);
            var mismatches = new List<string>();
            var baseModelSize = Vector3.zero;
            void Check(float expected, float actual, float tolerance, string label)
            {
                if (Mathf.Abs(expected - actual) > tolerance)
                {
                    mismatches.Add($"{label}: expected={expected:0.####}, actual={actual:0.####}");
                }
            }

            for (var rotation = 0; rotation < 4; rotation++)
            {
                var entity = (GameObject)create.Invoke(manager, new object[] { anchor, rotation });
                objects.Add(entity);
                var cells = PlacementRules.FootprintCells(anchor, 4, 1, rotation);
                var footprintRect = PlacementRules.FootprintWorldRect(cells);
                var expectedCenter = new Vector3(footprintRect.center.x, TerritoryGrid.WorldFloorY, footprintRect.center.y);
                var expectedSize = PlacementRules.FootprintScale(4, 1, rotation, 1f);
                var renderers = entity.GetComponentsInChildren<MeshRenderer>();
                Assert.Greater(renderers.Length, 0);
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
                var fence = entity.GetComponent<ElectricFence>();
                var obstacle = entity.GetComponent<NavMeshObstacle>();
                var actualHit = (Vector2)hitExtents.Invoke(null, new object[] { entity });
                var prop = entity.AddComponent<PropBehaviour>();
                prop.Configure(manager, config, actualHit, expectedSize.y);
                var hitPoint = prop.ClosestPoint(expectedCenter + new Vector3(10f, 0f, 10f));
                if (rotation == 0)
                {
                    baseModelSize = bounds.size;
                }
                Debug.Log($"[MoriGame][FenceRotationAudit] rotation={rotation} modelCenter={bounds.center} modelSize={bounds.size} " +
                    $"footprintCenter={expectedCenter} footprintSize={expectedSize} " +
                    $"entityCenter={entity.transform.position} damageHalf={fence.halfExtents} " +
                    $"obstacleSize={obstacle.size} hitHalf={actualHit}");

                Check(expectedCenter.x, entity.transform.position.x, 0.001f, $"rotation={rotation} 实体中心 X");
                Check(expectedCenter.z, entity.transform.position.z, 0.001f, $"rotation={rotation} 实体中心 Z");
                Check(expectedSize.x * 0.5f, fence.halfExtents.x, 0.001f, $"rotation={rotation} 伤害 X");
                Check(expectedSize.z * 0.5f, fence.halfExtents.y, 0.001f, $"rotation={rotation} 伤害 Z");
                Check(expectedSize.x, obstacle.size.x, 0.001f, $"rotation={rotation} 阻挡 X");
                Check(expectedSize.z, obstacle.size.z, 0.001f, $"rotation={rotation} 阻挡 Z");
                Check(bounds.size.x * 0.5f, actualHit.x, 0.001f, $"rotation={rotation} 受击 X 等于模型");
                Check(bounds.size.z * 0.5f, actualHit.y, 0.001f, $"rotation={rotation} 受击 Z 等于模型");
                Check(expectedCenter.x + actualHit.x, hitPoint.x, 0.001f, $"rotation={rotation} 受击中心 X");
                Check(expectedCenter.z + actualHit.y, hitPoint.z, 0.001f, $"rotation={rotation} 受击中心 Z");
                Check(expectedCenter.x, bounds.center.x, 0.01f, $"rotation={rotation} 模型中心 X");
                Check(expectedCenter.z, bounds.center.z, 0.01f, $"rotation={rotation} 模型中心 Z");
                Check(rotation % 2 == 0 ? baseModelSize.x : baseModelSize.z, bounds.size.x, 0.01f,
                    $"rotation={rotation} 模型长宽 X 保持预制体比例");
                Check(rotation % 2 == 0 ? baseModelSize.z : baseModelSize.x, bounds.size.z, 0.01f,
                    $"rotation={rotation} 模型长宽 Z 保持预制体比例");
            }
            Assert.IsEmpty(mismatches, string.Join("\n", mismatches));
        }

        [Test]
        public void 减速场_普通六五_Boss八五_场外恢复()
        {
            var fields = new[] { Field(35f, 15f) };
            Assert.AreEqual(0.65f, ElectricFence.SpeedFactorInFields(fields, Vector3.zero, false), 0.0001f);
            Assert.AreEqual(0.85f, ElectricFence.SpeedFactorInFields(fields, Vector3.zero, true), 0.0001f);
            Assert.AreEqual(1f, ElectricFence.SpeedFactorInFields(fields, new Vector3(2f, 0f, 0f), false), 0.0001f);
        }

        [Test]
        public void 重叠减速场_取最强不叠乘()
        {
            var fields = new[] { Field(35f, 15f), Field(20f, 10f) };
            Assert.AreEqual(0.65f, ElectricFence.SpeedFactorInFields(fields, Vector3.zero, false), 0.0001f);
            Assert.AreEqual(0.85f, ElectricFence.SpeedFactorInFields(fields, Vector3.zero, true), 0.0001f);
        }

        private ElectricFence Field(float slowPercent, float bossSlowPercent)
        {
            var go = new GameObject("TestElectricFence");
            objects.Add(go);
            var field = go.AddComponent<ElectricFence>();
            field.halfExtents = new Vector2(1f, 0.25f);
            field.slowPercent = slowPercent;
            field.bossSlowPercent = bossSlowPercent;
            return field;
        }
    }
}
