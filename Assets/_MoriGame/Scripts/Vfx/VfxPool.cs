using System.Collections.Generic;
using UnityEngine;

namespace MoriGame.Vfx
{
    /// <summary>特效池服务（2026-11-11，模式同 ZombiePool/ProjectilePool）：
    /// 一次性 = 取池实例→置位→Play 全部粒子→按 main.duration+startLifetime 自动回池；
    /// 循环 = 挂宿主随启停，句柄 Stop() 释放。全程池化不反复 Instantiate/Destroy；
    /// 条目 maxConcurrent 超限丢弃最旧（爆体 6 / 溅血 10）。挂 MatchBootstrap，经 MatchBootstrap.Vfx 访问。</summary>
    public class VfxPool : MonoBehaviour
    {
        private VfxLibrary library;
        private readonly Dictionary<GameObject, Queue<GameObject>> idle = new Dictionary<GameObject, Queue<GameObject>>();
        private readonly List<Active> active = new List<Active>();
        private int slotSeq;

        private struct Active
        {
            public GameObject Root;
            public GameObject Prefab;
            public VfxEntry Entry;
            public float RecycleAt; // 循环特效=float.MaxValue（仅句柄 Stop 释放）
            public int SlotId;
            public Transform Follow; // 世界跟随宿主（挂池根不继承宿主缩放，每帧同步位置）
        }

        public void Init(VfxLibrary lib)
        {
            library = lib;
        }

        /// <summary>一次性特效（rotation 传 default 即恒等）</summary>
        public void Play(VfxEntry entry, Vector3 position, Quaternion rotation = default)
        {
            if (library == null || entry == null || entry.prefab == null)
            {
                return;
            }
            if (entry.maxConcurrent > 0)
            {
                DropOldest(entry, entry.maxConcurrent - 1); // 给新特效腾位：丢同条目最旧
            }
            var instance = Take(entry, position, rotation);
            PlaySystems(instance);
            active.Add(new Active
            {
                Root = instance,
                Prefab = entry.prefab,
                Entry = entry,
                RecycleAt = Time.time + TotalDuration(instance),
                SlotId = slotSeq++
            });
        }

        /// <summary>循环特效挂宿主（worldPositionStays 保世界缩放，塔 0.25 等父级缩放不复合）；句柄 Stop 释放</summary>
        public LoopHandle PlayLoop(VfxEntry entry, Transform parent, Vector3 localPos)
        {
            if (library == null || entry == null || entry.prefab == null || parent == null)
            {
                return default;
            }
            var instance = Take(entry, parent.TransformPoint(localPos), Quaternion.identity);
            instance.transform.SetParent(parent, true);
            instance.transform.localPosition = localPos;
            PlaySystems(instance);
            var slot = slotSeq++;
            active.Add(new Active { Root = instance, Prefab = entry.prefab, Entry = entry, RecycleAt = float.MaxValue, SlotId = slot });
            return new LoopHandle(this, slot);
        }

        /// <summary>世界跟随循环（2026-11-11 修复弹丸拖尾）：挂 VfxPool 根节点每帧同步宿主位姿——
        /// 不挂小尺度父级（弹丸 0.06/酸液 0.14 会把特效缩没）；宿主销毁/失活自动回池。</summary>
        public LoopHandle PlayFollow(VfxEntry entry, Transform follow)
        {
            if (library == null || entry == null || entry.prefab == null || follow == null)
            {
                return default;
            }
            var instance = Take(entry, follow.position, follow.rotation);
            PlaySystems(instance);
            var slot = slotSeq++;
            active.Add(new Active { Root = instance, Prefab = entry.prefab, Entry = entry, RecycleAt = float.MaxValue, SlotId = slot, Follow = follow });
            return new LoopHandle(this, slot);
        }

        public class LoopHandle
        {
            private VfxPool owner;
            private readonly int slot;

            internal LoopHandle(VfxPool pool, int slotId)
            {
                owner = pool;
                slot = slotId;
            }

            public void Stop()
            {
                if (owner != null)
                {
                    owner.EndLoop(slot);
                    owner = null;
                }
            }
        }

        private void EndLoop(int slot)
        {
            for (var i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].SlotId != slot)
                {
                    continue;
                }
                Release(active[i].Root, active[i].Prefab);
                active.RemoveAt(i);
                return;
            }
        }

        private void Update()
        {
            for (var i = active.Count - 1; i >= 0; i--)
            {
                var item = active[i];
                if (item.Follow != null) // 世界跟随：同步位姿；宿主销毁/失活即回收（根治回收时序）
                {
                    if (item.Follow.gameObject == null || !item.Follow.gameObject.activeInHierarchy)
                    {
                        Release(item.Root, item.Prefab);
                        active.RemoveAt(i);
                        continue;
                    }
                    item.Root.transform.SetPositionAndRotation(item.Follow.position, item.Follow.rotation);
                }
                if (Time.time < item.RecycleAt)
                {
                    continue;
                }
                Release(item.Root, item.Prefab);
                active.RemoveAt(i);
            }
        }

        private void Release(GameObject root, GameObject prefab)
        {
            if (root == null)
            {
                return;
            }
            root.transform.SetParent(transform, false);
            root.SetActive(false);
            if (!idle.TryGetValue(prefab, out var queue))
            {
                queue = new Queue<GameObject>();
                idle[prefab] = queue;
            }
            queue.Enqueue(root);
        }

        private GameObject Take(VfxEntry entry, Vector3 position, Quaternion rotation)
        {
            GameObject instance = null;
            if (idle.TryGetValue(entry.prefab, out var queue) && queue.Count > 0)
            {
                instance = queue.Dequeue();
            }
            if (instance == null)
            {
                instance = Instantiate(entry.prefab, transform);
                if (library != null && library.compressOnSpawn)
                {
                    CompressBudget(instance); // 按库开关压缩（2026-11-11 用户自调参：默认关，首建一次不重复）
                }
            }
            instance.transform.SetParent(null);
            instance.transform.SetPositionAndRotation(position, rotation == default ? Quaternion.identity : rotation);
            instance.transform.localScale = Vector3.one * entry.scale;
            instance.SetActive(true);
            return instance;
        }

        private static void PlaySystems(GameObject root)
        {
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Simulate(0f, true, true); // 池化复用必须重置到起点再播
                ps.Play(true);
            }
        }

        /// <summary>实例化统一压缩粒子预算（2026-11-11 性能）：maxParticles 减半、装饰性子粒子（spark/smoke/glow）禁用、
        /// 粒子自带实时 Light 禁用（移动端帧率杀手）</summary>
        private static void CompressBudget(GameObject instance)
        {
            foreach (var light in instance.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
            }
            foreach (var ps in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.maxParticles = Mathf.Max(4, main.maxParticles / 2);
                if (ps.transform != instance.transform)
                {
                    var n = ps.gameObject.name.ToLowerInvariant();
                    if (n.Contains("spark") || n.Contains("smoke") || n.Contains("glow"))
                    {
                        ps.gameObject.SetActive(false);
                    }
                }
            }
        }

        private static float TotalDuration(GameObject root)
        {
            var max = 0f;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                max = Mathf.Max(max, main.duration + main.startLifetime.constantMax);
            }
            return max + 0.1f;
        }

        private void DropOldest(VfxEntry entry, int keep)
        {
            var kept = 0;
            for (var i = active.Count - 1; i >= 0; i--) // 从最新往回：保留最新 keep 个
            {
                if (active[i].Entry != entry)
                {
                    continue;
                }
                kept++;
                if (kept > keep)
                {
                    Release(active[i].Root, active[i].Prefab);
                    active.RemoveAt(i);
                }
            }
        }
    }
}

namespace MoriGame.Vfx
{
    /// <summary>特效总线门面（2026-11-11，风格同 SfxBus）：调用点一行直达；bootstrap/库缺席静默（编辑器测试安全）；
    /// 总开关 = VfxLibrary.vfxEnabled（Inspector 勾选，2026-11-11 移到资产上用户自管）。</summary>
    public static class VfxBus
    {
        private static VfxPool P => MatchBootstrap.Instance != null ? MatchBootstrap.Instance.Vfx : null;

        private static VfxLibrary L
        {
            get
            {
                var lib = MatchBootstrap.Instance != null ? MatchBootstrap.Instance.vfxLibrary : null;
                return lib != null && lib.vfxEnabled ? lib : null; // 总开关：资产级勾选
            }
        }

        public static void Play(System.Func<VfxLibrary, VfxEntry> pick, Vector3 position, Quaternion rotation = default)
        {
            var lib = L;
            var entry = lib != null ? pick(lib) : null;
            P?.Play(entry, position, rotation);
        }

        public static VfxPool.LoopHandle Loop(System.Func<VfxLibrary, VfxEntry> pick, Transform parent, Vector3 localPos)
        {
            var lib = L;
            var entry = lib != null ? pick(lib) : null;
            return P != null ? P.PlayLoop(entry, parent, localPos) : default;
        }

        /// <summary>世界跟随循环：挂池根随宿主每帧同步（小尺度宿主安全），宿主销毁/失活自动回收</summary>
        public static void Follow(System.Func<VfxLibrary, VfxEntry> pick, Transform follow)
        {
            var lib = L;
            var entry = lib != null ? pick(lib) : null;
            P?.PlayFollow(entry, follow);
        }
    }
}
