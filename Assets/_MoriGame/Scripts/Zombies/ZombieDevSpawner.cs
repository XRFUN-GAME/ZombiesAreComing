#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine;

namespace MoriGame.Zombies
{
    /// <summary>
    /// 开发刷怪器（仅 Editor/Development）：练习循环默认开（每 6 秒 1 只普通感染者、存活上限 8），
    /// 供 T5 射击实测；正式波次由 T7 WaveDirector 接管。键位：Z +1 · X +5 · C 清空 · G 开关循环。
    /// </summary>
    public class ZombieDevSpawner : MonoBehaviour
    {
        public float practiceIntervalSeconds = 6f;
        public int practiceMaxAlive = 8;
        public bool practiceLoop = false; // 默认关（T7 整备：正式局由 WaveDirector 接管，G 键手动开）

        private float nextSpawnRemaining;

        private void Start()
        {
            nextSpawnRemaining = 2f; // 开局 2 秒后来第一只
        }

        private void Update()
        {
            var pool = ZombiePool.Instance;
            if (pool == null)
            {
                return;
            }
            if (Input.GetKeyDown(KeyCode.Z))
            {
                Spawn(pool, 1);
            }
            if (Input.GetKeyDown(KeyCode.X))
            {
                Spawn(pool, 5);
            }
            if (Input.GetKeyDown(KeyCode.C))
            {
                pool.ClearAll();
                Debug.Log("[MoriGame] 调试：清空全部僵尸");
            }
            if (Input.GetKeyDown(KeyCode.G))
            {
                practiceLoop = !practiceLoop;
                Debug.Log($"[MoriGame] 调试：练习刷怪循环 = {practiceLoop}");
            }

            if (!practiceLoop)
            {
                return;
            }
            nextSpawnRemaining -= Time.deltaTime;
            if (nextSpawnRemaining <= 0f)
            {
                nextSpawnRemaining = practiceIntervalSeconds;
                if (pool.ActiveCount < practiceMaxAlive)
                {
                    Spawn(pool, 1);
                }
            }
        }

        private void Spawn(ZombiePool pool, int count)
        {
            var spawned = 0;
            for (var i = 0; i < count; i++)
            {
                if (pool.TryGetSpawnPosition(out var position))
                {
                    pool.SpawnNormal(position);
                    spawned++;
                }
            }
            Debug.Log($"[MoriGame] 调试：刷普通感染者 ×{spawned}/{count}（存活 {pool.ActiveCount}）");
        }
    }
}
#endif
