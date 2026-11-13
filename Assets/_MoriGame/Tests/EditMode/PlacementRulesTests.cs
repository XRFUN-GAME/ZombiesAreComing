using System.Collections.Generic;
using System.Reflection;
using MoriGame.Build;
using MoriGame.Configs;
using MoriGame.Territory;
using MoriGame.Ui;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MoriGame.Tests.EditMode
{
    /// <summary>放置/传送合法性（docs/02 2026-11-03 建设即扩张：建筑一律己方地盘内，圈地半径外溢扩地；
    /// 出生门环禁放禁传送为 2026-11-02 决策）</summary>
    public class PlacementRulesTests
    {
        private TerritoryGrid grid;
        private HashSet<CellCoord> occupied;

        [SetUp]
        public void SetUp()
        {
            TerritoryGrid.WorldOrigin = Vector2.zero; // 隔离主域污染：ExecuteAlways 调试器/场景构建器会写真实场景值（2026-11-11 13 红根因）
            TerritoryGrid.WorldFloorY = 0f;
            grid = new TerritoryGrid();
            grid.PlaceCore(CellOwner.P1, new CellCoord(25, 25), 10); // 测试核心固定 10×10 (25..34)²——与默认核心尺寸解耦
            occupied = new HashSet<CellCoord>();
            // 高台塔 1×1 米：中心 (1,-0.5) → 占格 (29..30)×(26..27)（2026-11-09 高台制）
            occupied.UnionWith(TerritoryGrid.CoreDeviceFootprintCells(new Vector2(1f, -0.5f), 1f));
            occupied.Add(TerritoryGrid.WorldToCell(1, 1)); // 出生点格 (30,30)
        }

        [Test]
        public void 建筑放己方边缘格_圈地外溢扩地()
        {
            var cell = new CellCoord(34, 28); // 己方东侧边缘格
            Assert.AreEqual(PlacementResult.Ok, PlacementRules.Validate(grid, cell, occupied, CellOwner.P1));
            Assert.IsTrue(grid.RegisterBuilding(cell, CellOwner.P1));
            // 圈地 32..36×26..30：100 + 25 - 15 格重叠 = 110 格
            Assert.AreEqual(110, grid.GetOwnedCellCount(CellOwner.P1));
            Assert.AreEqual(27.5f, grid.GetAreaSqm(CellOwner.P1));
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(36, 28)), "圈地外缘并入");
        }

        [Test]
        public void 放置范围三态_地盘内_相邻扩格_远格拒绝()
        {
            // docs/06 2026-11-04 修订：己方地盘内 + 与地盘四邻接的空格均可放（放邻格→圈地外扩）
            Assert.AreEqual(PlacementResult.Ok,
                PlacementRules.Validate(grid, new CellCoord(31, 28), occupied, CellOwner.P1), "地盘内=绿");
            Assert.AreEqual(PlacementResult.ExpansionCell,
                PlacementRules.Validate(grid, new CellCoord(35, 28), occupied, CellOwner.P1), "东侧相邻扩格=青绿（(34,28) 己方四邻接）");
            Assert.AreEqual(PlacementResult.ExpansionCell,
                PlacementRules.Validate(grid, new CellCoord(24, 28), occupied, CellOwner.P1), "西侧相邻同样可放");
            Assert.AreEqual(PlacementResult.NotOwnTerritory,
                PlacementRules.Validate(grid, new CellCoord(37, 28), occupied, CellOwner.P1), "隔一格的远野区仍拒（(36,28) 非己方）");
            Assert.AreEqual(PlacementResult.NotOwnTerritory,
                PlacementRules.Validate(grid, new CellCoord(40, 40), occupied, CellOwner.P1), "远格拒绝");
            Assert.IsTrue(PlacementRules.IsPlaceable(PlacementResult.Ok), "Ok 可落地");
            Assert.IsTrue(PlacementRules.IsPlaceable(PlacementResult.ExpansionCell), "扩格可落地");
            Assert.IsFalse(PlacementRules.IsPlaceable(PlacementResult.NotOwnTerritory), "野区不可落地");
        }

        [Test]
        public void 占用格拒绝_高台塔与出生点不可叠放()
        {
            Assert.AreEqual(PlacementResult.Occupied,
                PlacementRules.Validate(grid, new CellCoord(29, 27), occupied, CellOwner.P1), "高台塔格");
            Assert.AreEqual(PlacementResult.Occupied,
                PlacementRules.Validate(grid, new CellCoord(30, 30), occupied, CellOwner.P1), "出生点格");
            Assert.AreEqual(PlacementResult.Ok,
                PlacementRules.Validate(grid, new CellCoord(28, 28), occupied, CellOwner.P1), "地盘内塔外空格仍可放");
        }

        [Test]
        public void 高台塔一米占地_两乘两格按包围盒()
        {
            var cells = TerritoryGrid.CoreDeviceFootprintCells(new Vector2(1f, -0.5f), 1f);
            Assert.AreEqual(4, cells.Count, "1×1 米塔=2×2 格");
            Assert.IsTrue(cells.Contains(new CellCoord(29, 26)) && cells.Contains(new CellCoord(30, 27)),
                "底面 [0.5,1.5]×[-1,0] 覆盖格 (29..30)×(26..27)");
            Assert.AreEqual(Rect.MinMaxRect(0.5f, -1f, 1.5f, 0f),
                PlacementRules.FootprintWorldRect(cells), "塔底世界矩形");
            foreach (var cell in cells)
            {
                Assert.IsTrue(occupied.Contains(cell), $"塔格 {cell} 未登记占用");
                Assert.AreEqual(PlacementResult.Occupied,
                    PlacementRules.Validate(grid, cell, occupied, CellOwner.P1));
            }
        }

        [Test]
        public void 界外拒绝()
        {
            Assert.AreEqual(PlacementResult.OutOfBounds,
                PlacementRules.Validate(grid, new CellCoord(56, 5), occupied, CellOwner.P1));
            Assert.AreEqual(PlacementResult.OutOfBounds,
                PlacementRules.Validate(grid, new CellCoord(-1, 5), occupied, CellOwner.P1));
        }

        [Test]
        public void 出生门环禁放建筑_环带内侧可放()
        {
            // 0.5 米格：格西/南边界世界 ≥12（格 52..55）或 ≤-13（格 0..2）为刷怪环带
            Assert.AreEqual(PlacementResult.SpawnGateCell,
                PlacementRules.Validate(grid, new CellCoord(52, 28), occupied, CellOwner.P1));
            Assert.AreEqual(PlacementResult.SpawnGateCell,
                PlacementRules.Validate(grid, new CellCoord(2, 2), occupied, CellOwner.P1));
            // 环带内侧一格（世界 11.5）不在环上，按普通野区判定（NotOwnTerritory 而非 SpawnGateCell）
            Assert.AreEqual(PlacementResult.NotOwnTerritory,
                PlacementRules.Validate(grid, new CellCoord(51, 28), occupied, CellOwner.P1));
        }

        [Test]
        public void 传送落点_仅己方地盘可达_野区占用格门环全拒()
        {
            Assert.IsTrue(PlacementRules.IsTeleportValid(grid, occupied, new CellCoord(31, 28), CellOwner.P1), "己方空地可传送");
            Assert.IsFalse(PlacementRules.IsTeleportValid(grid, occupied, new CellCoord(29, 27), CellOwner.P1), "中控塔格不可传送");
            Assert.IsFalse(PlacementRules.IsTeleportValid(grid, occupied, new CellCoord(30, 30), CellOwner.P1), "出生点格不可传送");
            Assert.IsFalse(PlacementRules.IsTeleportValid(grid, occupied, new CellCoord(52, 28), CellOwner.P1), "出生门环不可传送");
            Assert.IsFalse(PlacementRules.IsTeleportValid(grid, occupied, new CellCoord(56, 28), CellOwner.P1), "界外不可传送");
            Assert.IsFalse(PlacementRules.IsTeleportValid(grid, occupied, new CellCoord(40, 40), CellOwner.P1), "野区不可传送（2026-11-03 收紧）");
        }

        [Test]
        public void 传送可达范围随地盘扩张()
        {
            Assert.IsFalse(PlacementRules.IsTeleportValid(grid, occupied, new CellCoord(36, 28), CellOwner.P1), "圈地外不可达");
            grid.RegisterBuilding(new CellCoord(34, 28), CellOwner.P1); // 圈地 32..36×26..30 外溢
            Assert.IsTrue(PlacementRules.IsTeleportValid(grid, occupied, new CellCoord(36, 28), CellOwner.P1), "扩地后可达");
        }

        // ---- M2-T3.5（2026-11-04）：占地多格道具（电网 4×1 米=8×2 格、路障 1×0.5 米=2×1 格）----

        [Test]
        public void 米格换算_四舍五入至少一格()
        {
            Assert.AreEqual(8, PlacementRules.MetersToCells(4f), "电网长边 4 米 = 8 格");
            Assert.AreEqual(2, PlacementRules.MetersToCells(1f), "1 米 = 2 格");
            Assert.AreEqual(1, PlacementRules.MetersToCells(0.5f), "0.5 米 = 1 格");
            Assert.AreEqual(1, PlacementRules.MetersToCells(0.2f), "亚格至少 1 格");
        }

        [Test]
        public void 电网足印枚举_旋转四档轴互换()
        {
            // 电网 4×1 格（docs/06 2026-11-04 修订），锚格 (28,26)：0=X+ 展开，1=Z+，2=X-，3=Z-（顺时针四档）
            var r0 = PlacementRules.FootprintCells(new CellCoord(28, 26), 4, 1, 0);
            Assert.AreEqual(4, r0.Count, "4×1=4 格");
            Assert.IsTrue(r0.Contains(new CellCoord(28, 26)) && r0.Contains(new CellCoord(31, 26)), "X+ 展开含首末格");
            var r1 = PlacementRules.FootprintCells(new CellCoord(28, 26), 4, 1, 1);
            Assert.IsTrue(r1.Contains(new CellCoord(28, 29)), "Z+ 展开轴互换");
            var r2 = PlacementRules.FootprintCells(new CellCoord(28, 26), 4, 1, 2);
            Assert.IsTrue(r2.Contains(new CellCoord(25, 26)) && !r2.Contains(new CellCoord(31, 26)), "X- 反向展开");
            var r3 = PlacementRules.FootprintCells(new CellCoord(28, 26), 4, 1, 3);
            Assert.IsTrue(r3.Contains(new CellCoord(28, 23)), "Z- 反向展开");
            Assert.AreEqual(4, r3.Count, "四档格数一致（4）");
        }

        [Test]
        public void 机枪塔双二占地居中_八八射界判定()
        {
            // 占地 2×2 格（docs/06 2026-11-04 修订：原点=模型中心=四格中心）；锚 (28,28) X+ 展开
            var cells = PlacementRules.FootprintCells(new CellCoord(28, 28), 2, 2, 0);
            Assert.AreEqual(4, cells.Count, "2×2=4 格");
            Assert.IsTrue(cells.Contains(new CellCoord(28, 28)) && cells.Contains(new CellCoord(29, 29)), "(28..29)²");
            var center = PlacementRules.FootprintCenterWorld(new CellCoord(28, 28), 2, 2, 0);
            Assert.AreEqual(new Vector3(0.5f, 0f, 0.5f), center, "四格中心=模型原点（格 28 西南角世界 0 → 中心 +0.5,+0.5）");
            Assert.AreEqual(new CellCoord(29, 28), PlacementRules.ClaimCellOf(new CellCoord(28, 28), 2, 0), "圈地注册格=长边中点");

            // 射界 = 占地各向外 4 格 = 10×10 格（闭区间 24..33；2026-11-10 用户定版 8×8→10×10）
            PlacementRules.RangeBounds(cells, 4, out var minX, out var minZ, out var maxX, out var maxZ);
            Assert.AreEqual(24, minX); Assert.AreEqual(33, maxX);
            Assert.AreEqual(24, minZ); Assert.AreEqual(33, maxZ);
            Assert.AreEqual(100, (maxX - minX + 1) * (maxZ - minZ + 1), "10×10=100 格");
        }

        [Test]
        public void 电网圈地对称矩形_四乘一长条圈六乘五_20261109()
        {
            // 4×1 长条沿 X：圈地长边两端各 +1、短边两侧各 +2 = 6×5（旧 5×5 沿长边只多出一侧，不对称）
            var horizontal = PlacementRules.FootprintCells(new CellCoord(30, 28), 4, 1, 0);
            PlacementRules.FenceClaimBounds(horizontal, out var minX, out var minZ, out var maxX, out var maxZ);
            Assert.AreEqual(6, maxX - minX + 1, "长边 4+2=6 格");
            Assert.AreEqual(5, maxZ - minZ + 1, "短边 1+4=5 格");
            Assert.AreEqual(29, minX, "两端各 +1 对称");
            Assert.AreEqual(26, minZ, "两侧各 +2 对称");
            Assert.AreEqual(30, maxZ);

            // 旋转 90°（长条沿 Z）：圈地转置为 5×6，仍对称
            var vertical = PlacementRules.FootprintCells(new CellCoord(30, 28), 4, 1, 1);
            PlacementRules.FenceClaimBounds(vertical, out minX, out minZ, out maxX, out maxZ);
            Assert.AreEqual(5, maxX - minX + 1, "转置后长边 5 格");
            Assert.AreEqual(6, maxZ - minZ + 1, "转置后短边 6 格");
            Assert.AreEqual(30, (maxX - minX + 1) * (maxZ - minZ + 1), "圈地 30 格");
        }

        [Test]
        public void 路障双格占位_全格己方通过_压相邻扩格为青绿态()
        {
            // 路障 1×0.5 米 = 2×1 格：锚 (31,29) X+ → (31,29)(32,29) 均己方且非占用 → Ok
            var okCells = PlacementRules.FootprintCells(new CellCoord(31, 29), 2, 1, 0);
            Assert.AreEqual(PlacementResult.Ok,
                PlacementRules.ValidateFootprint(grid, okCells, occupied, CellOwner.P1));
            // 锚 (34,29) X+ → (34,29)(35,29)：35 列为相邻扩格 → 聚合青绿态可放
            var expandCells = PlacementRules.FootprintCells(new CellCoord(34, 29), 2, 1, 0);
            Assert.AreEqual(PlacementResult.ExpansionCell,
                PlacementRules.ValidateFootprint(grid, expandCells, occupied, CellOwner.P1));
            // 锚 (30,29) 改 Z+ 旋转（(30,29)(30,30)）压出生点占用格 → Occupied（硬拒优先于扩格态）
            var occCells = PlacementRules.FootprintCells(new CellCoord(30, 29), 2, 1, 1);
            Assert.AreEqual(PlacementResult.Occupied,
                PlacementRules.ValidateFootprint(grid, occCells, occupied, CellOwner.P1));
        }

        [Test]
        public void 电网长条足印_扩格态与整条拒绝()
        {
            // 锚 (32,26) X+：x 32..35，35 列为相邻扩格（(34,26) 己方四邻接）→ 聚合青绿态可放
            var expand = PlacementRules.FootprintCells(new CellCoord(32, 26), 4, 1, 0);
            Assert.AreEqual(PlacementResult.ExpansionCell,
                PlacementRules.ValidateFootprint(grid, expand, occupied, CellOwner.P1));
            // 锚 (36,26) X+：x 36..39 首格即远离地盘（邻格均非己方）→ NotOwnTerritory 整条拒绝
            var wild = PlacementRules.FootprintCells(new CellCoord(36, 26), 4, 1, 0);
            Assert.AreEqual(PlacementResult.NotOwnTerritory,
                PlacementRules.ValidateFootprint(grid, wild, occupied, CellOwner.P1));
            // 锚 (55,55) Z+ 延伸跨界：首格 (55,55) 已在出生门环（界内 x/z 52..55）——
            // 门环守边判定先于越界格命中（环带挡在边界内侧，产品语义）
            var rim = PlacementRules.FootprintCells(new CellCoord(55, 55), 4, 1, 1);
            Assert.AreEqual(PlacementResult.SpawnGateCell,
                PlacementRules.ValidateFootprint(grid, rim, occupied, CellOwner.P1));
            // 锚自身界外（x=56 起）：首格即 OutOfBounds
            var outside = PlacementRules.FootprintCells(new CellCoord(56, 28), 4, 1, 0);
            Assert.AreEqual(PlacementResult.OutOfBounds,
                PlacementRules.ValidateFootprint(grid, outside, occupied, CellOwner.P1));
        }

        [Test]
        public void 射界半径奇偶格一致_严格五乘五米()
        {
            // 2026-11-04 实测修订：射界=中心世界坐标 ±half（不再从格下标推导），奇偶锚格无半边偏移
            // 2026-11-10 用户定版：外扩 3→4 格，10×10 格 = 5×5 米
            Assert.AreEqual(2.5f, PlacementRules.RangeHalfMeters(2, 4), "2×2 占地 + 外扩 4 格 = ±2.5 米");

            var evenCenter = PlacementRules.FootprintCenterWorld(new CellCoord(28, 28), 2, 2, 0); // (0.5, 0.5)
            var oddCenter = PlacementRules.FootprintCenterWorld(new CellCoord(29, 29), 2, 2, 0);  // (1.0, 1.0)
            var half = PlacementRules.RangeHalfMeters(2, 4);
            foreach (var c in new[] { evenCenter, oddCenter })
            {
                var width = (c.x + half) - (c.x - half);
                var depth = (c.z + half) - (c.z - half);
                Assert.AreEqual(5.0f, width, "任意奇偶锚射界宽严格 5 米");
                Assert.AreEqual(5.0f, depth, "任意奇偶锚射界深严格 5 米");
            }

            // 边界含：恰 ±2.5 在界内，超一丝出界（索敌与描边同一判定）
            Assert.IsTrue(PlacementRules.WorldInSquareRange(
                new Vector3(3.0f, 0f, 0.5f), evenCenter, half), "东边界含");
            Assert.IsTrue(PlacementRules.WorldInSquareRange(
                new Vector3(-2.0f, 0f, 0.5f), evenCenter, half), "西边界含");
            Assert.IsTrue(PlacementRules.WorldInSquareRange(
                new Vector3(0.5f, 0f, 3.0f), evenCenter, half), "北边界含");
            Assert.IsFalse(PlacementRules.WorldInSquareRange(
                new Vector3(3.01f, 0f, 0.5f), evenCenter, half), "超界即出");
            Assert.IsFalse(PlacementRules.WorldInSquareRange(
                new Vector3(3.0f, 0f, 3.01f), evenCenter, half), "一角超界即出");
        }

        [Test]
        public void 占地世界矩形_格边即物理边界()
        {
            // 机枪塔 2×2 锚 (28,28)：格 28 西南角世界 0 → 矩形 [0,1]²
            var turret = PlacementRules.FootprintCells(new CellCoord(28, 28), 2, 2, 0);
            var tr = PlacementRules.FootprintWorldRect(turret);
            Assert.AreEqual(Rect.MinMaxRect(0f, 0f, 1f, 1f), tr);

            // 电网 4×1 锚 (28,26) X+：x [0,2]，z 26 列 [-1,-0.5]
            var fenceX = PlacementRules.FootprintCells(new CellCoord(28, 26), 4, 1, 0);
            Assert.AreEqual(Rect.MinMaxRect(0f, -1f, 2f, -0.5f),
                PlacementRules.FootprintWorldRect(fenceX));

            // 同锚 Z+ 旋转：x [0,0.5]，z 26..29 [-1,1]
            var fenceZ = PlacementRules.FootprintCells(new CellCoord(28, 26), 4, 1, 1);
            Assert.AreEqual(Rect.MinMaxRect(0f, -1f, 0.5f, 1f),
                PlacementRules.FootprintWorldRect(fenceZ));
        }

        [Test]
        public void 占地矩形相交_相交拒_贴边与分离放()
        {
            // 2026-11-04 实测定案：新建筑 × 已放建筑世界矩形物理相交即拒（贴边共线合法）
            var turret = PlacementRules.FootprintWorldRect(
                PlacementRules.FootprintCells(new CellCoord(28, 28), 2, 2, 0)); // [0,1]²

            // 电网骑压塔位（同锚 X+：x[0,2] z[0,0.5]）→ 相交拒
            var crushing = PlacementRules.FootprintWorldRect(
                PlacementRules.FootprintCells(new CellCoord(28, 28), 4, 1, 0));
            Assert.IsTrue(PlacementRules.RectsIntersect(turret, crushing), "骑压=相交");

            // 电网东侧擦过塔半格（锚 (29,28) X+：x[0.5,2.5] z[0,0.5]）→ 部分相交也拒
            var clipping = PlacementRules.FootprintWorldRect(
                PlacementRules.FootprintCells(new CellCoord(29, 28), 4, 1, 0));
            Assert.IsTrue(PlacementRules.RectsIntersect(turret, clipping), "擦边重叠=相交");

            // 电网贴塔北边（锚 (28,30) X+：z[1,1.5] 与塔 z 上限 1 共线）→ 贴边合法
            var touching = PlacementRules.FootprintWorldRect(
                PlacementRules.FootprintCells(new CellCoord(28, 30), 4, 1, 0));
            Assert.IsFalse(PlacementRules.RectsIntersect(turret, touching), "贴边共线≠相交");

            // 电网远离塔（锚 (28,26) X+：z[-1,-0.5]）→ 分离合法
            var apart = PlacementRules.FootprintWorldRect(
                PlacementRules.FootprintCells(new CellCoord(28, 26), 4, 1, 0));
            Assert.IsFalse(PlacementRules.RectsIntersect(turret, apart), "分离≠相交");

            // 塔贴塔（锚 (30,28)：x[1,2] 与塔 x 上限 1 共线）→ 贴边合法
            var towerNext = PlacementRules.FootprintWorldRect(
                PlacementRules.FootprintCells(new CellCoord(30, 28), 2, 2, 0));
            Assert.IsFalse(PlacementRules.RectsIntersect(turret, towerNext), "塔贴塔共边合法");
        }

        [Test]
        public void 建筑承伤耗尽后进入已摧毁状态()
        {
            var go = new GameObject("PropHealthTest");
            var config = ScriptableObject.CreateInstance<PropConfig>();
            try
            {
                config.propId = "AutoTurret";
                config.maxHealth = 260;
                var prop = go.AddComponent<PropBehaviour>();
                prop.Configure(null, config, new Vector2(0.5f, 0.5f), 0.8f);
                prop.TakeDamage(100);
                Assert.AreEqual(160, prop.Health);
                Assert.IsFalse(prop.IsDestroyed);
                prop.TakeDamage(160);
                Assert.AreEqual(0, prop.Health);
                Assert.IsTrue(prop.IsDestroyed);
                prop.TakeDamage(20);
                Assert.AreEqual(0, prop.Health);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void 建筑血条_朝向观众且从右侧向左侧扣血()
        {
            var camera = Camera.main;
            var cameraGo = camera != null ? null : new GameObject("HealthBarTestCamera", typeof(Camera));
            if (cameraGo != null)
            {
                cameraGo.tag = "MainCamera";
                camera = cameraGo.GetComponent<Camera>();
            }
            var go = new GameObject("PropHealthBarDirectionTest");
            var config = ScriptableObject.CreateInstance<PropConfig>();
            try
            {
                go.transform.position = camera.transform.position + camera.transform.forward * 2f;
                config.maxHealth = 100;
                var prop = go.AddComponent<PropBehaviour>();
                prop.Configure(null, config, new Vector2(0.5f, 0.5f), 0.8f);
                prop.TakeDamage(25);
                typeof(PropBehaviour).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(prop, null);
                var bar = go.transform.Find("HealthBar");
                var fill = bar.Find("Fill");
                var awayFromCamera = bar.position - camera.transform.position;
                awayFromCamera.y = 0f;
                Assert.Greater(Vector3.Dot(bar.forward, awayFromCamera.normalized), 0.999f,
                    "血条正面朝相机，本地左缘对应观众左缘");
                Assert.Less(fill.localPosition.x, 0f, "扣血后保留左侧填充，右缘向左收缩");
                Assert.AreEqual(0.42f * 0.75f, fill.localScale.x, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(config);
                if (cameraGo != null)
                {
                    Object.DestroyImmediate(cameraGo);
                }
            }
        }

        [Test]
        public void 世界固定面板_无锚点脱离头部并按玩法锚点定位()
        {
            var previousMatch = MatchBootstrap.Instance;
            var matchGo = new GameObject("FixedPanelTestMatch");
            matchGo.SetActive(false);
            var match = matchGo.AddComponent<MatchBootstrap>();
            var anchorGo = new GameObject("FixedPanelTestAnchor");
            var headGo = new GameObject("FixedPanelTestHead");
            var panelGo = new GameObject("FixedPanelTest");
            try
            {
                anchorGo.transform.SetPositionAndRotation(new Vector3(2f, 0f, 3f), Quaternion.Euler(0f, 30f, 0f));
                match.playSpaceAnchor = anchorGo.transform;
                typeof(MatchBootstrap).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                    .GetSetMethod(true).Invoke(null, new object[] { match });
                panelGo.transform.SetParent(headGo.transform, false);

                Assert.IsTrue(UiTheme.PlaceFixedPanel(panelGo.transform,
                    "test.missing_panel", 90f, 1.2f, 0.72f));
                Assert.IsNull(panelGo.transform.parent, "运行后面板不再是头部子物体");
                var outward = Quaternion.Euler(0f, 120f, 0f) * Vector3.forward;
                var expected = anchorGo.transform.position + outward * 1.2f + Vector3.up * 0.72f;
                Assert.Less(Vector3.Distance(expected, panelGo.transform.position), 0.0001f);
                Assert.Greater(Vector3.Dot(panelGo.transform.forward, outward), 0.999f,
                    "面板正面 -Z 朝向锚点，观众从锚点向外看可读");
                var fixedPosition = panelGo.transform.position;
                headGo.transform.position += new Vector3(3f, 1f, -2f);
                Assert.AreEqual(fixedPosition, panelGo.transform.position, "头部移动不改变面板世界坐标");
            }
            finally
            {
                typeof(MatchBootstrap).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                    .GetSetMethod(true).Invoke(null, new object[] { previousMatch });
                Object.DestroyImmediate(panelGo);
                Object.DestroyImmediate(headGo);
                Object.DestroyImmediate(anchorGo);
                Object.DestroyImmediate(matchGo);
            }
        }

        [Test]
        public void 世界固定面板_场景锚点优先且保留手调旋转()
        {
            var anchorGo = new GameObject("FixedPanelWorldAnchor");
            var panelGo = new GameObject("FixedPanelAnchored");
            try
            {
                var anchor = anchorGo.AddComponent<WorldUiAnchor>();
                anchor.panelId = "test.fixed_panel";
                var position = new Vector3(-2.4f, 1.7f, 3.2f);
                var rotation = Quaternion.Euler(12f, 38f, -7f);
                anchorGo.transform.SetPositionAndRotation(position, rotation);

                Assert.IsTrue(UiTheme.PlaceFixedPanel(panelGo.transform, anchor.panelId, 160f, 9f, 9f));
                Assert.Less(Vector3.Distance(position, panelGo.transform.position), 0.0001f);
                Assert.Less(Quaternion.Angle(rotation, panelGo.transform.rotation), 0.001f);
                Assert.IsNull(panelGo.transform.parent);
            }
            finally
            {
                Object.DestroyImmediate(panelGo);
                Object.DestroyImmediate(anchorGo);
            }
        }

        [Test]
        public void 世界UI锚点_同场景重复ID报错并拒绝猜测()
        {
            var firstGo = new GameObject("DuplicateAnchorA");
            var secondGo = new GameObject("DuplicateAnchorB");
            try
            {
                firstGo.AddComponent<WorldUiAnchor>().panelId = "test.duplicate";
                secondGo.AddComponent<WorldUiAnchor>().panelId = "test.duplicate";
                LogAssert.Expect(LogType.Error,
                    new System.Text.RegularExpressions.Regex("存在重复世界 UI 锚点：test\\.duplicate"));
                Assert.IsNull(WorldUiAnchor.Find(firstGo.scene, "test.duplicate"));
            }
            finally
            {
                Object.DestroyImmediate(secondGo);
                Object.DestroyImmediate(firstGo);
            }
        }

        [Test]
        public void 世界UI锚点_相同ID按所属场景隔离()
        {
            var originalScene = SceneManager.GetActiveScene();
            var sceneA = default(Scene);
            var sceneB = default(Scene);
            try
            {
                sceneA = EditorSceneManager.OpenScene("Assets/_MoriGame/Scenes/StartScene.unity",
                    OpenSceneMode.Additive);
                sceneB = EditorSceneManager.OpenScene("Assets/_MoriGame/Scenes/MoriMain.unity",
                    OpenSceneMode.Additive);
                var aGo = new GameObject("SceneAnchorA");
                SceneManager.MoveGameObjectToScene(aGo, sceneA);
                var a = aGo.AddComponent<WorldUiAnchor>();
                a.panelId = "test.shared";
                var bGo = new GameObject("SceneAnchorB");
                SceneManager.MoveGameObjectToScene(bGo, sceneB);
                var b = bGo.AddComponent<WorldUiAnchor>();
                b.panelId = "test.shared";

                Assert.AreSame(a, WorldUiAnchor.Find(sceneA, "test.shared"));
                Assert.AreSame(b, WorldUiAnchor.Find(sceneB, "test.shared"));
            }
            finally
            {
                if (sceneB.IsValid())
                {
                    EditorSceneManager.CloseScene(sceneB, true);
                }
                if (sceneA.IsValid())
                {
                    EditorSceneManager.CloseScene(sceneA, true);
                }
                if (originalScene.IsValid())
                {
                    SceneManager.SetActiveScene(originalScene);
                }
            }
        }

        [Test]
        public void 建筑攻击点钳制在占地矩形边缘()
        {
            var go = new GameObject("PropAttackPointTest");
            var config = ScriptableObject.CreateInstance<PropConfig>();
            try
            {
                config.maxHealth = 200;
                go.transform.position = new Vector3(2f, 0f, 3f);
                var prop = go.AddComponent<PropBehaviour>();
                prop.Configure(null, config, new Vector2(1f, 0.25f), 0.4f);
                Assert.AreEqual(new Vector3(3f, 0f, 3f), prop.ClosestPoint(new Vector3(5f, 0f, 3f)));
                Assert.AreEqual(new Vector3(2f, 0f, 2.75f), prop.ClosestPoint(new Vector3(2f, 0f, 1f)));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void 野区与界外凝视_吸附最近可放边缘_占用冲突不吸附()
        {
            var previousMatch = MatchBootstrap.Instance;
            var instanceField = typeof(MatchBootstrap).GetField("<Instance>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic);
            var matchGo = new GameObject("SnapMatch");
            matchGo.SetActive(false);
            var managerGo = new GameObject("SnapBuildManager");
            var config = ScriptableObject.CreateInstance<PropConfig>();
            try
            {
                var match = matchGo.AddComponent<MatchBootstrap>();
                instanceField.SetValue(null, match);
                typeof(MatchBootstrap).GetField("<Grid>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic).SetValue(match, grid);
                var manager = managerGo.AddComponent<BuildManager>();
                config.footprintCellsX = 0.5f;
                config.footprintCellsY = 0.5f;
                const BindingFlags instancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(BuildManager).GetField("placing", instancePrivate).SetValue(manager, config);
                var shouldSnap = typeof(BuildManager).GetMethod("ShouldSnapToEdge",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsTrue((bool)shouldSnap.Invoke(null, new object[] { PlacementResult.NotOwnTerritory }));
                Assert.IsTrue((bool)shouldSnap.Invoke(null, new object[] { PlacementResult.OutOfBounds }));
                Assert.IsTrue((bool)shouldSnap.Invoke(null, new object[] { PlacementResult.SpawnGateCell }));
                Assert.IsFalse((bool)shouldSnap.Invoke(null, new object[] { PlacementResult.Occupied }));
                Assert.IsFalse((bool)shouldSnap.Invoke(null, new object[] { PlacementResult.Ok }));
                Assert.IsFalse((bool)shouldSnap.Invoke(null, new object[] { PlacementResult.ExpansionCell }));

                var trySnap = typeof(BuildManager).GetMethod("TryGetNearestPlaceableAnchor", instancePrivate);
                var args = new object[] { new CellCoord(38, 28), null };
                Assert.IsTrue((bool)trySnap.Invoke(manager, args));
                Assert.AreEqual(new CellCoord(35, 28), args[1], "东侧野区吸附到最近扩格边缘");
                args = new object[] { new CellCoord(56, 28), null };
                Assert.IsTrue((bool)trySnap.Invoke(manager, args));
                Assert.AreEqual(new CellCoord(35, 28), args[1], "界外同样回到最近可放格");

                config.footprintCellsX = 2f;
                typeof(BuildManager).GetField("placingRotation", instancePrivate).SetValue(manager, 2);
                typeof(BuildManager).GetField("edgeSnapCacheDirty", instancePrivate).SetValue(manager, true);
                args = new object[] { new CellCoord(40, 28), null };
                Assert.IsTrue((bool)trySnap.Invoke(manager, args));
                Assert.AreEqual(new CellCoord(35, 28), args[1], "4×1 长条 180° 时锚 35 覆盖 32..35，仍贴最近边缘");
            }
            finally
            {
                instanceField.SetValue(null, previousMatch);
                Object.DestroyImmediate(managerGo);
                Object.DestroyImmediate(matchGo);
                Object.DestroyImmediate(config);
            }
        }
    }
}
