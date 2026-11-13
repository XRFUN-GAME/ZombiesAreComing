using System;
using System.Collections;
using System.Collections.Generic;
using MoriGame.Configs;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

namespace MoriGame.Zombies
{
    /// <summary>按类型复用僵尸实体；正式模型、Avatar 与 AnimatorController 均由场景注入。</summary>
    public class ZombiePool : MonoBehaviour
    {
        public const int GlobalCap = 60;

        [Serializable]
        public class ModelSet
        {
            public string zombieId;
            public GameObject prefab;
            public Avatar avatar;
            public RuntimeAnimatorController controller;
            public Vector3 visualScale = Vector3.one;
            public string bodyMeshOverride;
            public float modelYCompensation;
        }

        public ZombieConfig normalConfig;
        public List<ZombieConfig> configs = new List<ZombieConfig>();
        public List<ModelSet> modelSets = new List<ModelSet>();
        public Material acidMaterial;
        public TMP_FontAsset nameLabelFont; // 僵尸血条上方类型名（迁移注入 MoriZhCN_SDF）

        public static ZombiePool Instance { get; private set; }

        private readonly List<ZombieAgent> inactive = new List<ZombieAgent>();
        private readonly List<ZombieAgent> active = new List<ZombieAgent>();

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public int ActiveCount => active.Count;
        public IReadOnlyList<ZombieAgent> ActiveZombies => active;

        public ZombieAgent SpawnNormal(Vector3 position)
        {
            return Spawn("Normal", position);
        }

        /// <summary>预生成池（2026-11-09 用户实测：出僵尸瞬间头盔卡顿）——运行期 Build 会当场
        /// Instantiate 蒙皮预制体（几十毫秒级），是卡顿根因。准备期分帧把各类型先造好塞进
        /// 池空闲列表（SetActive(false) 不渲染不更新），战斗期 Spawn 直接复用零实例化。</summary>
        public IEnumerator PreloadRoutine(IReadOnlyList<string> zombieIds, int perType, int buildsPerFrame = 2)
        {
            var pending = new List<string>();
            foreach (var id in zombieIds)
            {
                for (var i = 0; i < perType; i++)
                {
                    pending.Add(id);
                }
            }
            var builtSinceYield = 0;
            foreach (var id in pending)
            {
                var config = ConfigFor(id);
                if (config == null)
                {
                    continue;
                }
                var zombie = Build(config);
                if (zombie == null)
                {
                    continue;
                }
                Return(zombie); // 直接进空闲池（SetActive(false)）
                if (++builtSinceYield >= buildsPerFrame)
                {
                    builtSinceYield = 0;
                    yield return null;
                }
            }
            Debug.Log($"[MoriGame] 僵尸池预生成完成：空闲 {inactive.Count}（{string.Join("/", zombieIds)}×{perType}）");
        }

        public ZombieAgent Spawn(string zombieId, Vector3 position)
        {
            var config = ConfigFor(zombieId);
            if (config == null || active.Count >= GlobalCap)
            {
                return null;
            }
            var zombie = TakeInactive(zombieId);
            if (zombie == null)
            {
                var buildSw = System.Diagnostics.Stopwatch.StartNew(); // 现场实例化耗时探针（池空兜底路径）
                zombie = Build(config);
                buildSw.Stop();
                if (zombie == null)
                {
                    return null;
                }
                if (buildSw.Elapsed.TotalMilliseconds > 5f)
                {
                    Debug.Log($"[MoriGame][Perf] 现场实例化 {zombieId} 耗时 {buildSw.Elapsed.TotalMilliseconds:0.0}ms（池已空）");
                }
            }
            position.y = 0f;
            // 吸附导航网格（2026-11-06 站桩重构：玩法地面在锚点高度，不再恒 y=0）
            if (NavMesh.SamplePosition(position, out var hit, 3f, NavMesh.AllAreas))
            {
                position = hit.position;
            }
            zombie.transform.position = position;
            zombie.gameObject.SetActive(true);
            zombie.Initialize(config, this);
            active.Add(zombie);
            return zombie;
        }

        public ZombieConfig ConfigFor(string zombieId)
        {
            for (var i = 0; i < configs.Count; i++)
            {
                if (configs[i] != null && configs[i].zombieId == zombieId)
                {
                    return configs[i];
                }
            }
            return normalConfig != null && normalConfig.zombieId == zombieId ? normalConfig : null;
        }

        public void Return(ZombieAgent zombie)
        {
            if (zombie == null || !active.Remove(zombie))
            {
                return;
            }
            zombie.gameObject.SetActive(false);
            inactive.Add(zombie);
        }

        public void ClearAll()
        {
            for (var i = active.Count - 1; i >= 0; i--)
            {
                Return(active[i]);
            }
        }

        public static Vector3 RandomEdgePosition()
        {
            const float half = 12.5f;
            var origin = MoriGame.Territory.TerritoryGrid.WorldOrigin; // 锚点（RealSpace 原点）相对刷怪圈
            var edge = UnityEngine.Random.Range(0, 4);
            var along = UnityEngine.Random.Range(-half, half);
            switch (edge)
            {
                case 0: return new Vector3(origin.x + along, 0f, origin.y + half);
                case 1: return new Vector3(origin.x + along, 0f, origin.y - half);
                case 2: return new Vector3(origin.x + half, 0f, origin.y + along);
                default: return new Vector3(origin.x - half, 0f, origin.y + along);
            }
        }

        /// <summary>刷怪点（2026-11-09 用户地图实测）：边缘随机 + 导航采样 + **从高台寻路连通**三重验证——
        /// SamplePosition 不查连通性，地图悬浮/孤立面会被当近邻命中导致僵尸卡死；8 次重试，全败返回 false。
        /// 起点取高台外圈最近导航点（塔基中心被自身障碍雕空）。</summary>
        public bool TryGetSpawnPosition(out Vector3 position)
        {
            var match = MatchBootstrap.Instance;
            var from = Vector3.zero;
            if (match != null && match.coreDevice != null)
            {
                var towerCenter = match.coreDevice.transform.position;
                from = NavMesh.SamplePosition(towerCenter, out var towerHit, 2.5f, NavMesh.AllAreas)
                    ? towerHit.position
                    : towerCenter;
            }
            for (var attempt = 0; attempt < 8; attempt++)
            {
                var candidate = RandomEdgePosition();
                if (!NavMesh.SamplePosition(candidate, out var hit, 3f, NavMesh.AllAreas))
                {
                    continue;
                }
                var path = new NavMeshPath();
                if (NavMesh.CalculatePath(from, hit.position, NavMesh.AllAreas, path) &&
                    path.status == NavMeshPathStatus.PathComplete)
                {
                    position = hit.position;
                    return true;
                }
            }
            position = default;
            return false;
        }

        private ZombieAgent TakeInactive(string zombieId)
        {
            for (var i = inactive.Count - 1; i >= 0; i--)
            {
                var zombie = inactive[i];
                if (zombie != null && zombie.config != null && zombie.config.zombieId == zombieId)
                {
                    inactive.RemoveAt(i);
                    return zombie;
                }
            }
            return null;
        }

        private ZombieAgent Build(ZombieConfig config)
        {
            var model = PickModel(config.zombieId);
            if (model == null || model.prefab == null || model.controller == null)
            {
                Debug.LogError("[MoriGame] 缺少僵尸模型/Animator 配置：" + config.zombieId);
                return null;
            }
            var root = new GameObject("Zombie_" + config.zombieId);
            root.transform.SetParent(transform, false);
            var agent = root.AddComponent<NavMeshAgent>();
            var height = 1.7f * model.visualScale.y;
            var radius = 0.28f * Mathf.Max(model.visualScale.x, model.visualScale.z);
            agent.radius = radius;
            agent.height = height;
            agent.angularSpeed = 180f;
            agent.acceleration = 8f;
            // 攻击圈由 ZombieAgent 判定，导航提前停会把障碍路径余量变成额外停距。
            agent.stoppingDistance = 0f;
            agent.updateRotation = false;

            var visual = Instantiate(model.prefab, root.transform, false);
            visual.name = "Model";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = model.visualScale;
            ApplyBodyOverride(visual.transform, model.bodyMeshOverride);
            visual.transform.localPosition = new Vector3(0f,
                CalculateFootLocalY(visual.transform, root.transform, model.modelYCompensation), 0f);
            var animator = visual.GetComponent<Animator>();
            if (animator == null)
            {
                animator = visual.AddComponent<Animator>();
            }
            animator.avatar = model.avatar;
            animator.runtimeAnimatorController = model.controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var legHeight = height * 0.38f;
            var bodyTop = height * 0.78f;
            var body = root.AddComponent<CapsuleCollider>();
            body.direction = 1;
            body.radius = radius;
            body.height = bodyTop - legHeight;
            body.center = new Vector3(0f, (bodyTop + legHeight) / 2f, 0f);

            var legs = new GameObject("LegHitZone");
            legs.transform.SetParent(root.transform, false);
            var legCollider = legs.AddComponent<CapsuleCollider>();
            legCollider.direction = 1;
            legCollider.radius = radius * 0.85f;
            legCollider.height = legHeight;
            legCollider.center = new Vector3(0f, legHeight / 2f, 0f);
            legs.AddComponent<ZombieLeg>();

            var head = new GameObject("HeadHitZone");
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, height * 0.9f, 0f);
            var headCollider = head.AddComponent<SphereCollider>();
            headCollider.radius = height * 0.12f;
            head.AddComponent<ZombieWeakpoint>();

            var zombie = root.AddComponent<ZombieAgent>();
            zombie.animator = animator;
            zombie.visualRoot = visual.transform;
            zombie.nameLabelFont = nameLabelFont;
            return zombie;
        }

        /// <summary>该类型是否登记了可用模型（prefab+controller 齐全）——刷怪器选型前过滤，
        /// 模型被置空的类型不进类型表、不占波次预算（2026-11-10 用户：五类赋空另有安排）</summary>
        public bool HasModel(string zombieId)
        {
            for (var i = 0; i < modelSets.Count; i++)
            {
                var set = modelSets[i];
                if (set != null && set.zombieId == zombieId && set.prefab != null && set.controller != null)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>同 id 多模型随机挑（2026-11-09 用户要求：普通感染者男女三模型）；蓄水池抽样单遍均匀</summary>
        private ModelSet PickModel(string zombieId)
        {
            ModelSet fallback = null;
            ModelSet pick = null;
            var count = 0;
            for (var i = 0; i < modelSets.Count; i++)
            {
                var set = modelSets[i];
                if (set == null || set.zombieId != zombieId)
                {
                    continue;
                }
                if (fallback == null)
                {
                    fallback = set;
                }
                count++;
                if (UnityEngine.Random.Range(0, count) == 0)
                {
                    pick = set;
                }
            }
            return pick != null ? pick : fallback;
        }

        public static float CalculateFootLocalY(Transform visual, Transform agentRoot, float compensation)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>(false);
            var minY = float.MaxValue;
            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].enabled)
                {
                    minY = Mathf.Min(minY, renderers[i].bounds.min.y);
                }
            }
            return minY == float.MaxValue ? compensation : agentRoot.position.y - minY + compensation;
        }

        private static void ApplyBodyOverride(Transform model, string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName))
            {
                return;
            }
            Transform replacement = null;
            var transforms = model.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name == bodyName)
                {
                    replacement = transforms[i];
                    break;
                }
            }
            if (replacement == null)
            {
                Debug.LogWarning("[MoriGame] 模型没有指定体型 " + bodyName + "：" + model.name);
                return;
            }
            for (var i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].name.StartsWith("Body_", StringComparison.Ordinal))
                {
                    transforms[i].gameObject.SetActive(false);
                }
            }
            replacement.gameObject.SetActive(true);
        }
    }
}
