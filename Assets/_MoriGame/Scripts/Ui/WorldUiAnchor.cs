using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoriGame.Ui
{
    [DisallowMultipleComponent]
    [SelectionBase]
    public sealed class WorldUiAnchor : MonoBehaviour
    {
        public string panelId;
        public GameObject previewContent;

        private void Awake()
        {
            if (!Application.isPlaying || previewContent == null)
            {
                return;
            }
            previewContent.SetActive(false);
            Destroy(previewContent);
            previewContent = null;
        }

        public static WorldUiAnchor Find(Scene scene, string panelId)
        {
            if (string.IsNullOrEmpty(panelId))
            {
                return null;
            }
            WorldUiAnchor result = null;
            var anchors = Object.FindObjectsByType<WorldUiAnchor>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var anchor in anchors)
            {
                if (anchor.gameObject.scene != scene || anchor.panelId != panelId)
                {
                    continue;
                }
                if (result != null)
                {
                    Debug.LogError($"[MoriGame] 场景 {scene.name} 存在重复世界 UI 锚点：{panelId}");
                    return null;
                }
                result = anchor;
            }
            return result;
        }
    }

    public static class WorldUiPanels
    {
        public const string StatusHud = "match.status_hud";
        public const string Toast = "match.toast";
        public const string Settlement = "match.settlement";
        public const string TutorialChecklist = "tutorial.checklist";
        public const string TutorialJitHint = "tutorial.jit_hint";
        public const string TutorialLegendRight = "tutorial.legend_right";
        public const string TutorialRotateHint = "tutorial.rotate_hint";
        public const string TutorialGraduate = "tutorial.graduate";
        public const string TutorialRedo = "tutorial.redo";
        public const string TutorialSkip = "tutorial.skip";
    }
}
