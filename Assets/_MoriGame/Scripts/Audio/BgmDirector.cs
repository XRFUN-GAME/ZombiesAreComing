using UnityEngine;

namespace MoriGame.Audio
{
    /// <summary>背景音乐导演（2026-11-10）：双 AudioSource 交叉淡化，切换丝滑。
    /// 轮播 bg1 ↔ bg2，每轮按概率插紧张曲（至少隔 2 轮普通曲防密集）；
    /// 督战者实体生成 → 交叉切 Boss 主题循环，督战者死亡 → 切回轮播；对局结束 → 渐静停止。
    /// 当前曲剩余时长 ≤ 淡化窗口时预启动下一曲，听感无接缝。</summary>
    public class BgmDirector : MonoBehaviour
    {
        private SfxLibrary library;
        private AudioSource sourceA;
        private AudioSource sourceB;
        private AudioSource current;
        private AudioSource previous; // 淡出中的旧源
        private bool fading;
        private float fadeT;
        private int rotationIndex; // 0=bg1 1=bg2
        private int rotationsSinceTension;
        private bool bossMode;
        private bool stopped;

        public void Init(SfxLibrary lib)
        {
            library = lib;
            sourceA = gameObject.AddComponent<AudioSource>();
            sourceB = gameObject.AddComponent<AudioSource>();
            foreach (var src in new[] { sourceA, sourceB })
            {
                src.playOnAwake = false;
                src.spatialBlend = 0f; // 背景乐全局 2D
            }
            current = sourceA;
        }

        /// <summary>对局开始：起播轮播（MatchBootstrap.Awake 调用）</summary>
        public void StartRotation()
        {
            if (library == null || library.bgmLoop1 == null)
            {
                return;
            }
            stopped = false;
            bossMode = false;
            rotationIndex = 0;
            rotationsSinceTension = 0;
            CrossfadeTo(library.bgmLoop1, false);
        }

        /// <summary>督战者实体生成（07:12 特殊事件成功落地）——切 Boss 主题循环</summary>
        public void OnBossSpawned()
        {
            if (library == null || library.bgmBoss == null || bossMode)
            {
                return;
            }
            bossMode = true;
            CrossfadeTo(library.bgmBoss, true);
        }

        /// <summary>督战者死亡——切回普通轮播</summary>
        public void OnBossDied()
        {
            if (!bossMode || library == null)
            {
                return;
            }
            bossMode = false;
            rotationIndex ^= 1;
            rotationsSinceTension = 0;
            CrossfadeTo(rotationIndex == 0 ? library.bgmLoop1 : library.bgmLoop2, false);
        }

        /// <summary>对局结束——当前曲渐静后停止（结算界面静场）</summary>
        public void FadeOutStop()
        {
            stopped = true;
            bossMode = false;
            fading = false;
        }

        private void Update()
        {
            if (library == null || current == null)
            {
                return;
            }
            var fade = Mathf.Max(0.1f, library.bgmCrossfadeSeconds);

            if (stopped)
            {
                if (current.clip != null && current.isPlaying)
                {
                    current.volume = Mathf.Max(0f, current.volume - library.bgmVolume * Time.deltaTime / fade);
                    if (current.volume <= 0.001f)
                    {
                        current.Stop();
                        current.clip = null;
                    }
                }
                return;
            }

            if (fading)
            {
                fadeT = Mathf.Min(1f, fadeT + Time.deltaTime / fade);
                var eased = Mathf.SmoothStep(0f, 1f, fadeT); // 平滑曲线，避免线性切换的突兀感
                current.volume = library.bgmVolume * eased;
                if (previous != null)
                {
                    previous.volume = library.bgmVolume * (1f - eased);
                    if (fadeT >= 1f)
                    {
                        previous.Stop();
                        previous.clip = null;
                        previous = null;
                    }
                }
                if (fadeT >= 1f)
                {
                    fading = false;
                }
            }

            // 轮播调度：非 Boss、非单曲循环、剩余时长进入淡化窗口时预切下一曲
            if (!fading && !bossMode && current.clip != null && !current.loop &&
                current.clip.length - current.time <= fade)
            {
                AdvanceRotation();
            }
        }

        private void AdvanceRotation()
        {
            rotationsSinceTension++;
            AudioClip next;
            if (library.bgmTension != null && rotationsSinceTension >= 3 &&
                Random.value < library.bgmTensionChance)
            {
                next = library.bgmTension; // 插曲不改轮播指针，播完继续 bg1/bg2 交替
                rotationsSinceTension = 0;
            }
            else
            {
                rotationIndex ^= 1;
                next = rotationIndex == 0 ? library.bgmLoop1 : library.bgmLoop2;
            }
            CrossfadeTo(next, false);
        }

        private void CrossfadeTo(AudioClip clip, bool loop)
        {
            if (clip == null || current == null)
            {
                return;
            }
            var next = current == sourceA ? sourceB : sourceA;
            next.clip = clip;
            next.loop = loop;
            next.volume = 0f;
            next.Play();
            previous = current;
            current = next;
            fadeT = 0f;
            fading = true;
        }
    }
}
