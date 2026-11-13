using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MoriGame.Editor
{
    /// <summary>
    /// 中文字体资产生成（TMP 迁移 2026-11-02）：
    /// 源 = 系统微软雅黑 msyh.ttc face 0 → 静态 SDF 字库（采样 40pt · SDFAA · 2048 多图集）。
    /// 字符集 = ASCII 可打印 + 全角标点符号 + 全部 UI 字符串字面量 + 配置资产文本
    /// + **GB2312 一级字库 3755 常用字**（项目语料仅 ~900 独有字，覆盖不了未来文案）
    /// + docs 全文按频率补足，上限 4000 字（项目文案优先不裁）。首次由 MoriSceneSetup.CreateText 自动触发。
    /// </summary>
    public static class ChineseFontGenerator
    {
        private const string AssetPath = "Assets/_MoriGame/Art/Fonts/MoriZhCN_SDF.asset";
        private const string OsFontPath = @"C:\Windows\Fonts\msyh.ttc";
        private const int MaxCharacters = 4000;
        private const int SamplingPointSize = 40;

        private static readonly char[] ExtraSymbols =
            "，。！？：；、·—…（）《》【】％＋－×÷℃㎡↑↓←→〇".ToCharArray();

        [MenuItem("MoriGame/生成中文字体资产")]
        public static void Generate()
        {
            TMP_PackageResourceImporter.ImportResources(true, false, false); // TMP Essentials：TMP Settings / 着色器 / 默认资源

            var charset = BuildCharset();
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                OsFontPath, 0, SamplingPointSize, 5, GlyphRenderMode.SDFAA, 2048, 2048);
            if (fontAsset == null)
            {
                Debug.LogError($"[MoriGame] 无法加载源字体 {OsFontPath}（msyh.ttc 不存在？换思源黑体路径需改 OsFontPath）");
                return;
            }
            fontAsset.name = "MoriZhCN_SDF";
            fontAsset.TryAddCharacters(charset, out var missing);
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static; // 静态字库：运行时不需要源字体文件

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            if (File.Exists(AssetPath))
            {
                AssetDatabase.DeleteAsset(AssetPath); // 清掉旧版字库（子资产/atlas 残留）
            }
            AssetDatabase.CreateAsset(fontAsset, AssetPath);
            foreach (var tex in fontAsset.atlasTextures)
            {
                if (tex != null)
                {
                    AssetDatabase.AddObjectToAsset(tex, fontAsset);
                }
            }
            if (fontAsset.material != null)
            {
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[MoriGame] 中文字体资产完成：{charset.Length} 字 / 缺失 {missing?.Length ?? 0} / " +
                      $"atlas {fontAsset.atlasTextureCount} 页（采样 {SamplingPointSize}pt）→ {AssetPath}");
        }

        /// <summary>供 MoriSceneSetup 调用：已有则加载，缺失则生成</summary>
        public static TMP_FontAsset EnsureFontAsset()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            if (asset == null)
            {
                Generate();
                asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            }
            return asset;
        }

        private static string BuildCharset()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var required = new HashSet<char>();
            for (var c = 0x20; c <= 0x7E; c++)
            {
                required.Add((char)c); // ASCII 可打印（数字/字母/符号）
            }
            required.UnionWith(ExtraSymbols);
            foreach (var file in Directory.GetFiles(
                         Path.Combine(projectRoot, "Assets/_MoriGame"), "*.cs", SearchOption.AllDirectories))
            {
                required.UnionWith(ExtractStringLiterals(File.ReadAllText(file)));
            }
            foreach (var file in Directory.GetFiles(
                         Path.Combine(projectRoot, "Assets/_MoriGame/Configs"), "*.asset", SearchOption.AllDirectories))
            {
                required.UnionWith(File.ReadAllText(file)); // displayName / notes 等文案来源
            }

            // GB2312 一级字库（3755 常用字，按拼音序）：项目语料独有字不足千，必须引入标准常用表
            required.UnionWith(Gb2312Level1Chars());

            // docs 全文按字频补足至 4000 上限（覆盖游戏专有词汇：武器/僵尸/卡片/道具名等）
            var docFreq = new Dictionary<char, int>();
            foreach (var file in Directory.GetFiles(Path.Combine(projectRoot, "docs"), "*.md"))
            {
                foreach (var ch in File.ReadAllText(file))
                {
                    if (ch < 0x2E80 && !char.IsLetterOrDigit(ch))
                    {
                        continue; // 跳过 ASCII 控制与空白，只统计 CJK 与字母数字
                    }
                    docFreq[ch] = docFreq.TryGetValue(ch, out var n) ? n + 1 : 1;
                }
            }
            foreach (var kv in docFreq.OrderByDescending(k => k.Value).Select(k => k.Key))
            {
                if (required.Count >= MaxCharacters)
                {
                    break;
                }
                required.Add(kv);
            }
            if (required.Count > MaxCharacters)
            {
                Debug.LogWarning($"[MoriGame] 字符集已超 {MaxCharacters} 上限（{required.Count}），截断为高频优先");
            }
            return new string(required.ToArray());
        }

        /// <summary>GB2312 一级字库（16-55 区，0xB0A1-0xD7F9，3755 个最常用汉字，程序化解码无需字表文件）</summary>
        private static IEnumerable<char> Gb2312Level1Chars()
        {
            var encoding = Encoding.GetEncoding("GB2312");
            for (var hi = 0xB0; hi <= 0xD7; hi++)
            {
                for (var lo = 0xA1; lo <= 0xFE; lo++)
                {
                    var decoded = encoding.GetString(new[] { (byte)hi, (byte)lo });
                    if (decoded.Length == 1 && decoded[0] >= 0x4E00 && decoded[0] <= 0x9FFF)
                    {
                        yield return decoded[0]; // 只收 CJK 基本区汉字，过滤解码失败占位符
                    }
                }
            }
        }

        private static string ExtractStringLiterals(string code)
        {
            var joined = string.Concat(
                Regex.Matches(code, "\"(?:[^\"\\\\]|\\\\.)*\"").Cast<Match>().Select(m => m.Value));
            return joined;
        }
    }
}
