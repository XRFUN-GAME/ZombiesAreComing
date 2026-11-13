using UnityEngine;

namespace MoriGame.Audio
{
    /// <summary>音效总线门面：调用点一行直达；bootstrap 缺席/库为空时静默（编辑器测试安全）。
    /// Clip 经 SfxBus.Lib 取字段；循环音保留返回的 AudioSource 供 StopLoop。</summary>
    public static class SfxBus
    {
        public static SfxLibrary Lib => MatchBootstrap.Instance != null ? MatchBootstrap.Instance.sfxLibrary : null;

        private static SfxService S => MatchBootstrap.Instance != null ? MatchBootstrap.Instance.Sfx : null;

        public static void Ui(AudioClip clip)
        {
            if (clip != null)
            {
                S?.PlayUi(clip);
            }
        }

        public static void Game2D(AudioClip clip, bool pitchRandom = false)
        {
            if (clip != null)
            {
                S?.PlayGame2D(clip, pitchRandom);
            }
        }

        public static void World(AudioClip clip, Vector3 position, bool pitchRandom = false)
        {
            if (clip != null)
            {
                S?.PlayClip3D(clip, position, pitchRandom);
            }
        }

        public static void Death(Vector3 position, string zombieId, bool weakpointKill)
        {
            S?.PlayDeath(position, zombieId, weakpointKill);
        }

        /// <summary>金币入账：≥10 大额音，否则小额音</summary>
        public static void Coin(int amount)
        {
            var lib = Lib;
            if (lib == null)
            {
                return;
            }
            var clip = amount >= 10 ? lib.coinBig : lib.coinSmall;
            if (clip != null)
            {
                S?.PlayUi(clip);
            }
        }

        public static AudioSource StartLoop(AudioClip clip, bool spatial3D, Vector3 position)
        {
            return clip != null ? S?.StartLoop(clip, spatial3D, position) : null;
        }

        public static void StopLoop(AudioSource loopSource)
        {
            if (loopSource != null)
            {
                S?.StopLoop(loopSource);
            }
        }
    }
}
