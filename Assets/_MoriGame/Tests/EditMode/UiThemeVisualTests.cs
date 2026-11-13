using MoriGame.Ui;
using NUnit.Framework;
using UnityEngine;

namespace MoriGame.Tests
{
    /// <summary>WW Polygon 主题视觉组件契约（2026-11-12 重做：切角面板/箭头条/渐变）</summary>
    public class UiThemeVisualTests
    {
        [Test]
        public void ChamferQuad_BuildsStrokeAndFillOctagons()
        {
            var root = UiTheme.ChamferQuad(null, "T", UiTheme.PanelBg, new Vector2(0.9f, 0.3f), 0f);
            var strokeT = root.transform.Find("Stroke");
            var fillT = root.transform.Find("Panel");
            Assert.NotNull(strokeT);
            Assert.NotNull(fillT);
            var stroke = strokeT.GetComponent<MeshFilter>().sharedMesh;
            var fill = fillT.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(9, stroke.vertexCount); // 8 环点 + 中心
            Assert.AreEqual(9, fill.vertexCount);
            Assert.Less(fill.bounds.size.x, stroke.bounds.size.x); // 填充层内缩于描边层
            var fillVerts = fill.vertices;
            var topBright = fill.colors32[1].r; // 顶环点 (hw, hh-ch)
            var botBright = fill.colors32[0].r; // 底环点 (hw, -(hh-ch))
            Assert.Greater(topBright, botBright); // 顶亮底暗渐变方向
            Object.DestroyImmediate(root);
        }

        [Test]
        public void AccentStrip_BuildsSkewedChevron()
        {
            var panel = UiTheme.FramedQuad(null, "P", new Vector2(0.5f, 0.2f), UiTheme.PanelBg);
            UiTheme.AccentStrip(panel, UiTheme.AccentRed);
            var accentT = panel.transform.Find("Accent");
            Assert.NotNull(accentT);
            var accent = accentT.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(4, accent.vertexCount); // 平行四边形
            var v = accent.vertices;
            Assert.Greater(v[0].x, v[2].x); // 底边相对顶边右移（斜切）
            Object.DestroyImmediate(panel);
        }
    }
}
