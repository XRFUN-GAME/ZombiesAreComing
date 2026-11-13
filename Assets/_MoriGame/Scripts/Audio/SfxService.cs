using UnityEngine;

namespace MoriGame.Audio
{
    /// <summary>音效播放服务（MatchBootstrap 子组件）：2D 单源 + 3D 一次性源 + 循环源。
    /// 音量/音距/音调/并发参数全读 SfxLibrary；库为空时全部静默（编辑器测试安全）。</summary>
    public class SfxService : MonoBehaviour
    {
        private SfxLibrary lib;
        private AudioSource source2D;
        private int deathFrame = -1;
        private int deathCountThisFrame;

        public SfxLibrary Lib => lib;

        public void Init(SfxLibrary library)
        {
            lib = library;
            source2D = gameObject.AddComponent<AudioSource>();
            source2D.playOnAwake = false;
            source2D.spatialBlend = 0f;
        }

        /// <summary>UI/钱包/Toast 类（2D，uiVolume）</summary>
        public void PlayUi(AudioClip clip)
        {
            if (lib == null || clip == null)
            {
                return;
            }
            source2D.PlayOneShot(clip, lib.uiVolume);
        }

        /// <summary>游戏 2D（玩家开火 loop 之外的玩家事件，sfxVolume；pitchRandom 防重复感）</summary>
        public void PlayGame2D(AudioClip clip, bool pitchRandom = false)
        {
            if (lib == null || clip == null)
            {
                return;
            }
            source2D.pitch = pitchRandom ? RandomPitch() : 1f;
            source2D.PlayOneShot(clip, lib.sfxVolume);
            source2D.pitch = 1f;
        }

        /// <summary>世界声源一次性播放（3D 对数衰减，maxDistance≈15 米）</summary>
        public void PlayClip3D(AudioClip clip, Vector3 position, bool pitchRandom = false)
        {
            if (lib == null || clip == null)
            {
                return;
            }
            var src = NewSource(clip.name, position);
            ApplySpatial(src, true);
            src.pitch = pitchRandom ? RandomPitch() : 1f;
            src.clip = clip;
            src.Play();
            Destroy(src.gameObject, clip.length / Mathf.Max(src.pitch, 0.1f) + 0.1f);
        }

        /// <summary>循环音（玩家开火 loop=2D；电网 loop=3D）。返回源供 StopLoop。</summary>
        public AudioSource StartLoop(AudioClip clip, bool spatial3D, Vector3 position)
        {
            if (lib == null || clip == null)
            {
                return null;
            }
            var src = NewSource("Loop_" + clip.name, position);
            ApplySpatial(src, spatial3D);
            src.loop = true;
            src.clip = clip;
            src.Play();
            return src;
        }

        public void StopLoop(AudioSource loopSource)
        {
            if (loopSource != null)
            {
                Destroy(loopSource.gameObject);
            }
        }

        /// <summary>僵尸死亡：弱点击杀优先，按类型挑音池随机一条；同帧超过 deathConcurrentLimit 丢弃（防爆音）</summary>
        public void PlayDeath(Vector3 position, string zombieId, bool weakpointKill)
        {
            if (lib == null)
            {
                return;
            }
            var clip = weakpointKill ? lib.weakpointKill : PickDeathClip(zombieId);
            if (clip == null)
            {
                return;
            }
            if (Time.frameCount != deathFrame)
            {
                deathFrame = Time.frameCount;
                deathCountThisFrame = 0;
            }
            if (deathCountThisFrame >= Mathf.Max(1, lib.deathConcurrentLimit))
            {
                return;
            }
            deathCountThisFrame++;
            PlayClip3D(clip, position, true);
        }

        private AudioClip PickDeathClip(string zombieId)
        {
            if (zombieId == "Overseer")
            {
                return lib.overseerDeath;
            }
            var pool = zombieId == "Brute" ? lib.bruteDeathPool : lib.normalDeathPool;
            if (pool == null || pool.Length == 0)
            {
                return null;
            }
            return pool[Random.Range(0, pool.Length)]; // 空槽自然静默
        }

        private AudioSource NewSource(string name, Vector3 position)
        {
            var go = new GameObject("Sfx_" + name);
            go.transform.SetParent(transform, false);
            go.transform.position = position;
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.volume = lib.sfxVolume;
            src.dopplerLevel = 0f;
            return src;
        }

        private void ApplySpatial(AudioSource src, bool spatial3D)
        {
            src.spatialBlend = spatial3D ? 1f : 0f;
            if (spatial3D)
            {
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 1f;
                src.maxDistance = lib.spatialMaxDistance;
            }
        }

        private float RandomPitch()
        {
            return Random.Range(lib.pitchMin, lib.pitchMax);
        }
    }
}
