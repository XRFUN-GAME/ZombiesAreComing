#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace MoriGame.Territory
{
    /// <summary>
    /// 灰盒地盘可视化（仅 Editor / Development 构建编译）。
    /// 单 Mesh 顶点色平铺 56×56 格归属（0.5 米格）：P1 蓝 / P2 橙 / P3 紫 / P4 绿 / 野区灰，
    /// 5 秒预警格 3Hz 闪红；建筑增删与倒计时到期经 TerritoryGrid.Changed 事件驱动整刷。
    /// 桌面操作：B 放建筑 / N 拆最近建筑 / T 切换归属方；VR：右手柄握持放置、左手柄握持拆除。
    /// 网格所有权在 MatchBootstrap（正式玩法层），本组件只做绑定可视化；
    /// dev 放的建筑只登记圈地来源（无实体），落地建筑实体由 BuildManager 创建。
    /// [ExecuteAlways]（2026-11-02）：编辑态不 Play 也直接显示棋盘（全中性灰 + 格线），
    /// 归属色/闪烁/开发输入仅运行时。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    public class TerritoryGridDebugger : MonoBehaviour
    {
        [Header("放建筑的默认归属方（T 键循环）")]
        [SerializeField] private CellOwner playerSide = CellOwner.P1;

        [Header("场景引用的顶点色材质（保证 shader 进构建；为空时回退 Shader.Find，仅编辑器可用）")]
        public Material overlayMaterial;

        private const float CellYOffset = 0.05f; // 2026-11-09 实测：0.02 在 10 米外掠射角与瓦片顶 z-fight（偶发消失）
        private const float FlashHz = 3f;
        private const float PlaceAheadMeters = 1.5f;

        private static readonly Color PendingRed = new Color(1f, 0.12f, 0.1f, 0.6f);

        private readonly Color[] ownerColors =
        {
            new Color(0f, 0f, 0f, 0f),             // Neutral 全透明（2026-11-09 用户定稿：只看己方地盘，不画野区/网格线）
            new Color(0.16f, 0.45f, 1f, 0.48f),    // P1 蓝
            new Color(1f, 0.55f, 0.15f, 0.48f),    // P2 橙（单机不出现，网格能力保留）
            new Color(0.72f, 0.3f, 1f, 0.48f),     // P3 紫
            new Color(0.3f, 0.9f, 0.4f, 0.48f)     // P4 绿
        };

        private TerritoryGrid bound;

        /// <summary>绑定的正式网格（MatchBootstrap.Grid；绑定前为 null）</summary>
        public TerritoryGrid Grid => bound;

        private Mesh mesh;
        private Color32[] cellColors;
        private Transform head;
        private bool rightGripReady = true;
        private bool leftGripReady = true;

        private void Awake()
        {
            mesh = new Mesh { name = "TerritoryGridCells" };
            mesh.hideFlags = HideFlags.DontSave; // 内存网格不进场景文件；每次加载/重编译由 Awake 重建
            mesh.MarkDynamic();
            BuildTopology();
            GetComponent<MeshFilter>().sharedMesh = mesh;

            var overlay = overlayMaterial;
            if (overlay == null)
            {
                var shader = Shader.Find("MoriGame/TerritoryGrid");
                if (shader == null)
                {
                    Debug.LogError("[MoriGame] 缺少 overlayMaterial 且 Shader.Find 失败，地盘贴花不可见");
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                overlay = new Material(shader);
            }
            GetComponent<MeshRenderer>().sharedMaterial = overlay;
        }

        private void OnDestroy()
        {
            if (bound != null)
            {
                bound.Changed -= OnGridChanged;
            }
            if (mesh != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(mesh);
                }
                else
                {
                    DestroyImmediate(mesh); // 编辑态（ExecuteAlways）必须 DestroyImmediate
                }
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
            {
                return; // 编辑态：拓扑与中性色已在 Awake 铺好，静态展示棋盘；绑定/闪烁/开发输入仅运行时
            }
            if (bound == null)
            {
                var match = MatchBootstrap.Instance;
                if (match == null || match.Grid == null)
                {
                    return;
                }
                bound = match.Grid;
                // 物体本体挂锚点（局部顶点+transform 定位）：包围盒随 transform 走，
                // 与静态锚点写入时机完全解耦（2026-11-09 视锥剔除整板消失实测修复）
                var origin = TerritoryGrid.WorldOrigin;
                transform.position = new Vector3(origin.x, TerritoryGrid.WorldFloorY, origin.y);
                transform.rotation = Quaternion.identity;
                BuildTopology();
                bound.Changed += OnGridChanged;
                OnGridChanged();
            }
            FlashPendingCells();
            HandleDevInput();
        }

        private void BuildTopology()
        {
            // 2026-11-09 用户定稿：只画己方地盘色块——格线（随视角 z-fight 消失）与野区灰全部移除，
            // 中性格 alpha=0 不可见，仅归属格（蓝）与 5 秒预警闪红可见。
            // 顶点用**局部坐标**（物体本体挂锚点）：mesh.bounds 稳定不随静态锚点写入时机漂移——
            // 旧实现顶点直接写世界坐标，bounds 曾停在老原点导致视锥剔除随视角整板消失（实测）。
            var n = TerritoryGrid.GridSize;
            var cells = n * n;
            var verts = new Vector3[cells * 4];
            var tris = new int[cells * 6];
            cellColors = new Color32[verts.Length];
            const float inset = 0.015f; // 格间留缝露出地面（0.5 米格按比例缩半）
            var size = TerritoryGrid.CellSizeMeters;
            var vi = 0;
            var ti = 0;
            for (var z = 0; z < n; z++)
            {
                for (var x = 0; x < n; x++)
                {
                    var lx = (x - n / 2) * size;
                    var lz = (z - n / 2) * size;
                    var b = vi;
                    verts[vi++] = new Vector3(lx + inset, CellYOffset, lz + inset);
                    verts[vi++] = new Vector3(lx + size - inset, CellYOffset, lz + inset);
                    verts[vi++] = new Vector3(lx + size - inset, CellYOffset, lz + size - inset);
                    verts[vi++] = new Vector3(lx + inset, CellYOffset, lz + size - inset);
                    tris[ti++] = b; tris[ti++] = b + 2; tris[ti++] = b + 1;
                    tris[ti++] = b; tris[ti++] = b + 3; tris[ti++] = b + 2;
                }
            }
            var neutral = (Color32)ownerColors[0]; // 全透明：编辑态无网格数据时整板不可见
            for (var k = 0; k < cells * 4; k++)
            {
                cellColors[k] = neutral;
            }
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.SetColors(cellColors);
        }

        private void OnGridChanged()
        {
            if (mesh == null)
            {
                return;
            }
            var n = TerritoryGrid.GridSize;
            var pendingCells = new HashSet<CellCoord>();
            foreach (var p in Grid.GetPendingSnapshot())
            {
                pendingCells.Add(p.Cell);
            }
            for (var z = 0; z < n; z++)
            {
                for (var x = 0; x < n; x++)
                {
                    var c = new CellCoord(x, z);
                    var color = pendingCells.Contains(c)
                        ? (Color32)PendingRed
                        : (Color32)ownerColors[(int)Grid.GetOwner(c)];
                    SetCellColor(x, z, color);
                }
            }
            mesh.SetColors(cellColors);
        }

        private void SetCellColor(int x, int z, Color32 color)
        {
            var i = (z * TerritoryGrid.GridSize + x) * 4;
            cellColors[i] = color;
            cellColors[i + 1] = color;
            cellColors[i + 2] = color;
            cellColors[i + 3] = color;
        }

        private void FlashPendingCells()
        {
            if (Grid.PendingNeutralizeCount == 0)
            {
                return;
            }
            var flash = PendingRed;
            flash.a = Mathf.Lerp(0.15f, 0.75f,
                (Mathf.Sin(Time.time * Mathf.PI * 2f * FlashHz) + 1f) * 0.5f);
            var flash32 = (Color32)flash;
            foreach (var p in Grid.GetPendingSnapshot())
            {
                SetCellColor(p.Cell.X, p.Cell.Z, flash32);
            }
            mesh.SetColors(cellColors);
        }

        private void HandleDevInput()
        {
            if (head == null)
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    return;
                }
                head = cam.transform;
            }

            var place = Input.GetKeyDown(KeyCode.B);
            var remove = Input.GetKeyDown(KeyCode.N);
            if (Input.GetKeyDown(KeyCode.T))
            {
                playerSide = playerSide >= CellOwner.P4 ? CellOwner.P1 : playerSide + 1;
                Debug.Log($"[MoriGame] 建筑归属方 → {playerSide}");
            }

            if (TryGetButton(XRNode.RightHand, CommonUsages.gripButton, out var rightGrip))
            {
                if (rightGrip && rightGripReady)
                {
                    place = true;
                    rightGripReady = false;
                }
                else if (!rightGrip)
                {
                    rightGripReady = true;
                }
            }
            if (TryGetButton(XRNode.LeftHand, CommonUsages.gripButton, out var leftGrip))
            {
                if (leftGrip && leftGripReady)
                {
                    remove = true;
                    leftGripReady = false;
                }
                else if (!leftGrip)
                {
                    leftGripReady = true;
                }
            }

            if (!place && !remove)
            {
                return;
            }
            var flatForward = new Vector3(head.forward.x, 0f, head.forward.z).normalized;
            var target = head.position + flatForward * PlaceAheadMeters;
            var cell = TerritoryGrid.WorldToCell(target.x, target.z);

            if (place)
            {
                if (TerritoryGrid.InBounds(cell) && Grid.RegisterBuilding(cell, playerSide))
                {
                    Debug.Log($"[MoriGame] 建筑 {cell} → {playerSide}（共 {Grid.CountBuildings(playerSide)}）");
                }
                else
                {
                    Debug.LogWarning($"[MoriGame] 建筑放置失败 {cell}（越界）");
                }
            }
            else
            {
                RemoveNearestBuilding(cell);
            }
        }

        public void RemoveNearestBuilding(CellCoord from)
        {
            var buildings = Grid.GetBuildingSnapshot();
            if (buildings.Count == 0)
            {
                return;
            }
            var best = buildings[0];
            var bestDist = int.MaxValue;
            foreach (var b in buildings)
            {
                var dx = b.Position.X - from.X;
                var dz = b.Position.Z - from.Z;
                var d = dx * dx + dz * dz;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = b;
                }
            }
            Grid.RemoveBuilding(best.Position);
            Debug.Log($"[MoriGame] 拆除建筑 {best.Position}");
        }

        private static bool TryGetButton(XRNode node, InputFeatureUsage<bool> usage, out bool value)
        {
            value = false;
            var device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(usage, out value);
        }
    }
}
#endif
