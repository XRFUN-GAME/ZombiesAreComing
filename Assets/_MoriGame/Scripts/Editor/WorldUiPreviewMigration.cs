using System.Collections.Generic;
using MoriGame.Shop;
using MoriGame.Tutorial;
using MoriGame.Ui;
using MoriGame.Waves;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoriGame.Editor
{
    public static class WorldUiPreviewMigration
    {
        private const string MainScene = "Assets/_MoriGame/Scenes/MoriMain.unity";
        private const string TutorialScene = "Assets/_MoriGame/Scenes/Tutorial.unity";

        [MenuItem("MoriGame/世界 UI/生成或补齐可视化锚点")]
        public static void Run()
        {
            EditorSceneManager.SaveOpenScenes();
            var originalPath = EditorSceneManager.GetActiveScene().path;
            try
            {
                ApplyMain();
                ApplyTutorial();
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalPath) && EditorSceneManager.GetActiveScene().path != originalPath)
                {
                    EditorSceneManager.OpenScene(originalPath);
                }
            }
        }

        private static void ApplyMain()
        {
            var scene = EditorSceneManager.OpenScene(MainScene);
            SceneRecordAudit.Run();
            var root = EnsureRoot(scene);
            EnsureStatusHud(root);
            var source = Object.FindFirstObjectByType<WaveDirector>(FindObjectsInactive.Include);
            var font = source != null ? source.font : null;
            if (font == null)
            {
                throw new System.InvalidOperationException("MoriMain 缺 WaveDirector 中文字体");
            }

            var toast = EnsureAnchor(root, WorldUiPanels.Toast, 65f, 2.4f, 0.8f);
            if (toast.previewContent == null)
            {
                var host = new GameObject("ToastPreviewBuilder");
                try
                {
                    var builder = host.AddComponent<WaveDirector>();
                    builder.font = font;
                    SetPreview(toast, builder.BuildToastEditorPreview());
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }

            var settlement = EnsureAnchor(root, WorldUiPanels.Settlement, 65f, 2.4f, 0.95f);
            if (settlement.previewContent == null)
            {
                SetPreview(settlement, SettlementPanel.BuildEditorPreview(font).gameObject);
            }
            else
            {
                foreach (var text in settlement.previewContent.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name == "KeyData")
                    {
                        text.text = text.text.Replace("对局 12:00", "对局 08:00");
                        EditorUtility.SetDirty(text);
                    }
                }
            }
            ValidateUniqueIds(scene);
            Save(scene);
        }

        private static void ApplyTutorial()
        {
            var scene = EditorSceneManager.OpenScene(TutorialScene);
            SceneRecordAudit.Run();
            var root = EnsureRoot(scene);
            EnsureStatusHud(root);
            var source = Object.FindFirstObjectByType<TutorialController>(FindObjectsInactive.Include);
            var font = source != null ? source.font : null;
            if (font == null)
            {
                throw new System.InvalidOperationException("Tutorial 缺 TutorialController 中文字体");
            }

            var anchors = new Dictionary<string, WorldUiAnchor>
            {
                ["Checklist"] = EnsureAnchor(root, WorldUiPanels.TutorialChecklist, 70f, 2.4f, 0.95f),
                ["JitHint"] = EnsureAnchor(root, WorldUiPanels.TutorialJitHint, 180f, 2.3f, 0.45f),
                ["LegendRight"] = EnsureAnchor(root, WorldUiPanels.TutorialLegendRight, 120f, 2.3f, 1.25f),
                ["RotateHint"] = EnsureAnchor(root, WorldUiPanels.TutorialRotateHint, -115f, 2.3f, 1.15f),
                ["GraduatePanel"] = EnsureAnchor(root, WorldUiPanels.TutorialGraduate, 70f, 2.4f, 0.95f),
                ["BtnRedo"] = EnsureAnchor(root, WorldUiPanels.TutorialRedo, -155f, 2.3f, 0.85f),
                ["BtnSkip"] = EnsureAnchor(root, WorldUiPanels.TutorialSkip, 155f, 2.3f, 0.85f)
            };

            if (NeedsPreview(anchors, "Checklist", "JitHint", "LegendRight", "RotateHint"))
            {
                var host = new GameObject("TutorialPanelPreviewBuilder");
                try
                {
                    var builder = host.AddComponent<TutorialController>();
                    builder.font = font;
                    foreach (var panel in builder.BuildEditorPreviews())
                    {
                        SetPreview(anchors[panel.name], panel);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }

            if (NeedsPreview(anchors, "GraduatePanel", "BtnRedo", "BtnSkip"))
            {
                var host = new GameObject("TutorialFlowPreviewBuilder");
                try
                {
                    var builder = host.AddComponent<TutorialFlow>();
                    builder.font = font;
                    foreach (var panel in builder.BuildEditorPreviews())
                    {
                        SetPreview(anchors[panel.name], panel);
                    }
                }
                finally
                {
                    Object.DestroyImmediate(host);
                }
            }
            ValidateUniqueIds(scene);
            Save(scene);
        }

        private static Transform EnsureRoot(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == "WorldUi")
                {
                    return go.transform;
                }
            }
            var root = new GameObject("WorldUi");
            SceneManager.MoveGameObjectToScene(root, scene);
            return root.transform;
        }

        private static void EnsureStatusHud(Transform root)
        {
            var hud = Object.FindFirstObjectByType<StatusHud>(FindObjectsInactive.Include);
            if (hud == null)
            {
                throw new System.InvalidOperationException(root.gameObject.scene.name + " 缺 StatusHud");
            }
            hud.transform.SetParent(root, true);
            var anchor = hud.GetComponent<WorldUiAnchor>();
            if (anchor == null)
            {
                anchor = hud.gameObject.AddComponent<WorldUiAnchor>();
            }
            anchor.panelId = WorldUiPanels.StatusHud;
            hud.BuildEditorPreview();
            EditorUtility.SetDirty(hud);
            EditorUtility.SetDirty(anchor);
        }

        private static WorldUiAnchor EnsureAnchor(Transform root, string panelId, float yaw, float distance,
            float height)
        {
            var scene = root.gameObject.scene;
            WorldUiAnchor found = null;
            foreach (var anchor in Object.FindObjectsByType<WorldUiAnchor>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (anchor.gameObject.scene != scene || anchor.panelId != panelId)
                {
                    continue;
                }
                if (found != null)
                {
                    throw new System.InvalidOperationException(scene.name + " 重复 UI 锚点：" + panelId);
                }
                found = anchor;
            }
            if (found != null)
            {
                return found;
            }

            var go = new GameObject("Anchor_" + panelId);
            go.transform.SetParent(root, false);
            found = go.AddComponent<WorldUiAnchor>();
            found.panelId = panelId;
            UiTheme.PlaceFixedPanel(go.transform, yaw, distance, height);
            go.transform.SetParent(root, true);
            EditorUtility.SetDirty(found);
            return found;
        }

        private static bool NeedsPreview(Dictionary<string, WorldUiAnchor> anchors, params string[] names)
        {
            foreach (var name in names)
            {
                if (anchors[name].previewContent == null)
                {
                    return true;
                }
            }
            return false;
        }

        private static void SetPreview(WorldUiAnchor anchor, GameObject preview)
        {
            if (preview == null)
            {
                throw new System.InvalidOperationException("预览构建失败：" + anchor.panelId);
            }
            if (anchor.previewContent != null)
            {
                Object.DestroyImmediate(preview);
                return;
            }
            preview.transform.SetParent(anchor.transform, true);
            anchor.previewContent = preview;
            EditorUtility.SetDirty(anchor);
        }

        private static void ValidateUniqueIds(Scene scene)
        {
            var ids = new HashSet<string>();
            foreach (var anchor in Object.FindObjectsByType<WorldUiAnchor>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (anchor.gameObject.scene == scene && !ids.Add(anchor.panelId))
                {
                    throw new System.InvalidOperationException(scene.name + " 重复 UI 锚点：" + anchor.panelId);
                }
            }
        }

        private static void Save(Scene scene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[MoriGame] 世界 UI 可视化锚点已保存：" + scene.path);
        }
    }
}
