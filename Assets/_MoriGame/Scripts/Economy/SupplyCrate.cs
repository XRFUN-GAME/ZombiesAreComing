using MoriGame.Gestures;
using MoriGame.Player;
using MoriGame.Waves;
using TMPro;
using UnityEngine;

namespace MoriGame.Economy
{
    public class SupplyCrate : MonoBehaviour
    {
        public PicoGestureHub hub;
        public GameObject visualRoot;
        public TMP_Text label;
        public Collider gazeCollider;
        public int randomSeed = 20261106;
        public float gazeRangeMeters = 3f; // 高台制：玩家眼位高于工作台约 1.5 米，2 米不够
        public KeyCode openKey = KeyCode.B;

        private SupplyCrateState state;
        private Transform head;

        private void Start()
        {
            var director = WaveDirector.Instance;
            var duration = director != null && director.config != null
                ? director.config.matchDurationSeconds
                : 480f;
            state = new SupplyCrateState(randomSeed, matchDurationSeconds: duration);
            RefreshVisual();
        }

        private void Update()
        {
            if (state == null)
            {
                return;
            }
            if (head == null)
            {
                var cam = Camera.main;
                if (cam == null)
                {
                    return;
                }
                head = cam.transform;
            }

            var match = MatchBootstrap.Instance;
            var director = WaveDirector.Instance;
            var matchEnded = director != null && director.Scheduler != null && director.Scheduler.Ended;
            var elapsed = director != null && director.Scheduler != null
                ? director.Scheduler.ElapsedSeconds
                : match != null ? match.MatchElapsedSeconds : 0f;
            var canInteract = !matchEnded && match != null && match.Vitals != null &&
                              match.Vitals.State != VitalsState.Downed;
            var gazed = canInteract && state.Available && IsGazed();
            var opening = gazed && hub != null && hub.Pinching(GestureHand.Right);
            if (canInteract && state.Available && Input.GetKey(openKey))
            {
                opening = true;
            }

            var reward = state.Tick(opening, Time.deltaTime, elapsed);
            if (reward > 0 && match != null)
            {
                match.Wallet.Add(reward);
                match.Recorder?.RecordGold(GoldLedgerCategory.Supply, reward);
                Vfx.VfxBus.Play(l => l.crateOpen, transform.position + Vector3.up * 0.4f); // 金币喷溅（2026-11-11）
                MoriGame.Audio.SfxBus.World(MoriGame.Audio.SfxBus.Lib != null ? MoriGame.Audio.SfxBus.Lib.crateOpen : null, transform.position);
                MoriGame.Audio.SfxBus.Coin(reward);
                director?.ShowToast($"补给箱 +{reward} 金币");
                Debug.Log($"[MoriGame] 补给箱开启：+{reward} 金币，120 秒后刷新");
            }
            RefreshVisual();
        }

        private bool IsGazed()
        {
            if (gazeCollider == null)
            {
                return false;
            }
            return Physics.Raycast(new Ray(head.position, head.forward), out var hit, gazeRangeMeters) &&
                   hit.collider == gazeCollider;
        }

        private void RefreshVisual()
        {
            if (visualRoot != null && visualRoot.activeSelf != state.Available)
            {
                visualRoot.SetActive(state.Available);
            }
            if (gazeCollider != null && gazeCollider.enabled != state.Available)
            {
                gazeCollider.enabled = state.Available;
            }
            if (label == null)
            {
                return;
            }
            label.text = !state.Available
                ? $"补给箱 · {Mathf.CeilToInt(state.RespawnRemainingSeconds)}s"
                : state.OpenProgress > 0f
                    ? $"开箱 {Mathf.RoundToInt(state.OpenProgress * 100f)}%"
                    : "补给箱\n凝视+捏合 1.5 秒";
        }

        private void LateUpdate()
        {
            if (label == null || Camera.main == null)
            {
                return;
            }
            var toCam = Camera.main.transform.position - label.transform.position;
            toCam.y = 0f;
            if (toCam.sqrMagnitude > 0.0001f)
            {
                label.transform.rotation = Quaternion.LookRotation(-toCam.normalized);
            }
        }
    }
}
