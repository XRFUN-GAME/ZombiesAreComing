using MoriGame.Configs;
using UnityEngine;

namespace MoriGame.Devices
{
    /// <summary>
    /// 中控装置（docs/07 4.5）。数值走 CoreDeviceConfig（红线：不硬编码）；
    /// 状态机逻辑在 CoreDeviceState（纯 C# 可单测），本组件只做场景接线与 Tick。
    /// </summary>
    public class CoreDevice : MonoBehaviour
    {
        [Tooltip("由 MoriSceneSetup 注入；缺省时报错并停用")]
        public CoreDeviceConfig config;

        public CoreDeviceState State { get; private set; }

        public CoreDeviceState EnsureState()
        {
            if (State == null)
            {
                if (config == null)
                {
                    Debug.LogError("[MoriGame] CoreDevice 缺少 CoreDeviceConfig（先跑 ConfigAssetGenerator.GeneratePlayerCoreConfigs）");
                    return null;
                }
                State = new CoreDeviceState(config.maxHp, config.paralyzeSeconds, config.recoverHp);
            }
            return State;
        }

        private void Awake()
        {
            EnsureState();
        }

        private void Update()
        {
            State?.Tick(Time.deltaTime);
        }

        public bool TakeDamage(int amount)
        {
            if (State == null)
            {
                return false;
            }
            return State.TakeDamage(ApplyNewbieFactor(amount, NewbiePhaseIndex(),
                config != null ? config.newbiePhaseCount : 0,
                config != null ? config.newbieDamageFactor : 1f));
        }

        /// <summary>新手期减伤（纯函数可单测）：P1/P2（前 newbiePhaseCount 个阶段）受伤 ×factor，P3 起 ×1.0。
        /// phaseIndex<0（无调度器/编辑器直测）视为不减免。</summary>
        public static int ApplyNewbieFactor(int amount, int phaseIndex, int newbiePhaseCount, float factor)
        {
            if (phaseIndex < 0 || phaseIndex >= newbiePhaseCount)
            {
                return amount;
            }
            return Mathf.Max(1, Mathf.RoundToInt(amount * factor));
        }

        private static int NewbiePhaseIndex()
        {
            var director = Waves.WaveDirector.Instance;
            return director != null && director.Scheduler != null ? director.Scheduler.PhaseIndex : -1;
        }

        /// <summary>维修/恢复：仅正常态可回血，钳到 MaxHp（2026-11-11）</summary>
        public bool Heal(int amount)
        {
            return State != null && State.Heal(amount);
        }
    }
}
