using System.IO;
using MoriGame.Ui;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace MoriGame.Tests
{
    public class BrandAssetsTests
    {
        [Test]
        public void BrandAssets_PicoSplashAndAndroidIconsAreConfigured()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(
                "Assets/XR/Settings/PXR_Settings.asset");
            Assert.NotNull(settings);
            var serialized = new SerializedObject(settings);
            var splash = serialized.FindProperty("systemSplashScreen").objectReferenceValue as Texture2D;
            Assert.NotNull(splash);
            Assert.LessOrEqual(splash.width, 1024);
            Assert.LessOrEqual(splash.height, 1024);
            Assert.AreEqual(".png", Path.GetExtension(AssetDatabase.GetAssetPath(splash)).ToLowerInvariant());

            AssertIcons(AndroidPlatformIconKind.Legacy, 1);
            AssertIcons(AndroidPlatformIconKind.Round, 1);
            AssertIcons(AndroidPlatformIconKind.Adaptive, 2);
        }

        [Test]
        public void BrandAssets_ThreeScenesContainOnePersistentIdentity()
        {
            foreach (var name in new[] { "StartScene", "Tutorial", "MoriMain" })
            {
                var yaml = File.ReadAllText($"Assets/_MoriGame/Scenes/{name}.unity");
                Assert.AreEqual(1, Count(yaml, "m_Name: BrandIdentity"), name);
                Assert.AreEqual(1, Count(yaml, "panelId: " + WorldUiPanels.BrandLogo), name);
            }
        }

        private static void AssertIcons(PlatformIconKind kind, int layers)
        {
            var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            Assert.IsNotEmpty(icons);
            foreach (var icon in icons)
            {
                Assert.GreaterOrEqual(icon.layerCount, layers);
                for (var layer = 0; layer < layers; layer++)
                {
                    var texture = icon.GetTexture(layer);
                    Assert.NotNull(texture, $"{kind} {icon.width} layer {layer}");
                    Assert.AreEqual(icon.width, texture.width);
                    Assert.AreEqual(icon.height, texture.height);
                }
            }
        }

        private static int Count(string text, string value)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(value, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += value.Length;
            }
            return count;
        }
    }
}
