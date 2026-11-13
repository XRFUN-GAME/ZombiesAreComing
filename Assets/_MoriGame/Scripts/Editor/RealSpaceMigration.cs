using System;
using System.Collections.Generic;
using System.IO;
using MoriGame.Devices;
using MoriGame.Waves;
using MoriGame.Zombies;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace MoriGame.Editor
{
    /// <summary>高台制迁移 v12（2026-11-09）：用户手工场景（RealSpace 玩法树 + CUBE 地图）的增量迁移，**禁止全量重搭**：
    /// ① 给 CUBE 地形瓦片批量加 BoxCollider + NavigationStatic（用户已授权）——僵尸踩视觉地面（沙砖顶 0.25），
    /// 导航可见高低起伏；② 塔底座/工作台按**射线实测地面**精确落地（用户手工值可能差半格）；
    /// ③ TowerVisual 高度归一 0.5（用户定案，X/Z 尺度保留用户值），RealSpace Y=塔顶（玩家 XR 局部归零即脚位）；
    /// ④ 遗留烘焙 agentClimb 提到 0.6（普通台阶 0.25—0.5 不阻拦僵尸）；⑤ 五点采样自检。
    /// 触发：Builds/migrate-realspace.trigger。可重复执行（幂等）。</summary>
    public static class RealSpaceMigration
    {
        private const string AnchorName = "RealSpace";
        /// <summary>探针偏移：塔位地面在锚点 XZ+1.2 米（避开塔底面）</summary>
        private static readonly Vector2 TowerProbeOffset = new Vector2(1.2f, 0f);
        private const float FallbackGroundY = 0.5f;

        public static void Run()
        {
            EditorSceneManager.SaveOpenScenes();
            var scene = EditorSceneManager.OpenScene("Assets/_MoriGame/Scenes/MoriMain.unity");

            var anchor = FindAnchor(scene);
            var match = UnityEngine.Object.FindFirstObjectByType<MatchBootstrap>();
            if (match == null)
            {
                throw new InvalidOperationException("场景里找不到 MatchBootstrap");
            }
            match.playSpaceAnchor = anchor;
            EditorUtility.SetDirty(match);

            var tileCount = AddTileColliders();

            // 塔体：尺度保留用户值，仅居中；塔顶=渲染包围盒实测（玩家脚位）
            var towerVisual = PrepareTowerVisual();
            var towerTopY = towerVisual.GetComponent<MeshRenderer>().bounds.max.y;

            // 实测地面（瓦片碰撞体就位后）；先挪锚点（RealSpace Y=塔顶=玩家脚位）再落位子物体，
            // 否则挪锚会带走已放置的子物体
            var towerGroundY = GroundProbe(anchor.position + new Vector3(TowerProbeOffset.x, 5f, TowerProbeOffset.y));
            var p0 = anchor.position;
            anchor.position = new Vector3(p0.x, towerTopY, p0.z);
            EditorUtility.SetDirty(anchor.gameObject);

            var tower = BuildTower(anchor, towerGroundY);
            ReanchorUnder(anchor, "XR Origin", Vector3.zero, Quaternion.identity);
            ReanchorUnder(anchor, "RespawnPoint", Vector3.zero, Quaternion.identity);
            ReanchorUnder(anchor, "Spawn_P1", Vector3.zero, Quaternion.identity);
            // 工作台坐标=用户手改定稿（2026-11-09 局部 (0,0,0)，塔顶同高），迁移**不碰**——只校验父子挂载
            var workbench = GameObject.Find("Workbench");
            if (workbench != null && workbench.transform.parent != anchor)
            {
                workbench.transform.SetParent(anchor, true);
                EditorUtility.SetDirty(workbench);
            }
            EnsureTerritoryDebugger(anchor);
            AddNormalModelVariants();
            ApplyWorkbenchAnchors();
            InjectPropVisualPrefabs();

            var crate = UnityEngine.Object.FindFirstObjectByType<Economy.SupplyCrate>();
            if (crate != null)
            {
                crate.gazeRangeMeters = 3f;
                EditorUtility.SetDirty(crate);
            }

            RemoveLegacyHelpers();
            BakeMapNavmesh(anchor, towerGroundY);

            // 拍波节奏权威值（2026-11-10 用户定案 12 秒）：场景序列化值可能是旧默认，迁移强制对齐
            var director = UnityEngine.Object.FindFirstObjectByType<WaveDirector>();
            if (director != null && director.waveIntervalSeconds != 12f)
            {
                director.waveIntervalSeconds = 12f;
                EditorUtility.SetDirty(director);
                Debug.Log("[MoriGame] 拍距已对齐 12 秒（迁移权威值）");
            }

            // 音效库注入（2026-11-10 音效系统）：运行时经 MatchBootstrap.Sfx 访问
            var sfxAsset = AssetDatabase.LoadAssetAtPath<MoriGame.Audio.SfxLibrary>(
                "Assets/_MoriGame/Configs/SfxLibrary.asset");
            if (sfxAsset != null && match.sfxLibrary != sfxAsset)
            {
                match.sfxLibrary = sfxAsset;
                EditorUtility.SetDirty(match);
                Debug.Log("[MoriGame] MatchBootstrap 已注入 SfxLibrary");
            }

            // T7 遗留清理（2026-11-10）：WristMenu 组件已移出 Assets，场景残留面板一并移除防 missing script
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == "WristMenuPanel")
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    Debug.Log("[MoriGame] 已移除场景残留 WristMenuPanel（M2-T7 清理）");
                }
            }

            // 弹速权威值（2026-11-11 用户：25×1.5，场景序列化旧值强制对齐）
            var projectiles = UnityEngine.Object.FindFirstObjectByType<MoriGame.Combat.ProjectilePool>();
            if (projectiles != null && projectiles.speedMetersPerSecond != 37.5f)
            {
                projectiles.speedMetersPerSecond = 37.5f;
                EditorUtility.SetDirty(projectiles);
                Debug.Log("[MoriGame] 弹速已对齐 37.5 米/秒（迁移权威值）");
            }

            // 特效库注入（2026-11-11 特效系统）：运行时经 MatchBootstrap.Vfx 访问
            var vfxAsset = AssetDatabase.LoadAssetAtPath<MoriGame.Vfx.VfxLibrary>(
                "Assets/_MoriGame/Configs/VfxLibrary.asset");
            if (vfxAsset != null && match.vfxLibrary != vfxAsset)
            {
                match.vfxLibrary = vfxAsset;
                EditorUtility.SetDirty(match);
                Debug.Log("[MoriGame] MatchBootstrap 已注入 VfxLibrary");
            }

            InjectZombieNameFont(); // 血条类型名字体（含缺字自愈）

            // 收尾摘要先取值（教学关步骤会切场景卸载本场景对象，切换后再读会 MissingReferenceException）
            var towerCells = MoriGame.Territory.TerritoryGrid
                .CoreDeviceFootprintCells(new Vector2(tower.position.x, tower.position.z),
                    towerVisual.lossyScale.x).Count;
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            ApplyWorkbenchAnchorsToTutorial(); // 教学关同步悬浮锚点化（开→改→存→回主场景）
            Debug.Log("[MoriGame] 高台制迁移完成：tiles+" + tileCount +
                      " towerGround=" + towerGroundY.ToString("F3") +
                      " towerTop=" + towerTopY.ToString("F3") +
                      " towerCells=" + towerCells);
        }

        private static Transform FindAnchor(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == AnchorName)
                {
                    return root.transform;
                }
            }
            throw new InvalidOperationException("场景里找不到 RealSpace 根物体");
        }

        /// <summary>向下射线取地面高度（瓦片碰撞体）； miss 时回退沙砖顶面常量</summary>
        public static float GroundProbe(Vector3 from)
        {
            return Physics.Raycast(from, Vector3.down, out var hit, 20f)
                ? hit.point.y
                : FallbackGroundY;
        }

        /// <summary>CUBE 地形瓦片批量加碰撞体与导航静态标记（幂等：已有碰撞体的跳过）</summary>
        private static int AddTileColliders()
        {
            var cubeRoot = GameObject.Find("CUBE");
            if (cubeRoot == null)
            {
                Debug.LogWarning("[MoriGame] 场景里没有 CUBE 地形根，跳过瓦片碰撞");
                return 0;
            }
            var count = 0;
            foreach (var filter in cubeRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                var go = filter.gameObject;
                if (go.GetComponent<Collider>() != null || go.GetComponent<MeshRenderer>() == null)
                {
                    continue;
                }
                go.AddComponent<BoxCollider>();
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
                count++;
            }
            Debug.Log("[MoriGame] 地形瓦片碰撞体补齐：" + count + " 块");
            return count;
        }

        /// <summary>TowerVisual：高度归一 0.5，居中于 CoreDevice 局部原点上方</summary>
        private static Transform PrepareTowerVisual()
        {
            var core = UnityEngine.Object.FindFirstObjectByType<CoreDevice>();
            if (core == null)
            {
                throw new InvalidOperationException("场景里找不到 CoreDevice（高台载体）");
            }
            var towerVisual = core.transform.Find("TowerVisual");
            if (towerVisual == null)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
                cube.name = "TowerVisual";
                towerVisual = cube.transform;
                towerVisual.SetParent(core.transform, false);
                towerVisual.GetComponent<MeshRenderer>().sharedMaterial = EnsureTowerMaterial();
                towerVisual.localScale = new Vector3(1f, 0.5f, 1f);
            }
            towerVisual.localPosition = new Vector3(0f, towerVisual.localScale.y * 0.5f, 0f);
            towerVisual.localRotation = Quaternion.identity;
            return towerVisual;
        }

        /// <summary>高台落地：底座贴实测地面，碰撞/障碍随 TowerVisual 尺度；返回塔 Transform</summary>
        private static Transform BuildTower(Transform anchor, float groundY)
        {
            var core = UnityEngine.Object.FindFirstObjectByType<CoreDevice>();
            var t = core.transform;
            if (t.parent != anchor)
            {
                t.SetParent(anchor, true);
            }
            t.localPosition = new Vector3(0f, groundY - anchor.position.y, 0f);
            t.localRotation = Quaternion.identity;

            var towerVisual = t.Find("TowerVisual");
            var size = towerVisual.localScale;
            var oldVisual = t.Find("Visual");
            if (oldVisual != null && oldVisual.gameObject.activeSelf)
            {
                oldVisual.gameObject.SetActive(false); // 旧 ZhongKong 大模型留档禁用
            }

            var collider = core.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = core.gameObject.AddComponent<BoxCollider>();
            }
            collider.center = new Vector3(0f, size.y * 0.5f, 0f);
            collider.size = size;
            var obstacle = core.GetComponent<NavMeshObstacle>();
            if (obstacle == null)
            {
                obstacle = core.gameObject.AddComponent<NavMeshObstacle>();
            }
            obstacle.carving = true;
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = collider.center;
            obstacle.size = collider.size;
            EditorUtility.SetDirty(core.gameObject);
            return t;
        }

        /// <summary>地盘可视化调试器（用户手工场景缺它——初始地盘 5×5 没有任何视觉指示，
        /// 玩家从塔顶看出去全是野区红块）；材质/setup 时代资产复用（自定义 shader 场景引用保进构建）</summary>
        private static void EnsureTerritoryDebugger(Transform anchor)
        {
            var existed = GameObject.Find("TerritoryDebugger");
            if (existed != null)
            {
                existed.transform.position = anchor.position; // 局部顶点制：物体=锚点位置（2026-11-09）
                return;
            }
            var go = new GameObject("TerritoryDebugger");
            go.transform.position = anchor.position;
            var debugger = go.AddComponent<MoriGame.Territory.TerritoryGridDebugger>();
            const string matPath = "Assets/_MoriGame/Art/Materials/TerritoryGridOverlay.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (material == null)
            {
                var shader = Shader.Find("MoriGame/TerritoryGrid");
                if (shader == null)
                {
                    throw new InvalidOperationException("找不到 MoriGame/TerritoryGrid shader");
                }
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, matPath);
            }
            debugger.overlayMaterial = material;
            EditorUtility.SetDirty(go);
            Debug.Log("[MoriGame] 已补建 TerritoryDebugger（地盘可视化）");
        }

        /// <summary>工作台悬浮锚点制（2026-11-12 用户恢复，今日备份实体版至 Builds/SceneBackups/*-solid-*）：
        /// 删台面视觉件及旧工作台 BoxCollider/NavMeshObstacle；玩家承伤迁移到 XR 根的 0.25×1.8 米触发胶囊。
        /// 枪锚点右前 45°，建筑锚点左前 45°/95°；AnchorFloat 浮动 + 淡金微光圈，重复执行不叠加组件。</summary>
        public static void ApplyWorkbenchAnchors()
        {
            var workbench = GameObject.Find("Workbench");
            if (workbench == null)
            {
                Debug.LogWarning("[MoriGame] 迁移跳过：场景里没有 Workbench");
                return;
            }
            var root = workbench.transform;

            // 悬浮版无实体台面：移除旧大盒与空气导航障碍，玩家承伤迁移到 XR 根的小胶囊
            foreach (var legacyBox in root.GetComponents<BoxCollider>())
            {
                UnityEngine.Object.DestroyImmediate(legacyBox);
            }
            var legacyObstacle = root.GetComponent<NavMeshObstacle>();
            if (legacyObstacle != null)
            {
                UnityEngine.Object.DestroyImmediate(legacyObstacle);
            }
            var match = UnityEngine.Object.FindFirstObjectByType<MatchBootstrap>();
            if (match != null && match.playerRig != null)
            {
                match.playerAttackTarget = match.playerRig;
                MoriSceneSetup.EnsurePlayerHitProxy(match.playerRig);
                EditorUtility.SetDirty(match);
                EditorUtility.SetDirty(match.playerRig.gameObject);
            }

            // ① 台面视觉件删除
            var removed = 0;
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                var name = root.GetChild(i).name;
                if (name.StartsWith("ArcTop_") || name.StartsWith("WorkbenchLeg") || name == "PistolSlotMarker")
                {
                    UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
                    removed++;
                }
            }

            const float distance = 0.45f;
            const float heightY = 0.70f;
            var gold = new Material(Shader.Find("Sprites/Default")) { color = new Color(1f, 0.84f, 0.25f, 0.5f) };

            // ② 枪锚点：右前 45° + 浮动 + 微光圈
            var slotAnchor = root.Find("PistolSlotAnchor");
            if (slotAnchor != null)
            {
                slotAnchor.localPosition = new Vector3(
                    Mathf.Sin(45f * Mathf.Deg2Rad) * distance, heightY + 0.16f, // 枪锚点定版 0.86 米，不随环高度调整
                    Mathf.Cos(45f * Mathf.Deg2Rad) * distance);
                if (slotAnchor.GetComponent<MoriGame.Build.AnchorFloat>() == null)
                {
                    slotAnchor.gameObject.AddComponent<MoriGame.Build.AnchorFloat>();
                }
                AttachRing(slotAnchor, gold, 0.16f, heightY - slotAnchor.localPosition.y); // 仅枪环降至建筑环同高 0.70 米
            }

            // ③ 建筑锚点：左前 45°/70° + 浮动 + 微光圈；删底板；模型回浮空位
            foreach (var standName in new[] { "Stand_AutoTurret", "Stand_ElectricFence" })
            {
                var stand = GameObject.Find(standName);
                if (stand == null)
                {
                    continue;
                }
                var t = stand.transform;
                var isFence = standName.EndsWith("ElectricFence");
                var angle = isFence ? 95f : 45f; // 用户 2026-11-12：两建筑模型拉开（原 70° 距离太近互相抢触发）
                t.localPosition = new Vector3(-Mathf.Sin(angle * Mathf.Deg2Rad) * distance, heightY,
                    Mathf.Cos(angle * Mathf.Deg2Rad) * distance);
                if (t.GetComponent<MoriGame.Build.AnchorFloat>() == null)
                {
                    t.gameObject.AddComponent<MoriGame.Build.AnchorFloat>();
                }
                AttachRing(t, gold, 0.10f, 0f); // 微光圈随模型缩小（原 0.14）
                var plate = t.Find("BasePlate");
                if (plate != null)
                {
                    UnityEngine.Object.DestroyImmediate(plate.gameObject);
                }
                MoriSceneSetup.RebuildStandDisplay(t, !isFence); // 展示模换现用正式预制体 1:5 + 抓取半径 0.18（两场景同步）
            }
            EditorUtility.SetDirty(workbench);
            Debug.Log($"[MoriGame] 工作台悬浮锚点化完成：台面件删除 {removed}，锚点浮动+微光圈就位");
        }

        /// <summary>僵尸血条类型名字体：池组件序列化注入 MoriZhCN_SDF；静态图集缺字形时自愈补字
        /// （2026-11-12 实测缺"普/者"——GB2312 高频截断把低频字裁掉，"普通感染者"会渲染成方块）</summary>
        private static void InjectZombieNameFont()
        {
            var pool = UnityEngine.Object.FindFirstObjectByType<ZombiePool>();
            if (pool == null)
            {
                return;
            }
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/_MoriGame/Art/Fonts/MoriZhCN_SDF.asset");
            if (font == null)
            {
                Debug.LogWarning("[MoriGame] 类型名字体缺失：MoriZhCN_SDF.asset 不存在");
                return;
            }
            if (font.HasCharacters("普通感染者疾行者喷吐者盾卫蛮兽督战者", out var missing))
            {
                if (pool.nameLabelFont == font)
                {
                    return; // 字形齐 + 已注入，幂等退出
                }
            }
            else if (missing != null && missing.Count > 0)
            {
                var chars = new System.Text.StringBuilder();
                foreach (var u in missing)
                {
                    chars.Append((char)u);
                }
                font.TryAddCharacters(chars.ToString(), out var stillMissing);
                EditorUtility.SetDirty(font);
                Debug.Log("[MoriGame] 类型名字体补字 " + chars.Length + " 个" +
                          (string.IsNullOrEmpty(stillMissing) ? "" : "（仍缺：" + stillMissing + "）"));
            }
            pool.nameLabelFont = font;
            EditorUtility.SetDirty(pool);
            Debug.Log("[MoriGame] ZombiePool 类型名字体已注入");
        }

        /// <summary>教学关工作台同步悬浮锚点化：开 Tutorial → 锚点化 → 存盘 → 回主场景（迁移收尾调用）</summary>
        private static void ApplyWorkbenchAnchorsToTutorial()
        {
            const string tutorialPath = "Assets/_MoriGame/Scenes/Tutorial.unity";
            if (!System.IO.File.Exists(tutorialPath))
            {
                Debug.LogWarning("[MoriGame] 教学关锚点化跳过：Tutorial.unity 不存在");
                return;
            }
            var tutorial = EditorSceneManager.OpenScene(tutorialPath, OpenSceneMode.Single);
            ApplyWorkbenchAnchors();
            EditorSceneManager.SaveScene(tutorial);
            EditorSceneManager.OpenScene("Assets/_MoriGame/Scenes/MoriMain.unity", OpenSceneMode.Single);
            Debug.Log("[MoriGame] 教学关工作台已同步悬浮锚点化");
        }

        private static void AttachRing(Transform host, Material gold, float radius, float yOff)
        {
            var existing = host.Find("AnchorRing");
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
            var ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "AnchorRing";
            ring.transform.SetParent(host, false);
            ring.transform.localPosition = new Vector3(0f, yOff, 0f);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            ring.GetComponent<MeshRenderer>().sharedMaterial = gold;
            UnityEngine.Object.DestroyImmediate(ring.GetComponent<Collider>());
        }

        /// <summary>实体台面恢复（2026-11-12 备档未调用——回退悬浮锚点制时改回迁移调用）：按 Tutorial 场景蓝本逐参数重建弧板 9 段 + 腿 3 条 +
        /// 枪托板 + 展示位底板；枪锚点/展示位回原位，清微光圈与浮动组件；
        /// 已含台面的场景（Tutorial 等）幂等跳过。回退前 MoriMain 的台面件已被锚点化迁移删除。</summary>
        public static void RestoreSolidWorkbench()
        {
            var workbench = GameObject.Find("Workbench");
            if (workbench == null)
            {
                Debug.LogWarning("[MoriGame] 迁移跳过：场景里没有 Workbench");
                return;
            }
            var root = workbench.transform;

            const float R = 0.65f;        // WorkbenchRadiusMeters
            const float surfaceY = 0.73f; // WorkbenchSurfaceY
            var pistolDark = LoadArtMaterial("PistolDark");
            var standGrey = LoadArtMaterial("StandGrey");

            // ① 清锚点制遗留：微光圈删除、浮动组件按类型名摘除（不依赖已删除的 AnchorFloat 类）
            var ringsRemoved = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "AnchorRing")
                {
                    UnityEngine.Object.DestroyImmediate(t.gameObject);
                    ringsRemoved++;
                }
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                foreach (var c in t.gameObject.GetComponents<Component>())
                {
                    if (c != null && c.GetType().Name == "AnchorFloat")
                    {
                        UnityEngine.Object.DestroyImmediate(c);
                    }
                }
            }

            // ② 弧板 9 段：总是删旧重建（纯偏航，纠正历史错误旋转；蓝本段长 0.29025438）
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                if (root.GetChild(i).name.StartsWith("ArcTop_"))
                {
                    UnityEngine.Object.DestroyImmediate(root.GetChild(i).gameObject);
                }
            }
            const int segmentCount = 9;
            var angleStep = 180f / (segmentCount - 1);
            for (var i = 0; i < segmentCount; i++)
            {
                var a = (i - (segmentCount - 1) / 2f) * angleStep;
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = "ArcTop_" + i;
                plate.transform.SetParent(root, false);
                plate.transform.localPosition = new Vector3(
                    Mathf.Sin(a * Mathf.Deg2Rad) * R, surfaceY - 0.03f,
                    Mathf.Cos(a * Mathf.Deg2Rad) * R);
                plate.transform.localRotation = Quaternion.Euler(0f, a, 0f);
                plate.transform.localScale = new Vector3(0.29025438f, 0.06f, 0.26f);
                plate.GetComponent<MeshRenderer>().sharedMaterial = pistolDark;
                UnityEngine.Object.DestroyImmediate(plate.GetComponent<Collider>());
            }

            // ③ 腿 3 条 @ ±80°/0°（已存在则跳过）
            if (root.Find("WorkbenchLeg") == null)
            {
                foreach (var deg in new[] { -80f, 0f, 80f })
                {
                    var leg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    leg.name = "WorkbenchLeg";
                    leg.transform.SetParent(root, false);
                    leg.transform.localPosition = new Vector3(
                        Mathf.Sin(deg * Mathf.Deg2Rad) * R, 0.35f, Mathf.Cos(deg * Mathf.Deg2Rad) * R);
                    leg.transform.localScale = new Vector3(0.05f, 0.7f, 0.05f);
                    leg.GetComponent<MeshRenderer>().sharedMaterial = pistolDark;
                    UnityEngine.Object.DestroyImmediate(leg.GetComponent<Collider>());
                }
            }

            // ④ 枪托板（已存在则跳过）+ 枪锚点回位（托板正上方 0.125 米）
            if (root.Find("PistolSlotMarker") == null)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "PistolSlotMarker";
                marker.transform.SetParent(root, false);
                marker.transform.localPosition = new Vector3(
                    Mathf.Sin(45f * Mathf.Deg2Rad) * R, surfaceY + 0.015f,
                    Mathf.Cos(45f * Mathf.Deg2Rad) * R);
                marker.transform.localScale = new Vector3(0.18f, 0.03f, 0.24f);
                marker.GetComponent<MeshRenderer>().sharedMaterial = standGrey;
                UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            }

            var slotAnchor = root.Find("PistolSlotAnchor");
            if (slotAnchor != null)
            {
                slotAnchor.localPosition = new Vector3(
                    Mathf.Sin(45f * Mathf.Deg2Rad) * R, surfaceY + 0.14f,
                    Mathf.Cos(45f * Mathf.Deg2Rad) * R);
            }

            // ⑤ 展示位回位 + 底板重建 + 模型回位
            RestoreStand("Stand_AutoTurret", new Vector3(-0.51f, surfaceY, 0.4f), pistolDark);
            RestoreStand("Stand_ElectricFence", new Vector3(-0.205f, surfaceY, 0.615f), pistolDark);

            EditorUtility.SetDirty(workbench);
            Debug.Log($"[MoriGame] 实体台面恢复完成：弧板 9 + 腿 3 + 托板 1 + 底板 2；清微光圈 {ringsRemoved}");
        }

        /// <summary>展示位回实体形态：回蓝本坐标，补 BasePlate，模型回 y=0.03（与场景原版一致）</summary>
        private static void RestoreStand(string standName, Vector3 localPos, Material plateMat)
        {
            var stand = GameObject.Find(standName);
            if (stand == null)
            {
                Debug.LogWarning("[MoriGame] 实体台面恢复：找不到展示位 " + standName);
                return;
            }
            var t = stand.transform;
            t.localPosition = localPos;
            if (t.Find("BasePlate") == null)
            {
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = "BasePlate";
                plate.transform.SetParent(t, false);
                plate.transform.localPosition = new Vector3(0f, 0.015f, 0f);
                plate.transform.localScale = new Vector3(0.22f, 0.03f, 0.22f);
                plate.GetComponent<MeshRenderer>().sharedMaterial = plateMat;
                UnityEngine.Object.DestroyImmediate(plate.GetComponent<Collider>());
            }
            var model = t.Find("Model");
            if (model != null)
            {
                model.localPosition = new Vector3(0f, 0.03f, 0f);
            }
        }

        /// <summary>建筑落地视觉预制体注入（2026-11-12 正式模型）：BuildManager 序列化引用 + 机枪塔预制体补 Muzzle 枪口节点</summary>
        private static void InjectPropVisualPrefabs()
        {
            var manager = UnityEngine.Object.FindFirstObjectByType<Build.BuildManager>();
            if (manager == null)
            {
                Debug.LogWarning("[MoriGame] 迁移跳过：场景里没有 BuildManager");
                return;
            }
            var turret = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/资产/NEW/预制体/Prop_机枪塔.prefab");
            var fence = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/资产/NEW/预制体/Prop_电网.prefab");
            manager.turretVisualPrefab = turret;
            manager.fenceVisualPrefab = fence;
            EditorUtility.SetDirty(manager);
            EnsureTurretMuzzleNode(turret);
            Debug.Log($"[MoriGame] 建筑视觉预制体注入完成：机枪塔={turret != null} 电网={fence != null}");
        }

        /// <summary>机枪塔预制体补 Muzzle 枪口空节点（默认包围盒前上方，用户在预制体面板拖到真实枪口；已存在则不动）</summary>
        private static void EnsureTurretMuzzleNode(GameObject prefab)
        {
            if (prefab == null)
            {
                return;
            }
            var path = UnityEditor.AssetDatabase.GetAssetPath(prefab);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.transform.Find("Muzzle") != null)
                {
                    return;
                }
                var bounds = new Bounds(root.transform.position, Vector3.zero);
                var renderers = root.GetComponentsInChildren<MeshRenderer>();
                if (renderers.Length > 0)
                {
                    bounds = renderers[0].bounds;
                    for (var i = 1; i < renderers.Length; i++)
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }
                }
                var muzzle = new GameObject("Muzzle");
                // 转局部坐标：默认包围盒前上方（前向 +Z，模型前向不符时用户拖 Muzzle 即可）
                var local = root.transform.InverseTransformPoint(bounds.center);
                muzzle.transform.localPosition = new Vector3(
                    local.x, local.y + bounds.extents.y * 0.5f, local.z + bounds.extents.z);
                muzzle.transform.SetParent(root.transform, false);
                EditorUtility.SetDirty(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log("[MoriGame] 机枪塔预制体已补 Muzzle 枪口节点（面板可拖动调位）");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>按资产名加载灰盒材质（EnsureMat 建立于 Assets/_MoriGame/Art/Materials/）</summary>
        private static Material LoadArtMaterial(string name)
        {
            var path = "Assets/_MoriGame/Art/Materials/" + name + ".mat";
            var mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Debug.LogWarning("[MoriGame] 实体台面恢复：找不到材质 " + path);
            }
            return mat;
        }

        /// <summary>僵尸模型指派权威表（2026-11-10 用户定版）：普通=男女目录全量随机（排除五款朋克=34 款，
        /// 新模型丢进目录自动纳入）；疾行=Boss Wretch ×0.8、喷吐=五款朋克随机 ×0.8、盾卫=Boss Brute ×1.0、
        /// 蛮兽=Boss Slobber ×1.0、督战=Boss Blobber ×1.5。控制器统一复用 ZombieAnimator（Humanoid 重定向），
        /// avatar 取各 prefab 自带；逐类型按资产路径去重+清不在指派清单内的死引用，缩放每次强制对齐。</summary>
        private static void AddNormalModelVariants()
        {
            var pool = UnityEngine.Object.FindFirstObjectByType<ZombiePool>();
            if (pool == null)
            {
                Debug.LogWarning("[MoriGame] 迁移跳过：场景里没有 ZombiePool");
                return;
            }
            RuntimeAnimatorController normalController = null;
            foreach (var set in pool.modelSets)
            {
                if (set != null && set.zombieId == "Normal" && set.controller != null)
                {
                    normalController = set.controller;
                    break;
                }
            }
            if (normalController == null)
            {
                Debug.LogWarning("[MoriGame] 迁移跳过：找不到基础 Normal 模型集（含控制器）");
                return;
            }
            var changed = false;

            // ① 其余五类模型集移除置空（用户 2026-11-10：另有安排）；池无该类模型时刷怪器按 HasModel 跳过不占预算
            for (var i = pool.modelSets.Count - 1; i >= 0; i--)
            {
                var set = pool.modelSets[i];
                if (set != null && set.zombieId != "Normal")
                {
                    Debug.Log("[MoriGame] 移除非普通模型集（置空）：" + set.zombieId);
                    pool.modelSets.RemoveAt(i);
                    changed = true;
                }
            }

            // ② 目录全量枚举：男/女两个 prefab 目录的现有与未来模型全量纳入；
            // 排除表（2026-11-10 用户：五款朋克模型另有用处，不进普通随机池，池=34 款）
            var excluded = new HashSet<string>
            {
                "TT_zombie_M_15_punk.prefab",
                "TT_zombie_M_16_punk.prefab",
                "TT_zombie_M_17_punk.prefab",
                "TT_zombie_F_15_punk.prefab",
                "TT_zombie_F_16_punk.prefab"
            };
            var desired = new List<string>();
            foreach (var dir in new[]
                     {
                         "Assets/ToonyTinyPeople/TT_Zombies/prefabs/zombie_male",
                         "Assets/ToonyTinyPeople/TT_Zombies/prefabs/zombie_female"
                     })
            {
                foreach (var file in Directory.GetFiles(dir, "*.prefab"))
                {
                    var name = Path.GetFileName(file);
                    if (!excluded.Contains(name))
                    {
                        desired.Add(file.Replace('\\', '/'));
                    }
                }
            }
            desired.Sort();

            // ③ 去陈旧：不在目录清单内的 Normal 条目移除（防目录改名/删除后残留死引用）
            for (var i = pool.modelSets.Count - 1; i >= 0; i--)
            {
                var set = pool.modelSets[i];
                if (set == null || set.zombieId != "Normal")
                {
                    continue;
                }
                var path = set.prefab != null ? AssetDatabase.GetAssetPath(set.prefab) : null;
                if (path == null || !desired.Contains(path))
                {
                    Debug.Log("[MoriGame] 移除失效普通模型集：" + (path ?? "空引用"));
                    pool.modelSets.RemoveAt(i);
                    changed = true;
                }
            }

            // ④ 补缺失：控制器复用基础条目，avatar 取各 prefab 自带，体型权威 0.6（×0.6 二次调压版）
            var added = 0;
            foreach (var path in desired)
            {
                var exists = false;
                foreach (var set in pool.modelSets)
                {
                    if (set != null && set.zombieId == "Normal" && set.prefab != null &&
                        AssetDatabase.GetAssetPath(set.prefab) == path)
                    {
                        exists = true;
                        break;
                    }
                }
                if (exists)
                {
                    continue;
                }
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning("[MoriGame] 变体预制体缺失：" + path);
                    continue;
                }
                var animator = prefab.GetComponentInChildren<Animator>(true);
                pool.modelSets.Add(new ZombiePool.ModelSet
                {
                    zombieId = "Normal",
                    prefab = prefab,
                    avatar = animator != null ? animator.avatar : null,
                    controller = normalController,
                    visualScale = Vector3.one * 0.6f
                });
                added++;
                changed = true;
            }
            if (added > 0)
            {
                var total = 0;
                foreach (var set in pool.modelSets)
                {
                    if (set != null && set.zombieId == "Normal")
                    {
                        total++;
                    }
                }
                Debug.Log($"[MoriGame] 普通感染者模型池补齐：+{added}，全随机总数 {total}");
            }

            // ⑤ 特殊五类模型指派（2026-11-10 用户定版，缩放按用户指定值强制对齐）：
            // 疾行=Boss/Wretch ×0.8；喷吐=五款朋克随机 ×0.8；盾卫=Boss/Brute ×1.0；蛮兽=Boss/Slobber ×1.0；督战=Boss/Blobber ×1.5
            var specialRoster = new Dictionary<string, string[]>
            {
                { "Runner", new[] { "Assets/ToonyTinyPeople/Boss/Prefabs/SM_Chr_ZombieBoss_Wretch_01.prefab" } },
                {
                    "Spitter", new[]
                    {
                        "Assets/ToonyTinyPeople/TT_Zombies/prefabs/zombie_male/TT_zombie_M_15_punk.prefab",
                        "Assets/ToonyTinyPeople/TT_Zombies/prefabs/zombie_male/TT_zombie_M_16_punk.prefab",
                        "Assets/ToonyTinyPeople/TT_Zombies/prefabs/zombie_male/TT_zombie_M_17_punk.prefab",
                        "Assets/ToonyTinyPeople/TT_Zombies/prefabs/zombie_female/TT_zombie_F_15_punk.prefab",
                        "Assets/ToonyTinyPeople/TT_Zombies/prefabs/zombie_female/TT_zombie_F_16_punk.prefab"
                    }
                },
                { "ShieldGuard", new[] { "Assets/ToonyTinyPeople/Boss/Prefabs/SM_Chr_ZombieBoss_Brute_01.prefab" } },
                { "Brute", new[] { "Assets/ToonyTinyPeople/Boss/Prefabs/SM_Chr_ZombieBoss_Slobber_01.prefab" } },
                { "Overseer", new[] { "Assets/ToonyTinyPeople/Boss/Prefabs/SM_Chr_ZombieBoss_Blobber_01.prefab" } }
            };
            var specialScales = new Dictionary<string, Vector3>
            {
                { "Runner", Vector3.one * 0.8f },
                { "Spitter", Vector3.one * 0.8f },
                { "ShieldGuard", Vector3.one },
                { "Brute", Vector3.one },
                { "Overseer", Vector3.one * 1.5f }
            };
            foreach (var kv in specialRoster)
            {
                for (var i = pool.modelSets.Count - 1; i >= 0; i--)
                {
                    var set = pool.modelSets[i];
                    if (set == null || set.zombieId != kv.Key)
                    {
                        continue;
                    }
                    var path = set.prefab != null ? AssetDatabase.GetAssetPath(set.prefab) : null;
                    if (path == null || Array.IndexOf(kv.Value, path) < 0)
                    {
                        Debug.Log($"[MoriGame] 移除失效 {kv.Key} 模型集：{(path ?? "空引用")}");
                        pool.modelSets.RemoveAt(i);
                        changed = true;
                    }
                }
                foreach (var path in kv.Value)
                {
                    ZombiePool.ModelSet match = null;
                    foreach (var set in pool.modelSets)
                    {
                        if (set != null && set.zombieId == kv.Key && set.prefab != null &&
                            AssetDatabase.GetAssetPath(set.prefab) == path)
                        {
                            match = set;
                            break;
                        }
                    }
                    if (match == null)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab == null)
                        {
                            Debug.LogWarning("[MoriGame] 特殊类型预制体缺失：" + path);
                            continue;
                        }
                        var animator = prefab.GetComponentInChildren<Animator>(true);
                        match = new ZombiePool.ModelSet
                        {
                            zombieId = kv.Key,
                            prefab = prefab,
                            avatar = animator != null ? animator.avatar : null,
                            controller = normalController
                        };
                        pool.modelSets.Add(match);
                        changed = true;
                        Debug.Log($"[MoriGame] {kv.Key} 模型指派：{Path.GetFileName(path)}");
                    }
                    if (match.visualScale != specialScales[kv.Key])
                    {
                        match.visualScale = specialScales[kv.Key];
                        changed = true;
                    }
                }
            }

            if (changed)
            {
                EditorUtility.SetDirty(pool);
            }
        }

        private static Transform ReanchorUnder(Transform anchor, string name, Vector3 localPos, Quaternion localRot)
        {
            var go = GameObject.Find(name);
            if (go == null)
            {
                Debug.LogWarning("[MoriGame] 迁移跳过缺失对象：" + name);
                return null;
            }
            return ReanchorUnder(anchor, go, localPos, localRot);
        }

        private static Transform ReanchorUnder(Transform anchor, GameObject go, Vector3 localPos, Quaternion localRot)
        {
            var t = go.transform;
            if (t.parent != anchor)
            {
                t.SetParent(anchor, true);
            }
            t.localPosition = localPos;
            t.localRotation = localRot;
            EditorUtility.SetDirty(go);
            return t;
        }

        /// <summary>v1 遗留物清理（幂等）：隐形地板与 NavMeshSurface 实验对象</summary>
        private static void RemoveLegacyHelpers()
        {
            foreach (var name in new[] { "ZombieFloor", "MapNavSurface" })
            {
                var go = GameObject.Find(name);
                if (go != null)
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            const string navDir = "Assets/_MoriGame/Art/NavMesh";
            foreach (var name in new[] { "ZombieFloorNavMesh", "MapNavMesh" })
            {
                var path = navDir + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<NavMeshData>(path) != null)
                {
                    AssetDatabase.DeleteAsset(path);
                }
            }
        }

        /// <summary>给场景交互体打导航排除标记（不被烘焙成可行走面）</summary>
        private static void ExcludeFromNav(string objectName)
        {
            var go = GameObject.Find(objectName);
            if (go == null)
            {
                return;
            }
            var modifier = go.GetComponent<NavMeshModifier>();
            if (modifier == null)
            {
                modifier = go.AddComponent<NavMeshModifier>();
            }
            var so = new SerializedObject(modifier);
            var ignore = so.FindProperty("m_IgnoreOverride");
            if (ignore != null)
            {
                ignore.boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(go);
        }

        /// <summary>遗留全场景导航烘焙：瓦片（NavigationStatic）+地图巨型地面网格进烘焙；
        /// 工作台/高台/补给箱 NavMeshModifier 排除；agentClimb 0.6 放行普通台阶</summary>
        private static void BakeMapNavmesh(Transform anchor, float groundY)
        {
            ExcludeFromNav("Workbench");
            ExcludeFromNav("CoreDevice");
            ExcludeFromNav("SupplyCrate");

            var settings = new SerializedObject(UnityEditor.AI.NavMeshBuilder.navMeshSettingsObject);
            var bakeRadius = settings.FindProperty("m_BuildSettings.agentRadius");
            var bakeClimb = settings.FindProperty("m_BuildSettings.agentClimb");
            if (bakeRadius == null || bakeClimb == null)
            {
                throw new InvalidOperationException("找不到 NavMesh 场景烘焙设置");
            }
            bakeRadius.floatValue = 0.14f; // 审计断言读它
            bakeClimb.floatValue = 0.6f;   // 普通台阶（沙 0.25 / 土 0.25 / 水 0.5）不阻拦僵尸
            settings.ApplyModifiedPropertiesWithoutUndo();
            UnityEditor.AI.NavMeshBuilder.ClearAllNavMeshes();
            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();

            // 自检：锚点+刷怪环四点采样——命中实测地面高度±0.5 且**从塔基寻路可达**（SamplePosition
            // 不查连通性，用户地图的悬浮装饰面会当近邻命中，僵尸会刷在孤岛上）；5 点过 4 即通过（容一处地图阻挡）
            var diagnostics = "";
            var connectedCount = 0;
            var offsets = new[]
            {
                Vector3.zero,
                new Vector3(0f, 0f, 12.5f), new Vector3(0f, 0f, -12.5f),
                new Vector3(12.5f, 0f, 0f), new Vector3(-12.5f, 0f, 0f)
            };
            // 塔基中心被自身 NavMeshObstacle 雕空，寻路起点取塔外圈最近导航点
            var towerBase = NavMesh.SamplePosition(
                new Vector3(anchor.position.x + TowerProbeOffset.x, groundY, anchor.position.z),
                out var baseHit, 2f, NavMesh.AllAreas)
                ? baseHit.position
                : new Vector3(anchor.position.x + TowerProbeOffset.x, groundY, anchor.position.z);
            foreach (var offset in offsets)
            {
                var probe = anchor.position + offset + Vector3.up * 0.5f;
                var hitOk = NavMesh.SamplePosition(probe, out var navHit, 2f, NavMesh.AllAreas) &&
                            Mathf.Abs(navHit.position.y - groundY) < 0.5f;
                var connected = false;
                if (hitOk)
                {
                    var path = new NavMeshPath();
                    connected = NavMesh.CalculatePath(towerBase, navHit.position, NavMesh.AllAreas, path) &&
                                path.status == NavMeshPathStatus.PathComplete;
                }
                if (hitOk && connected)
                {
                    connectedCount++;
                }
                diagnostics += " " + offset.ToString("F1") + "→" +
                               (hitOk ? navHit.position.y.ToString("F3") : "miss") +
                               (connected ? "" : "(不通)");
            }
            var sampleAllOk = connectedCount >= offsets.Length - 1;
            if (!sampleAllOk)
            {
                throw new InvalidOperationException("迁移导航自检未通过（刷怪环未落到视觉地面高度）：" + diagnostics);
            }
            Debug.Log("[MoriGame] 迁移导航自检通过" + diagnostics);
        }

        private static Material EnsureTowerMaterial()
        {
            const string path = "Assets/_MoriGame/Art/Materials/TowerGrey.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.color = new Color(0.42f, 0.44f, 0.47f, 1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
