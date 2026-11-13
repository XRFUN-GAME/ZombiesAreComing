using MoriGame.Territory;
using NUnit.Framework;

namespace MoriGame.Tests.EditMode
{
    /// <summary>docs/09 地盘规则（CORE-07）+ docs/02 2026-11-06：0.5 米格 56×56、初始地盘 5×5（6.25㎡，
    /// 2026-11-06 站桩重构：以 RealSpace 锚点格为中心）、建设即扩张（拓界信标废除——建筑落地以占位为中心圈 5×5 格）</summary>
    public class TerritoryGridTests
    {
        private static TerritoryGrid NewGridWithP1Core()
        {
            var grid = new TerritoryGrid();
            grid.PlaceCore(CellOwner.P1, new CellCoord(10, 10), 10); // 显式 10×10 夹具：(10..19,10..19)——规则用例与默认核心尺寸解耦
            return grid;
        }

        // docs/02 2026-11-03：所有权与建造统一 0.5 米格
        [Test]
        public void 世界坐标映射_半米格()
        {
            Assert.AreEqual(56, TerritoryGrid.GridSize);
            Assert.AreEqual(new CellCoord(28, 28), TerritoryGrid.WorldToCell(0f, 0f), "含原点格");
            Assert.AreEqual(new CellCoord(28, 28), TerritoryGrid.WorldToCell(0.49f, 0.49f));
            Assert.AreEqual(new CellCoord(27, 27), TerritoryGrid.WorldToCell(-0.01f, -0.01f));
            Assert.AreEqual(new CellCoord(0, 0), TerritoryGrid.WorldToCell(-14f, -14f), "西/南边界");
            Assert.AreEqual(new CellCoord(55, 55), TerritoryGrid.WorldToCell(13.99f, 13.99f), "东/北最后一格");
            Assert.AreEqual(-14f, TerritoryGrid.CellMinWorldX(0));
            Assert.AreEqual(-14f, TerritoryGrid.CellMinWorldZ(0));
            Assert.AreEqual(0.25f, TerritoryGrid.CellCenterWorldX(28), 1e-4f, "含原点格中心");
            Assert.AreEqual(0.25f, TerritoryGrid.CellCenterWorldZ(28), 1e-4f, "含原点格中心");
        }

        // 显式尺寸核心放置机制（初始尺寸见"默认守中布局"用例；当前默认 5×5=6.25㎡）
        [Test]
        public void 显式核心放置_十乘十格面积二十五平米()
        {
            var grid = NewGridWithP1Core();
            Assert.AreEqual(100, grid.GetOwnedCellCount(CellOwner.P1));
            Assert.AreEqual(25f, grid.GetAreaSqm(CellOwner.P1));
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(10, 10)));
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(19, 19)));
            Assert.AreEqual(CellOwner.Neutral, grid.GetOwner(new CellCoord(9, 10)), "西侧界外");
            Assert.AreEqual(CellOwner.Neutral, grid.GetOwner(new CellCoord(20, 10)), "东侧界外");
        }

        // 条目 1：斜角地盘不触发接壤，四邻接触发
        [Test]
        public void 四邻接触发接壤_斜角不触发()
        {
            var orthogonal = new TerritoryGrid();
            orthogonal.PlaceCore(CellOwner.P1, new CellCoord(10, 10), 10);
            orthogonal.PlaceCore(CellOwner.P2, new CellCoord(10, 20), 10);
            // P1 (10..19) 与 P2 (20..29) 存在 (x,19)-(x,20) 四邻接
            Assert.IsTrue(orthogonal.CheckAdjacency(CellOwner.P1, CellOwner.P2));

            var diagonal = new TerritoryGrid();
            diagonal.PlaceCore(CellOwner.P1, new CellCoord(10, 10), 10);
            diagonal.PlaceCore(CellOwner.P2, new CellCoord(20, 20), 10);
            // 最近格 (19,19) 与 (20,20) 仅斜角
            Assert.IsFalse(diagonal.CheckAdjacency(CellOwner.P1, CellOwner.P2));
        }

        // 建设即扩张（docs/02 2026-11-03）：建筑落地以占位为中心圈 5×5 格（6.25㎡），外溢部分并入地盘
        [Test]
        public void 建筑落地即圈地_占位中心五乘五格()
        {
            var grid = NewGridWithP1Core();

            // 圈地完全落在地盘内：面积不变
            Assert.IsTrue(grid.RegisterBuilding(new CellCoord(12, 12), CellOwner.P1));
            Assert.AreEqual(100, grid.GetOwnedCellCount(CellOwner.P1), "圈内无新增");

            // 边缘建筑：圈地外溢 16 格（圈 17..21² 与核心 10..19² 重叠 9 格只计一次）
            Assert.IsTrue(grid.RegisterBuilding(new CellCoord(19, 19), CellOwner.P1));
            Assert.AreEqual(116, grid.GetOwnedCellCount(CellOwner.P1));
            Assert.AreEqual(29f, grid.GetAreaSqm(CellOwner.P1));
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(21, 21)), "圈地外缘并入");
        }

        // 条目 2：多建筑圈地重叠只计一次
        [Test]
        public void 多建筑圈地重叠只计一次()
        {
            var grid = NewGridWithP1Core();
            grid.RegisterBuilding(new CellCoord(19, 19), CellOwner.P1); // → 116 格
            grid.RegisterBuilding(new CellCoord(21, 21), CellOwner.P1); // 圈 19..23² 与已占重叠 9 格

            Assert.AreEqual(132, grid.GetOwnedCellCount(CellOwner.P1));
            Assert.AreEqual(33f, grid.GetAreaSqm(CellOwner.P1));
            Assert.AreEqual(2, grid.CountBuildings(CellOwner.P1));
        }

        // 条目 3：建筑销毁后，只中立化"失去来源且不连通"的网格外围
        [Test]
        public void 建筑销毁只中立化失去来源且不连通的网格()
        {
            var grid = NewGridWithP1Core();
            Assert.IsTrue(grid.RegisterBuilding(new CellCoord(19, 19), CellOwner.P1), "A");
            Assert.IsTrue(grid.RegisterBuilding(new CellCoord(21, 17), CellOwner.P1), "B");

            // 核 100 + A 新增 16 + B 新增 14 = 130 格
            Assert.AreEqual(32.5f, grid.GetAreaSqm(CellOwner.P1), "两建筑面积");

            grid.RemoveBuilding(new CellCoord(21, 17));

            // B 独占的 14 格进入 5 秒预警（期间保留归属）；A 与核心不受影响
            Assert.AreEqual(14, grid.PendingNeutralizeCount, "预警格数");
            Assert.AreEqual(32.5f, grid.GetAreaSqm(CellOwner.P1), "预警期间保留归属");
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(23, 17)), "预警期间仍归属");

            grid.Tick(4.9f);
            Assert.AreEqual(32.5f, grid.GetAreaSqm(CellOwner.P1), "4.9 秒仍未中立化");
            grid.Tick(0.1f);
            Assert.AreEqual(29f, grid.GetAreaSqm(CellOwner.P1), "5 秒后失去来源格转野区");
            Assert.AreEqual(CellOwner.Neutral, grid.GetOwner(new CellCoord(23, 17)));

            // 核心与保留来源格不受影响
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(10, 10)));
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(21, 21)), "A 的圈地保留");
        }

        // 条目 4：初始地盘（核心区）在任何情况下不被夺取
        [Test]
        public void 核心区不可被敌方建筑圈地夺取()
        {
            var grid = new TerritoryGrid();
            grid.PlaceCore(CellOwner.P1, new CellCoord(10, 10), 10);
            grid.PlaceCore(CellOwner.P2, new CellCoord(20, 20), 10);

            // 敌方建筑直接压在 P1 核心格上，圈地覆盖核心区
            grid.RegisterBuilding(new CellCoord(11, 11), CellOwner.P2);

            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(10, 10)));
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(11, 11)));
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(19, 19)));
            // P2 的越界圈地格因与 P2 核心不连通而无效
            Assert.AreEqual(25f, grid.GetAreaSqm(CellOwner.P2));
            Assert.AreEqual(25f, grid.GetAreaSqm(CellOwner.P1));
        }

        // 条目 5（网格侧）：失去归属记录 + 30 秒离线销毁判定（建筑实体销毁在实体侧落地）
        [Test]
        public void 失去归属网格记录与三十秒销毁判定()
        {
            var grid = NewGridWithP1Core();
            grid.RegisterBuilding(new CellCoord(19, 19), CellOwner.P1);
            grid.RegisterBuilding(new CellCoord(21, 17), CellOwner.P1);
            grid.RemoveBuilding(new CellCoord(21, 17));
            grid.Tick(5f);

            Assert.AreEqual(14, grid.LostCells.Count, "失去归属记录数");
            foreach (var record in grid.LostCells)
            {
                Assert.AreEqual(CellOwner.P1, record.PreviousOwner);
            }

            var lostAt = grid.LostCells[0].LostAtGridSeconds;
            Assert.IsFalse(TerritoryGrid.ShouldDestroyOfflineProp(lostAt, lostAt + 29.9f), "29.9 秒不销毁");
            Assert.IsTrue(TerritoryGrid.ShouldDestroyOfflineProp(lostAt, lostAt + 30f), "30 秒销毁");
        }

        // docs/02 规则 4 引申：预警期内来源恢复 → 取消中立化
        [Test]
        public void 预警期内重建建筑取消中立化()
        {
            var grid = NewGridWithP1Core();
            grid.RegisterBuilding(new CellCoord(19, 19), CellOwner.P1);
            grid.RegisterBuilding(new CellCoord(21, 17), CellOwner.P1);
            grid.RemoveBuilding(new CellCoord(21, 17));
            Assert.AreEqual(14, grid.PendingNeutralizeCount);

            grid.Tick(4.5f);
            Assert.IsTrue(grid.RegisterBuilding(new CellCoord(21, 17), CellOwner.P1), "重建建筑");

            Assert.AreEqual(0, grid.PendingNeutralizeCount, "预警取消");
            grid.Tick(5f);
            Assert.AreEqual(32.5f, grid.GetAreaSqm(CellOwner.P1), "地盘完整保留");
            Assert.AreEqual(0, grid.LostCells.Count, "无失去归属记录");
        }

        // docs/02 守中布局（2026-11-09 二次校准）：P1 单核心 8×8 格（16㎡）以锚点格为中心，高塔 2×2 居中对称
        [Test]
        public void 默认守中布局_单玩家八乘八核心()
        {
            var grid = new TerritoryGrid();
            grid.SetupDefaultCores();

            Assert.AreEqual(CellOwner.P1, grid.GetOwner(TerritoryGrid.WorldToCell(0, 0)), "锚点格归属 P1");
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(24, 24)), "起格");
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(31, 31)), "止格");
            Assert.AreEqual(CellOwner.Neutral, grid.GetOwner(new CellCoord(23, 28)), "西侧界外");
            Assert.AreEqual(CellOwner.Neutral, grid.GetOwner(new CellCoord(32, 28)), "东侧界外");
            Assert.AreEqual(16f, grid.GetAreaSqm(CellOwner.P1));
            Assert.AreEqual(0f, grid.GetAreaSqm(CellOwner.P2));
        }

        // 2026-11-04 真机定案：机枪塔落地圈地=攻击范围（占地 2×2 ±3 格 = 8×8），矩形圈地注册
        [Test]
        public void 机枪塔落地圈地等于射界_八乘八矩形()
        {
            var grid = NewGridWithP1Core(); // 初始 10×10 (10..19)²
            // 占地 (19..20)²，射界=占地±3=(16..23)²；注册格 (19,19) 仅作移除键
            Assert.IsTrue(grid.RegisterBuilding(new CellCoord(19, 19), CellOwner.P1, 16, 16, 23, 23));
            Assert.AreEqual(148, grid.GetOwnedCellCount(CellOwner.P1), "100 + 64 - 16 格核心重叠");
            Assert.AreEqual(37f, grid.GetAreaSqm(CellOwner.P1));
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(16, 23)), "射界四角全部归属");
            Assert.AreEqual(CellOwner.P1, grid.GetOwner(new CellCoord(23, 16)));
            Assert.AreEqual(CellOwner.Neutral, grid.GetOwner(new CellCoord(24, 20)), "射界与核心外不归属");
            Assert.IsTrue(grid.RemoveBuilding(new CellCoord(19, 19)), "按注册格移除");
            Assert.AreEqual(148, grid.GetOwnedCellCount(CellOwner.P1),
                "移除后 5 秒中立化倒计时内归属保留（docs/02 失去来源网格延迟转野区）");
            grid.Tick(TerritoryGrid.NeutralizeDelaySeconds);
            Assert.AreEqual(100, grid.GetOwnedCellCount(CellOwner.P1), "倒计时到期回落初始 10×10");
        }
    }
}
