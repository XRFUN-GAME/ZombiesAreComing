using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MoriGame.Editor
{
    /// <summary>枪模替换（2026-11-09）：WeaponController/Visual 下的 GunModel 整体替换为指定 prefab 实例
    /// （PrefabUtility.InstantiatePrefab 保持预制体链接，审计按 GetCorrespondingObjectFromSource 验源）；
    /// 手腕挂载位姿快照、Muzzle/Magazine 校准点、插槽抓放链路全部不动。
    /// 触发：Builds/swap-gun.trigger。可重复执行（幂等）。</summary>
    public static class GunModelSwap
    {
        public const string PrefabPath = "Assets/ArtAsset/Gun/MD_Pistol/Pistol.prefab";
        private const string ScenePath = "Assets/_MoriGame/Scenes/MoriMain.unity";

        public static void Run()
        {
            EditorSceneManager.SaveOpenScenes();
            EditorSceneManager.OpenScene(ScenePath);

            var weapon = UnityEngine.Object.FindFirstObjectByType<Combat.WeaponController>();
            if (weapon == null || weapon.visualRoot == null)
            {
                throw new InvalidOperationException("场景里找不到 WeaponController/visualRoot");
            }
            var visual = weapon.visualRoot.transform;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new FileNotFoundException("找不到枪模预制体", PrefabPath);
            }

            var oldModel = visual.Find("GunModel");
            if (oldModel != null)
            {
                var oldSource = PrefabUtility.GetCorrespondingObjectFromSource(oldModel.gameObject);
                var oldPath = oldSource != null ? AssetDatabase.GetAssetPath(oldSource) : "(非预制体实例)";
                UnityEngine.Object.DestroyImmediate(oldModel.gameObject);
                Debug.Log("[MoriGame] 旧枪模已移除：" + oldPath);
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = "GunModel";
            instance.transform.SetParent(visual, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            EditorUtility.SetDirty(weapon.gameObject);

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[MoriGame] 枪模替换完成：" + PrefabPath + "（手腕位姿/Muzzle/插槽链路未动；握持贴合与枪口位置如需微调请编辑器手动校准）");
        }
    }
}
