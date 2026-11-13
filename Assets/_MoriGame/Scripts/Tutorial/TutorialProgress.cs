namespace MoriGame.Tutorial
{
    /// <summary>
    /// M2-T0 新手引导四件套纯逻辑层（零 Unity 依赖，EditMode 可单测）——延续 TerritoryGrid/WaveScheduler 模式。
    /// 清单步骤与 JIT 提示类别、图例淡出阈值的数值语义均按任务单（milestones/M2-单局闭环.md M2-T0）。
    /// </summary>

    /// <summary>引导清单四步（检测真实手势事件打勾；UI 按 CurrentStep 顺序展示，完成检测不强制顺序）。
    /// 2026-11-05 站桩工作台制（docs/07）：全程站桩不动——抓枪→射击→抓建筑→放置。</summary>
    public enum TutorialStep : int
    {
        GrabWeapon = 0,  // ① 右手伸向工作台右侧枪械插槽，捏合抓起枪（抓起即装备）
        FireOnce = 1,    // ② 食指伸直射击一次
        GrabStand = 2,   // ③ 右手伸向左侧建筑展示位，捏合抓起（抓起即购买）
        PlaceDown = 3    // ④ 看向地面捏合放下，落地放置
    }

    /// <summary>JITHint 两类情境提示（每类每局最多 2 次）。
    /// 原第三类 HurtNoTurn（侧推转身提示）随转向功能移除（2026-11-06）。</summary>
    public enum JitHintKind : int
    {
        FirstWave = 0,     // 首波僵尸到 → "食指伸直=开火"
        StandIdle = 1      // 开局 12 秒未抓起展示位模型 → 抓起操作提示
    }

    /// <summary>常驻手势图例淡出阈值：对应操作完成 3 次后淡出</summary>
    public enum TutorialAction : int
    {
        Fire = 0,    // 开火
        Confirm = 1  // 捏合确认（抓枪/抓建筑/放置）
    }

    /// <summary>四步清单状态机：乱序完成检测 + 顺序引导（CurrentStep=第一个未完成步）。
    /// 激活窗口（准备期 12 秒）由控制器管，这里只管打勾与完成判定。</summary>
    public sealed class TutorialChecklist
    {
        public const int StepCount = 4;

        private readonly bool[] done = new bool[StepCount];

        /// <summary>全部步骤完成（引导提前收起的条件之一；另一条件是准备期超时）</summary>
        public bool AllDone => done[0] && done[1] && done[2] && done[3];

        /// <summary>第一个未完成步骤（顺序引导展示用）；全完成返回 -1</summary>
        public int CurrentStep
        {
            get
            {
                for (var i = 0; i < StepCount; i++)
                {
                    if (!done[i])
                    {
                        return i;
                    }
                }
                return -1;
            }
        }

        public bool IsDone(TutorialStep step) => done[(int)step];

        /// <summary>打勾（幂等）；返回该步是否首次完成（驱动 UI 打勾反馈）</summary>
        public bool MarkDone(TutorialStep step)
        {
            var i = (int)step;
            if (done[i])
            {
                return false;
            }
            done[i] = true;
            return true;
        }

        /// <summary>全部清零（教学关「重新演示」，M4-T0）</summary>
        public void ResetAll()
        {
            for (var i = 0; i < StepCount; i++)
            {
                done[i] = false;
            }
        }
    }

    /// <summary>JIT 提示预算：每类每局最多 MaxPerMatch 次（防唠叨）；类间独立计数。</summary>
    public sealed class JitHintBudget
    {
        public const int MaxPerMatch = 2;
        public const int KindCount = 2;

        private readonly int[] used = new int[KindCount];

        public int Used(JitHintKind kind) => used[(int)kind];

        /// <summary>尝试消耗一次预算；返回 true=允许显示（调用方负责实际展示）</summary>
        public bool TryConsume(JitHintKind kind)
        {
            var i = (int)kind;
            if (used[i] >= MaxPerMatch)
            {
                return false;
            }
            used[i]++;
            return true;
        }
    }

    /// <summary>图例淡出计数：开火/确认各满 FadeThreshold 次 → 图例条淡出。
    /// 原左条（侧推转身）随转向功能移除（2026-11-06），仅存右条承载两条语义。</summary>
    public sealed class LegendFadeTracker
    {
        public const int FadeThreshold = 3;
        public const int ActionCount = 2;

        private readonly int[] counts = new int[ActionCount];

        public int Count(TutorialAction action) => counts[(int)action];

        public void Increment(TutorialAction action)
        {
            if (counts[(int)action] < FadeThreshold)
            {
                counts[(int)action]++; // 封顶计数：溢出无意义，防长局无限增长
            }
        }

        /// <summary>右条可见（开火与确认任一未满 3 次）</summary>
        public bool RightVisible => counts[(int)TutorialAction.Fire] < FadeThreshold ||
                                    counts[(int)TutorialAction.Confirm] < FadeThreshold;
    }

    /// <summary>M4-T0 毕业标记（PlayerPrefs 持久化，跨启动）：
    /// 首次启动无标记 → 强制教学；毕业（完成四步或跳过）→ 写标记。</summary>
    public static class TutorialGraduation
    {
        public const string PrefsKey = "MoriGame.Tutorial.Graduated";

        public static bool IsGraduated()
        {
            return UnityEngine.PlayerPrefs.GetInt(PrefsKey, 0) == 1;
        }

        public static void SetGraduated()
        {
            UnityEngine.PlayerPrefs.SetInt(PrefsKey, 1);
            UnityEngine.PlayerPrefs.Save();
        }

        /// <summary>仅测试用：清除标记（真实玩家无入口）</summary>
        public static void ClearForTest()
        {
            UnityEngine.PlayerPrefs.DeleteKey(PrefsKey);
            UnityEngine.PlayerPrefs.Save();
        }
    }

    // StartRouteDecider 已删除（2026-11-12 用户定版：选择页始终显示，不再强制路由——
    // 未毕业时 StartSceneController 仅锁定「进入正式关卡」按钮）
}
