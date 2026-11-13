using System;
using System.Collections.Generic;
using UnityEngine;

namespace MoriGame.Territory
{
    public enum CellOwner : byte
    {
        Neutral = 0,
        P1 = 1,
        P2 = 2,
        P3 = 3,
        P4 = 4
    }

    [Serializable]
    public readonly struct CellCoord : IEquatable<CellCoord>
    {
        public readonly int X;
        public readonly int Z;

        public CellCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(CellCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is CellCoord other && Equals(other);
        public override int GetHashCode() => (X * 397) ^ Z;
        public override string ToString() => $"({X},{Z})";
    }

    /// <summary>
    /// 56×56 地盘网格，0.5 米格（docs/02 2026-11-03：所有权与建造统一 0.5 米格，每格 0.25㎡）。
    /// 纯逻辑（仅用 Vector2 结构），EditMode 可单测。
    /// 2026-11-06 站桩重构：网格以 WorldOrigin（RealSpace 原点=玩家点）为世界锚，默认 (0,0) 兼容单测。
    /// 建设即扩张（拓界信标废除）：初始地盘 = 锚点中心 6×6 格（9㎡，2026-11-09 用户定稿：高塔 2×2 居中对称）；
    /// 任何建筑落地即以占位为中心圈 5×5 格（多建筑重叠只计一次），经四邻接 BFS 连通裁剪；
    /// 失去来源且不连通的格保留原归属 5 秒（预警）后转野区。
    /// 重算只在建筑放置/销毁时触发（docs/02 性能约束）。
    /// </summary>
    public class TerritoryGrid
    {
        public const int GridSize = 56;
        public const float CellSizeMeters = 0.5f;
        public const float CellAreaSqm = 0.25f;
        public const int CoreSizeCells = 8; // 2026-11-09 二次校准：初始地盘 6×6→8×8 格（16㎡），高塔 2×2 居中对称
        public const int CoreDeviceSizeCells = 1;

        /// <summary>网格世界锚点（XZ；RealSpace 原点即玩家点，MatchBootstrap.Awake 写入；默认 (0,0)=单测兼容）</summary>
        public static Vector2 WorldOrigin { get; set; }

        /// <summary>玩法地面世界高度（隐形可行走层顶面=锚点 Y；调试器画格/描边抬离地面用；默认 0）</summary>
        public static float WorldFloorY { get; set; }
        /// <summary>建筑圈地边长（docs/02 2026-11-03：建筑落地以占位为中心圈 5×5 格 = 6.25㎡）</summary>
        public const int BuildingClaimCells = 5;
        public const float NeutralizeDelaySeconds = 5f;
        public const float OfflinePropDestroySeconds = 30f;

        private readonly CellOwner[,] owners = new CellOwner[GridSize, GridSize];
        private readonly Dictionary<CellOwner, CellCoord> coreOrigins =
            new Dictionary<CellOwner, CellCoord>();
        private readonly Dictionary<CellOwner, int> coreSizes = new Dictionary<CellOwner, int>();
        private readonly List<Building> buildings = new List<Building>();
        private readonly List<PendingNeutralize> pending = new List<PendingNeutralize>();
        private readonly List<LostCellRecord> lostCells = new List<LostCellRecord>();
        private float gridSeconds;

        public struct Building
        {
            public CellCoord Position;
            public CellOwner Owner;
            /// <summary>圈地矩形（闭区间下标）；默认建筑 5×5，机枪塔=8×8 射界（2026-11-04 实测定案）</summary>
            public int MinX, MinZ, MaxX, MaxZ;
        }

        private struct PendingNeutralize
        {
            public CellCoord Cell;
            public CellOwner PreviousOwner;
            public float RemainingSeconds;
        }

        public struct PendingNeutralizeInfo
        {
            public CellCoord Cell;
            public CellOwner PreviousOwner;
            public float RemainingSeconds;
        }

        public struct LostCellRecord
        {
            public CellCoord Cell;
            public CellOwner PreviousOwner;
            public float LostAtGridSeconds;
        }

        public IReadOnlyList<LostCellRecord> LostCells => lostCells;

        public int PendingNeutralizeCount => pending.Count;

        /// <summary>所有权重算或中立化到期后触发（纯 C# 事件，调试可视化等订阅方使用）</summary>
        public event Action Changed;

        /// <summary>世界坐标 → 网格下标（0..55，锚点相对）；每格 0.5 米，格边界落在 0.5 米整数倍上</summary>
        public static CellCoord WorldToCell(float worldX, float worldZ) =>
            new CellCoord(
                (int)Math.Floor((worldX - WorldOrigin.x) / CellSizeMeters) + GridSize / 2,
                (int)Math.Floor((worldZ - WorldOrigin.y) / CellSizeMeters) + GridSize / 2);

        /// <summary>格下标 → 该格世界坐标最小角（西边界）</summary>
        public static float CellMinWorldX(int index) => WorldOrigin.x + (index - GridSize / 2) * CellSizeMeters;

        /// <summary>格下标 → 该格世界坐标最小角（南边界）</summary>
        public static float CellMinWorldZ(int index) => WorldOrigin.y + (index - GridSize / 2) * CellSizeMeters;

        /// <summary>格下标 → 该格中心世界坐标（X 轴）</summary>
        public static float CellCenterWorldX(int index) => CellMinWorldX(index) + CellSizeMeters * 0.5f;

        /// <summary>格下标 → 该格中心世界坐标（Z 轴）</summary>
        public static float CellCenterWorldZ(int index) => CellMinWorldZ(index) + CellSizeMeters * 0.5f;

        public static bool InBounds(CellCoord c) =>
            c.X >= 0 && c.X < GridSize && c.Z >= 0 && c.Z < GridSize;

        /// <summary>放置某玩家的初始地盘（默认 CoreSizeCells=8 格=16㎡），originCell 为左下角格</summary>
        public void PlaceCore(CellOwner player, CellCoord originCell, int sizeCells = CoreSizeCells)
        {
            if (player == CellOwner.Neutral)
            {
                throw new ArgumentOutOfRangeException(nameof(player));
            }
            if (sizeCells <= 0 ||
                originCell.X < 0 || originCell.X + sizeCells - 1 >= GridSize ||
                originCell.Z < 0 || originCell.Z + sizeCells - 1 >= GridSize)
            {
                throw new ArgumentOutOfRangeException(nameof(originCell));
            }
            coreOrigins[player] = originCell;
            coreSizes[player] = sizeCells;
            ComputeOwnership();
        }

        /// <summary>站桩布局（2026-11-09 定稿）：P1 初始地盘 6×6 格（9㎡），
        /// 以锚点（RealSpace 原点=玩家点）所在格为中心 → 起格 (25,25)、止 (30,30)，高塔 2×2 居中对称。</summary>
        public void SetupDefaultCores()
        {
            var center = WorldToCell(WorldOrigin.x, WorldOrigin.y);
            PlaceCore(CellOwner.P1, new CellCoord(center.X - CoreSizeCells / 2, center.Z - CoreSizeCells / 2));
        }

        /// <summary>高台塔的物理占地格：按塔底面宽实测枚举（1 米=2×2 格；不足半格至少 1 格）。</summary>
        public static List<CellCoord> CoreDeviceFootprintCells(Vector2 towerCenter, float widthMeters)
        {
            var half = widthMeters * 0.5f;
            var minCell = WorldToCell(towerCenter.x - half, towerCenter.y - half);
            var maxCell = WorldToCell(towerCenter.x + half - 0.01f, towerCenter.y + half - 0.01f);
            var result = new List<CellCoord>();
            for (var x = minCell.X; x <= maxCell.X; x++)
            {
                for (var z = minCell.Z; z <= maxCell.Z; z++)
                {
                    result.Add(new CellCoord(x, z));
                }
            }
            return result;
        }

        /// <summary>建筑落地（建设即扩张）：登记圈地来源（占位中心 5×5 格）并重算所有权。
        /// 数量上限不在网格层（ShopService 购买侧管：机枪塔 4 / 普通设施共享 30）。</summary>
        public bool RegisterBuilding(CellCoord position, CellOwner owner)
        {
            var r = BuildingClaimCells / 2;
            return RegisterBuilding(position, owner, position.X - r, position.Z - r, position.X + r, position.Z + r);
        }

        /// <summary>矩形圈地注册（2026-11-04 实测定案：机枪塔落地圈地=攻击范围，占地±3 格=8×8）。
        /// position 仍作移除键（RemoveBuilding 按它找）。越界格由所有权重算内部跳过。</summary>
        public bool RegisterBuilding(CellCoord position, CellOwner owner, int minX, int minZ, int maxX, int maxZ)
        {
            if (!InBounds(position) || owner == CellOwner.Neutral)
            {
                return false;
            }
            buildings.Add(new Building
            {
                Position = position, Owner = owner,
                MinX = minX, MinZ = minZ, MaxX = maxX, MaxZ = maxZ
            });
            ComputeOwnership();
            return true;
        }

        public bool RemoveBuilding(CellCoord position)
        {
            for (var i = 0; i < buildings.Count; i++)
            {
                if (buildings[i].Position.Equals(position))
                {
                    buildings.RemoveAt(i);
                    ComputeOwnership();
                    return true;
                }
            }
            return false;
        }

        public int CountBuildings(CellOwner owner)
        {
            var count = 0;
            foreach (var b in buildings)
            {
                if (b.Owner == owner)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>当前生效归属（含倒计时期间保留的格子）</summary>
        public CellOwner GetOwner(CellCoord c) => owners[c.X, c.Z];

        /// <summary>世界坐标是否在 P1 己方地盘内（2026-11-05 站桩制：僵尸进地盘减速 15% 的判定源）</summary>
        public bool IsInPlayerTerritory(float worldX, float worldZ) =>
            GetOwner(WorldToCell(worldX, worldZ)) == CellOwner.P1;

        public List<CellCoord> GetValidCells(CellOwner player)
        {
            var result = new List<CellCoord>();
            for (var x = 0; x < GridSize; x++)
            {
                for (var z = 0; z < GridSize; z++)
                {
                    if (owners[x, z] == player)
                    {
                        result.Add(new CellCoord(x, z));
                    }
                }
            }
            return result;
        }

        public int GetOwnedCellCount(CellOwner player)
        {
            var count = 0;
            for (var x = 0; x < GridSize; x++)
            {
                for (var z = 0; z < GridSize; z++)
                {
                    if (owners[x, z] == player)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        /// <summary>有效地盘面积（docs/02：格数 × 0.25㎡）</summary>
        public float GetAreaSqm(CellOwner player) => GetOwnedCellCount(player) * CellAreaSqm;

        /// <summary>接壤判定（策划案 9.1）：任意有效网格四邻接即接壤，斜角不算</summary>
        public bool CheckAdjacency(CellOwner a, CellOwner b)
        {
            for (var x = 0; x < GridSize; x++)
            {
                for (var z = 0; z < GridSize; z++)
                {
                    if (owners[x, z] != a)
                    {
                        continue;
                    }
                    if (x + 1 < GridSize && owners[x + 1, z] == b ||
                        x > 0 && owners[x - 1, z] == b ||
                        z + 1 < GridSize && owners[x, z + 1] == b ||
                        z > 0 && owners[x, z - 1] == b)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 重建所有权：候选（核心优先、单主建筑 5×5 圈地、多主重叠记争议=中立）
        /// → 从各核心 BFS 四邻接连通填充 → 失效且非核心格进入 5 秒中立化倒计时。
        /// </summary>
        public void ComputeOwnership()
        {
            var candidates = BuildCandidates();
            var valid = ComputeValid(candidates);

            for (var x = 0; x < GridSize; x++)
            {
                for (var z = 0; z < GridSize; z++)
                {
                    var cell = new CellCoord(x, z);
                    var oldOwner = owners[x, z];
                    if (oldOwner != CellOwner.Neutral && valid[x, z] == CellOwner.Neutral &&
                        !IsCoreCell(cell, oldOwner))
                    {
                        AddPending(cell, oldOwner);
                    }
                }
            }

            // 倒计时内重新有效（重建建筑/重新连通）→ 取消中立化
            pending.RemoveAll(p => valid[p.Cell.X, p.Cell.Z] != CellOwner.Neutral);

            for (var x = 0; x < GridSize; x++)
            {
                for (var z = 0; z < GridSize; z++)
                {
                    owners[x, z] = valid[x, z];
                }
            }

            // 预警期保留原归属
            foreach (var p in pending)
            {
                owners[p.Cell.X, p.Cell.Z] = p.PreviousOwner;
            }

            Changed?.Invoke();
        }

        /// <summary>推进倒计时；到期的格子转野区并记录到 LostCells（供道具离线 30 秒销毁逻辑查询）</summary>
        public void Tick(float deltaSeconds)
        {
            gridSeconds += deltaSeconds;
            var anyExpired = false;
            for (var i = pending.Count - 1; i >= 0; i--)
            {
                var p = pending[i];
                p.RemainingSeconds -= deltaSeconds;
                if (p.RemainingSeconds <= 0f)
                {
                    owners[p.Cell.X, p.Cell.Z] = CellOwner.Neutral;
                    lostCells.Add(new LostCellRecord
                    {
                        Cell = p.Cell,
                        PreviousOwner = p.PreviousOwner,
                        LostAtGridSeconds = gridSeconds
                    });
                    pending.RemoveAt(i);
                    anyExpired = true;
                }
                else
                {
                    pending[i] = p;
                }
            }
            if (anyExpired)
            {
                Changed?.Invoke();
            }
        }

        public List<PendingNeutralizeInfo> GetPendingSnapshot()
        {
            var result = new List<PendingNeutralizeInfo>(pending.Count);
            foreach (var p in pending)
            {
                result.Add(new PendingNeutralizeInfo
                {
                    Cell = p.Cell,
                    PreviousOwner = p.PreviousOwner,
                    RemainingSeconds = p.RemainingSeconds
                });
            }
            return result;
        }

        public List<Building> GetBuildingSnapshot() => new List<Building>(buildings);

        /// <summary>docs/02 规则 5：失去归属网格上的道具 30 秒未回归则销毁不退款</summary>
        public static bool ShouldDestroyOfflineProp(float lostAtGridSeconds, float nowGridSeconds) =>
            nowGridSeconds - lostAtGridSeconds >= OfflinePropDestroySeconds;

        private void AddPending(CellCoord cell, CellOwner previousOwner)
        {
            foreach (var p in pending)
            {
                if (p.Cell.Equals(cell))
                {
                    return;
                }
            }
            pending.Add(new PendingNeutralize
            {
                Cell = cell,
                PreviousOwner = previousOwner,
                RemainingSeconds = NeutralizeDelaySeconds
            });
        }

        private CellOwner[,] BuildCandidates()
        {
            var candidates = new CellOwner[GridSize, GridSize];
            foreach (var kv in coreOrigins)
            {
                foreach (var cell in CoreCells(kv.Value, coreSizes[kv.Key]))
                {
                    candidates[cell.X, cell.Z] = kv.Key;
                }
            }

            // 建筑圈地（建设即扩张；矩形=各建筑自带 Min/Max，默认 5×5、机枪塔 8×8 射界）；
            // 多主重叠 = 争议格，保持中立（docs 未定义，取保守值）
            var claims = new Dictionary<CellCoord, int>();
            foreach (var b in buildings)
            {
                for (var x = b.MinX; x <= b.MaxX; x++)
                {
                    for (var z = b.MinZ; z <= b.MaxZ; z++)
                    {
                        var c = new CellCoord(x, z);
                        if (!InBounds(c))
                        {
                            continue;
                        }
                        claims[c] = claims.TryGetValue(c, out var mask)
                            ? mask | (1 << (int)b.Owner)
                            : 1 << (int)b.Owner;
                    }
                }
            }

            foreach (var kv in claims)
            {
                if (CountBits(kv.Value) != 1)
                {
                    continue;
                }
                var c = kv.Key;
                // 核心区归属优先，永不被建筑圈地夺取
                if (candidates[c.X, c.Z] == CellOwner.Neutral)
                {
                    for (var owner = 1; owner <= 4; owner++)
                    {
                        if (kv.Value == 1 << owner)
                        {
                            candidates[c.X, c.Z] = (CellOwner)owner;
                            break;
                        }
                    }
                }
            }
            return candidates;
        }

        private CellOwner[,] ComputeValid(CellOwner[,] candidates)
        {
            var valid = new CellOwner[GridSize, GridSize];
            foreach (var kv in coreOrigins)
            {
                var player = kv.Key;
                var queue = new Queue<CellCoord>();
                foreach (var c in CoreCells(kv.Value, coreSizes[player]))
                {
                    if (valid[c.X, c.Z] != player)
                    {
                        valid[c.X, c.Z] = player;
                        queue.Enqueue(c);
                    }
                }

                while (queue.Count > 0)
                {
                    var cur = queue.Dequeue();
                    foreach (var n in Neighbors(cur))
                    {
                        if (!InBounds(n))
                        {
                            continue;
                        }
                        if (candidates[n.X, n.Z] == player && valid[n.X, n.Z] != player)
                        {
                            valid[n.X, n.Z] = player;
                            queue.Enqueue(n);
                        }
                    }
                }
            }
            return valid;
        }

        private static IEnumerable<CellCoord> CoreCells(CellCoord origin, int sizeCells)
        {
            for (var dx = 0; dx < sizeCells; dx++)
            {
                for (var dz = 0; dz < sizeCells; dz++)
                {
                    yield return new CellCoord(origin.X + dx, origin.Z + dz);
                }
            }
        }

        private static IEnumerable<CellCoord> Neighbors(CellCoord c)
        {
            yield return new CellCoord(c.X + 1, c.Z);
            yield return new CellCoord(c.X - 1, c.Z);
            yield return new CellCoord(c.X, c.Z + 1);
            yield return new CellCoord(c.X, c.Z - 1);
        }

        private bool IsCoreCell(CellCoord cell, CellOwner owner)
        {
            return coreOrigins.TryGetValue(owner, out var origin) &&
                   coreSizes.TryGetValue(owner, out var size) &&
                   cell.X >= origin.X && cell.X < origin.X + size &&
                   cell.Z >= origin.Z && cell.Z < origin.Z + size;
        }

        private static int CountBits(int value)
        {
            var count = 0;
            while (value != 0)
            {
                value &= value - 1;
                count++;
            }
            return count;
        }
    }
}
