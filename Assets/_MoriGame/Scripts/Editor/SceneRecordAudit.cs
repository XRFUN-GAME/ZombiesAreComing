using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MoriGame.Editor
{
    /// <summary>场景记录器（2026-11-06）：用户手动改场景后先固化现状——保存打开场景 → 文件级备份 →
    /// 全层级转储（路径/激活/标签/组件/局部与世界变换）到 Builds/scene-record.txt。只读记录不改对象。</summary>
    public static class SceneRecordAudit
    {
        public static void Run()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                throw new System.InvalidOperationException("无活动场景");
            }
            var saveLine = scene.path + " saved=" + (UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes() ? "true" : "false");

            var backupDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds", "SceneBackups");
            Directory.CreateDirectory(backupDir);
            var src = Path.Combine(Directory.GetCurrentDirectory(), scene.path.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar));
            var backup = Path.Combine(backupDir, Path.GetFileNameWithoutExtension(scene.path) + "-record-" + System.DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".unity");
            if (File.Exists(src))
            {
                File.Copy(src, backup, true);
            }

            var sb = new StringBuilder();
            sb.AppendLine("scene=" + scene.path);
            sb.AppendLine("backup=" + backup.Replace('\\', '/'));
            sb.AppendLine("savedAt=" + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            foreach (var root in scene.GetRootGameObjects())
            {
                Dump(root.transform, 0, sb);
            }
            File.WriteAllText(Path.Combine(Directory.GetCurrentDirectory(), "Builds", "scene-record.txt"), sb.ToString());
            Debug.Log("[MoriGame] 场景记录完成：" + saveLine + " backup=" + backup);
        }

        private static void Dump(Transform t, int depth, StringBuilder sb)
        {
            var go = t.gameObject;
            var comps = go.GetComponents<Component>();
            var names = new StringBuilder();
            foreach (var c in comps)
            {
                if (c == null)
                {
                    names.Append("[missing],");
                    continue;
                }
                var n = c.GetType().Name;
                if (n == "Transform")
                {
                    continue;
                }
                names.Append(n).Append(',');
            }
            var lp = t.localPosition;
            var le = t.localEulerAngles;
            var ls = t.localScale;
            var wp = t.position;
            sb.Append(new string(' ', depth * 2)).Append(go.name)
                .Append(" | active=").Append(go.activeSelf)
                .Append(" tag=").Append(go.tag)
                .Append(" | L p=(").Append(lp.x.ToString("0.###")).Append(',').Append(lp.y.ToString("0.###")).Append(',').Append(lp.z.ToString("0.###")).Append(')')
                .Append(" r=(").Append(le.x.ToString("0.#")).Append(',').Append(le.y.ToString("0.#")).Append(',').Append(le.z.ToString("0.#")).Append(')')
                .Append(" s=(").Append(ls.x.ToString("0.###")).Append(',').Append(ls.y.ToString("0.###")).Append(',').Append(ls.z.ToString("0.###")).Append(')')
                .Append(" | W p=(").Append(wp.x.ToString("0.###")).Append(',').Append(wp.y.ToString("0.###")).Append(',').Append(wp.z.ToString("0.###")).Append(')');
            if (names.Length > 0)
            {
                sb.Append(" | ").Append(names.ToString(0, names.Length - 1));
            }
            sb.AppendLine();
            for (var i = 0; i < t.childCount; i++)
            {
                Dump(t.GetChild(i), depth + 1, sb);
            }
        }
    }
}
