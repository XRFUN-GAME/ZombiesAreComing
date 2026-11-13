using UnityEngine;

namespace MoriGame.Ui
{
    /// <summary>WW Polygon 低模军事风 UI 主题（2026-11-12 用户定版）：炭黑半透明面板 + 米白粗边框 +
    /// 红强调（血条/伤害/锁定）、青绿信息色；蓝橙保留为左手/右手交互语义色。
    /// VR 适配：全部世界空间、面板短边 ≥4 厘米、字号按 1 米阅读距离校准、凝视+捏合交互不依赖悬停。</summary>
    public static class UiTheme
    {
        public static readonly Color PanelBg = new Color(0.086f, 0.096f, 0.088f, 0.90f); // 橄榄炭
        public static readonly Color PanelBorder = new Color(0.93f, 0.93f, 0.90f, 0.96f);
        public static readonly Color AccentRed = new Color(0.82f, 0.16f, 0.13f);
        public static readonly Color AccentOrange = new Color(0.95f, 0.55f, 0.10f);
        public static readonly Color AccentBlue = new Color(0.16f, 0.42f, 0.90f);
        public static readonly Color AccentCyan = new Color(0.30f, 0.85f, 1f);
        public static readonly Color TextMain = new Color(0.96f, 0.96f, 0.94f);
        public static readonly Color TextDim = new Color(0.68f, 0.70f, 0.73f);
        public static readonly Color LockedBg = new Color(0.15f, 0.16f, 0.15f, 0.92f);

        private static Mesh unitQuad;
        private static Material baseMaterial;

        /// <summary>单位四边形（1×1 米，居中）</summary>
        public static Mesh UnitQuad()
        {
            if (unitQuad == null)
            {
                unitQuad = new Mesh { name = "UiThemeQuad" };
                unitQuad.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
                };
                unitQuad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
                unitQuad.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                unitQuad.RecalculateNormals();
            }
            return unitQuad;
        }

        /// <summary>Sprites/Default 共享底材质（URP 兼容）；Tinted 返回独立着色实例</summary>
        public static Material BaseMat()
        {
            if (baseMaterial == null)
            {
                baseMaterial = new Material(Shader.Find("Sprites/Default"));
            }
            return baseMaterial;
        }

        public static Material Tinted(Color color)
        {
            return new Material(BaseMat()) { color = color };
        }

        /// <summary>优先使用场景中手调的世界 UI 锚点；未配置时回退到玩法锚点默认布局。</summary>
        public static bool PlaceFixedPanel(Transform panel, string panelId, float yawOffsetDegrees,
            float distanceMeters, float heightAboveAnchor)
        {
            if (panel == null)
            {
                return false;
            }
            var worldAnchor = WorldUiAnchor.Find(panel.gameObject.scene, panelId);
            if (worldAnchor == null)
            {
                return PlaceFixedPanel(panel, yawOffsetDegrees, distanceMeters, heightAboveAnchor);
            }
            if (worldAnchor.transform != panel)
            {
                panel.SetParent(null, true);
            }
            panel.SetPositionAndRotation(worldAnchor.transform.position, worldAnchor.transform.rotation);
            return true;
        }

        /// <summary>把信息面板固定到玩法锚点四周；面板位于低位并面朝锚点，不随相机移动。</summary>
        public static bool PlaceFixedPanel(Transform panel, float yawOffsetDegrees, float distanceMeters,
            float heightAboveAnchor)
        {
            if (panel == null)
            {
                return false;
            }
            panel.SetParent(null, true);
            var match = MatchBootstrap.Instance != null
                ? MatchBootstrap.Instance
                : UnityEngine.Object.FindFirstObjectByType<MatchBootstrap>();
            var anchor = match != null
                ? match.playSpaceAnchor != null ? match.playSpaceAnchor : match.playerRig
                : null;
            if (anchor == null)
            {
                return false;
            }
            var outward = Quaternion.Euler(0f, anchor.eulerAngles.y + yawOffsetDegrees, 0f) * Vector3.forward;
            panel.SetPositionAndRotation(
                anchor.position + outward * distanceMeters + Vector3.up * heightAboveAnchor,
                Quaternion.LookRotation(outward, Vector3.up));
            return true;
        }

        /// <summary>世界空间实心四边形（尺寸=米）；返回 MeshRenderer 所在子节点</summary>
        public static GameObject Quad(Transform parent, string name, Color color, Vector2 size, float localZ)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, localZ);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = UnitQuad();
            go.AddComponent<MeshRenderer>().sharedMaterial = Tinted(color);
            return go;
        }

        /// <summary>切角面板（WW Polygon 语言）：米白描边八边形背板 + 上下渐变面板色前板。
        /// frameFraction 兼容旧签名，仅作占位（描边厚度由短边自动推算）。</summary>
        public static GameObject FramedQuad(Transform parent, string name, Vector2 size, Color panelColor,
            float frameFraction = 0.08f)
        {
            return ChamferQuad(parent, name, panelColor, size, 0f);
        }

        /// <summary>切角八边形面板：描边层（全尺寸米白）+ 填充层（内缩、顶亮底暗渐变）。z 越小越靠观众。</summary>
        public static GameObject ChamferQuad(Transform parent, string name, Color color, Vector2 size, float localZ,
            float chamferFrac = 0.16f)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var stroke = Mathf.Max(0.004f, Mathf.Min(size.x, size.y) * 0.03f);
            var border = OctMesh(root.transform, "Stroke", PanelBorder, size, 0f, 1f, 1f, localZ + 0.004f);
            border.transform.localScale = Vector3.one; // 尺寸已烘进网格
            var fillW = Mathf.Max(0.01f, size.x - stroke * 2f);
            var fillH = Mathf.Max(0.01f, size.y - stroke * 2f);
            var fill = OctMesh(root.transform, "Panel", color, new Vector2(fillW, fillH), 0f, 1.16f, 0.78f, localZ);
            fill.transform.localPosition = new Vector3(0f, 0f, localZ);
            return root;
        }

        /// <summary>八边形网格（四角 45° 切角）：8 环点 + 1 中心点扇形；顶点色做上下渐变（Sprites/Default 乘顶点色）。
        /// 面法向 -Z（与既有 Quad 一致，朝向观众）。</summary>
        private static GameObject OctMesh(Transform parent, string objName, Color color, Vector2 size,
            float inset, float brightTop, float brightBottom, float localZ)
        {
            var hw = Mathf.Max(0.001f, size.x * 0.5f - inset);
            var hh = Mathf.Max(0.001f, size.y * 0.5f - inset);
            var ch = Mathf.Min(hw, hh) * Mathf.Clamp01(0.32f + inset); // 切角绝对量
            var go = new GameObject(objName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, localZ);
            var mesh = new Mesh { name = "UiThemeOct" };
            var verts = new Vector3[9];
            var colors = new Color32[9];
            verts[0] = new Vector3(hw, -(hh - ch), 0f);
            verts[1] = new Vector3(hw, hh - ch, 0f);
            verts[2] = new Vector3(hw - ch, hh, 0f);
            verts[3] = new Vector3(-(hw - ch), hh, 0f);
            verts[4] = new Vector3(-hw, hh - ch, 0f);
            verts[5] = new Vector3(-hw, -(hh - ch), 0f);
            verts[6] = new Vector3(-(hw - ch), -hh, 0f);
            verts[7] = new Vector3(hw - ch, -hh, 0f);
            verts[8] = Vector3.zero;
            for (var i = 0; i < 8; i++)
            {
                var t = Mathf.Clamp01((verts[i].y + hh) / (hh * 2f));
                var bright = Mathf.Lerp(brightBottom, brightTop, t);
                colors[i] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f * bright), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f * bright), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f * bright), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255));
            }
            var midBright = (brightTop + brightBottom) * 0.5f;
            colors[8] = new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f * midBright), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f * midBright), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f * midBright), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(color.a * 255f), 0, 255));
            mesh.vertices = verts;
            mesh.colors32 = colors;
            var tris = new int[8 * 3];
            for (var i = 0; i < 8; i++) // 环点 CCW，(center, next, cur) → 法向 -Z
            {
                tris[i * 3] = 8;
                tris[i * 3 + 1] = (i + 1) % 8;
                tris[i * 3 + 2] = i;
            }
            mesh.triangles = tris;
            var uv = new Vector2[9];
            for (var i = 0; i < 8; i++)
            {
                uv[i] = new Vector2((verts[i].x + hw) / (hw * 2f), (verts[i].y + hh) / (hh * 2f));
            }
            uv[8] = new Vector2(0.5f, 0.5f);
            mesh.uv = uv;
            mesh.RecalculateNormals();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = BaseMat();
            renderer.sortingOrder = inset > 0f ? -5 : -10; // 透明队列按距离排序会翻转（闪烁根因），改用显式层序：描边-10 < 填充-5 < 箭头+10
            return go;
        }

        /// <summary>面板左侧斜切箭头条（WW 招牌元素；蓝=教学 / 橙=正式 / 红=警示），宽=短边 14%</summary>
        public static void AccentStrip(GameObject framedPanel, Color color, float frameFraction = 0.08f)
        {
            var root = framedPanel.transform;
            var panel = root.Find("Panel");
            var size = panel != null
                ? new Vector2(panel.GetComponent<MeshFilter>().sharedMesh.bounds.size.x,
                    panel.GetComponent<MeshFilter>().sharedMesh.bounds.size.y)
                : new Vector2(0.4f, 0.2f);
            var h = Mathf.Max(0.02f, size.y - Mathf.Min(size.x, size.y) * 0.12f);
            var w = Mathf.Max(0.02f, Mathf.Min(size.x, size.y) * 0.14f);
            var skew = h * 0.5f;
            var strip = new GameObject("Accent");
            strip.transform.SetParent(root, false);
            strip.transform.localPosition = new Vector3(
                -size.x * 0.5f + h * 0.06f + w * 0.5f, 0f, -0.001f);
            var mesh = new Mesh { name = "UiThemeChevron" };
            var bl = new Vector3(skew, -h * 0.5f, 0f); // 平行四边形：底边右移
            var br = new Vector3(skew + w, -h * 0.5f, 0f);
            var tl = new Vector3(0f, h * 0.5f, 0f);
            var tr = new Vector3(w, h * 0.5f, 0f);
            mesh.vertices = new[] { bl, br, tl, tr };
            var c32 = (Color32)color;
            mesh.colors32 = new[] { c32, c32, c32, c32 };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 }; // 与 Quad 同绕向（法向 -Z）
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.RecalculateNormals();
            strip.AddComponent<MeshFilter>().sharedMesh = mesh;
            var stripRenderer = strip.AddComponent<MeshRenderer>();
            stripRenderer.sharedMaterial = BaseMat();
            stripRenderer.sortingOrder = 10; // 永远盖在所属面板的描边/填充之上（透明队列距离排序不稳定的修复）
        }

        /// <summary>WW 模板字排版：加粗 + 字距放宽（军规钢印感）。trackingFrac 单位=em（0.1=字宽 10%），TMP 源码 TMP_Text.cs:4597 证实</summary>
        public static void Military(TMPro.TMP_Text text, float trackingFrac = 0.10f)
        {
            text.fontStyle |= TMPro.FontStyles.Bold;
            text.characterSpacing = trackingFrac;
        }

        /// <summary>技术注记小字（如 "// MISSION SELECT"）：暗色小号加宽字距，垫在主标题下</summary>
        public static GameObject Caption(Transform parent, string content, float fontSize, float localZ = -0.002f)
        {
            var go = new GameObject("Caption");
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TMPro.TextMeshPro>();
            tmp.fontSize = fontSize;
            tmp.color = TextDim;
            Military(tmp, 0.22f);
            tmp.text = content;
            tmp.alignment = TMPro.TextAlignmentOptions.Left;
            tmp.rectTransform.sizeDelta = new Vector2(fontSize * content.Length * 1.2f + 0.1f, fontSize * 1.8f);
            tmp.transform.localPosition = new Vector3(0f, 0f, localZ);
            return go;
        }

        /// <summary>UGUI（Canvas ×0.01 惯例）面板加四条米白边框条——子级 Image 不遮内容</summary>
        public static void AddUguiBorder(RectTransform rt, Color color, float thicknessUnits = 0.9f)
        {
            var w = rt.sizeDelta.x;
            var h = rt.sizeDelta.y;
            Bar(rt, "BorderT", color, new Vector2(0f, h * 0.5f - thicknessUnits * 0.5f), new Vector2(w, thicknessUnits));
            Bar(rt, "BorderB", color, new Vector2(0f, -h * 0.5f + thicknessUnits * 0.5f), new Vector2(w, thicknessUnits));
            Bar(rt, "BorderL", color, new Vector2(-w * 0.5f + thicknessUnits * 0.5f, 0f),
                new Vector2(thicknessUnits, h - thicknessUnits * 2f));
            Bar(rt, "BorderR", color, new Vector2(w * 0.5f - thicknessUnits * 0.5f, 0f),
                new Vector2(thicknessUnits, h - thicknessUnits * 2f));
        }

        private static void Bar(RectTransform parent, string name, Color color, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var barRt = (RectTransform)go.transform;
            barRt.sizeDelta = size;
            barRt.anchoredPosition = anchoredPos;
            var image = go.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        /// <summary>UGUI 面板底（炭黑）——调用方先加底再加文字，最后 AddUguiBorder 收边</summary>
        public static UnityEngine.UI.Image UguiPanel(RectTransform rt)
        {
            var image = rt.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = PanelBg;
            image.raycastTarget = false;
            return image;
        }
    }
}
