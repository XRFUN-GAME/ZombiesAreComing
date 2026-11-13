using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ByteDance.PICO.XR;
using MoriGame.Ui;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MoriGame.Editor
{
    public static class BrandArtSetup
    {
        public const string SourcePath = "Assets/Resources/Image/xrfun01.png";
        public const string BrandRootName = "BrandIdentity";
        public const string LogoPath = BrandDirectory + "/BrandLogo.png";
        public const string SplashPath = BrandDirectory + "/PicoSplash_1024.png";
        public const string MaterialPath = BrandDirectory + "/BrandLogo.mat";

        private const string BrandDirectory = "Assets/_MoriGame/Art/Brand";
        private const string PicoSettingsPath = "Assets/XR/Settings/PXR_Settings.asset";
        private static readonly string[] ScenePaths =
        {
            "Assets/_MoriGame/Scenes/StartScene.unity",
            "Assets/_MoriGame/Scenes/Tutorial.unity",
            "Assets/_MoriGame/Scenes/MoriMain.unity"
        };
        private static readonly Color32 Background = new Color32(18, 22, 24, 255);

        [MenuItem("MoriGame/品牌/生成 xrfun01 图标、启动图与场景标识")]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                throw new InvalidOperationException("请先退出 Play，再生成品牌资源");
            }

            EditorSceneManager.SaveOpenScenes();
            var backupDirectory = BackupInputs();
            Directory.CreateDirectory(BrandDirectory);
            Directory.CreateDirectory(BrandDirectory + "/Icons");

            using (var source = LoadCroppedSource(out var crop))
            {
                SaveTexture(source.Texture, LogoPath);
                using (var splash = Composite(source.Texture, 1024, 0.86f, true, false))
                {
                    SaveTexture(splash.Texture, SplashPath);
                }
                ConfigureAndroidIcons(source.Texture);
                ConfigurePicoSplash();
                CreateLogoMaterial();
                ApplySceneIdentities();
                WriteReport(backupDirectory, source.Texture, crop);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[MoriGame] xrfun01 品牌资源、PICO 启动图、Android 图标与三场景标识完成");
        }

        private sealed class TextureLease : IDisposable
        {
            public Texture2D Texture { get; }
            public TextureLease(Texture2D texture) => Texture = texture;
            public void Dispose()
            {
                if (Texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(Texture);
                }
            }
        }

        private static TextureLease LoadCroppedSource(out RectInt crop)
        {
            var bytes = File.ReadAllBytes(SourcePath);
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(source, bytes, false))
                {
                    throw new InvalidOperationException("无法读取 xrfun01.png");
                }
                var pixels = source.GetPixels32();
                var minX = source.width;
                var minY = source.height;
                var maxX = -1;
                var maxY = -1;
                for (var y = 0; y < source.height; y++)
                {
                    for (var x = 0; x < source.width; x++)
                    {
                        if (pixels[y * source.width + x].a <= 8)
                        {
                            continue;
                        }
                        minX = Mathf.Min(minX, x);
                        minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x);
                        maxY = Mathf.Max(maxY, y);
                    }
                }
                if (maxX < minX || maxY < minY)
                {
                    throw new InvalidOperationException("xrfun01.png 没有可见像素");
                }

                crop = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
                var croppedPixels = new Color32[crop.width * crop.height];
                for (var y = 0; y < crop.height; y++)
                {
                    Array.Copy(pixels, (crop.y + y) * source.width + crop.x,
                        croppedPixels, y * crop.width, crop.width);
                }
                var cropped = new Texture2D(crop.width, crop.height, TextureFormat.RGBA32, false);
                cropped.SetPixels32(croppedPixels);
                cropped.Apply();
                return new TextureLease(cropped);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private static TextureLease Resize(Texture2D source, int maxWidth, int maxHeight)
        {
            var scale = Mathf.Min(maxWidth / (float)source.width, maxHeight / (float)source.height);
            var width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            var height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));
            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
                output.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                output.Apply();
                return new TextureLease(output);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        private static TextureLease Composite(Texture2D logo, int size, float contentFraction,
            bool opaque, bool circular)
        {
            var contentSize = Mathf.Max(1, Mathf.RoundToInt(size * contentFraction));
            using (var resized = Resize(logo, contentSize, contentSize))
            {
                var pixels = new Color32[size * size];
                var center = (size - 1) * 0.5f;
                var radius = size * 0.5f;
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var dx = x - center;
                        var dy = y - center;
                        var visible = !circular || dx * dx + dy * dy <= radius * radius;
                        pixels[y * size + x] = opaque && visible ? Background : new Color32(0, 0, 0, 0);
                    }
                }

                var offsetX = (size - resized.Texture.width) / 2;
                var offsetY = (size - resized.Texture.height) / 2;
                var logoPixels = resized.Texture.GetPixels32();
                for (var y = 0; y < resized.Texture.height; y++)
                {
                    for (var x = 0; x < resized.Texture.width; x++)
                    {
                        var index = (offsetY + y) * size + offsetX + x;
                        pixels[index] = Blend(logoPixels[y * resized.Texture.width + x], pixels[index]);
                    }
                }
                var output = new Texture2D(size, size, TextureFormat.RGBA32, false);
                output.SetPixels32(pixels);
                output.Apply();
                return new TextureLease(output);
            }
        }

        private static Color32 Blend(Color32 source, Color32 destination)
        {
            var sourceAlpha = source.a / 255f;
            var destinationAlpha = destination.a / 255f;
            var outputAlpha = sourceAlpha + destinationAlpha * (1f - sourceAlpha);
            if (outputAlpha <= 0f)
            {
                return new Color32(0, 0, 0, 0);
            }
            byte Channel(byte s, byte d) => (byte)Mathf.Clamp(Mathf.RoundToInt(
                (s * sourceAlpha + d * destinationAlpha * (1f - sourceAlpha)) / outputAlpha), 0, 255);
            return new Color32(Channel(source.r, destination.r), Channel(source.g, destination.g),
                Channel(source.b, destination.b), (byte)Mathf.RoundToInt(outputAlpha * 255f));
        }

        private static void ConfigureAndroidIcons(Texture2D logo)
        {
            ConfigureIconKind(logo, AndroidPlatformIconKind.Legacy, "Legacy");
            ConfigureIconKind(logo, AndroidPlatformIconKind.Round, "Round");
            ConfigureIconKind(logo, AndroidPlatformIconKind.Adaptive, "Adaptive");
        }

        private static void ConfigureIconKind(Texture2D logo, PlatformIconKind kind, string prefix)
        {
            var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            foreach (var icon in icons)
            {
                if (kind.Equals(AndroidPlatformIconKind.Adaptive))
                {
                    if (icon.layerCount < 2)
                    {
                        throw new InvalidOperationException("Android Adaptive Icon 缺少背景/前景层");
                    }
                    using (var background = SolidTexture(icon.width, icon.height))
                    {
                        var path = IconPath(prefix, icon.width, "Background");
                        SaveTexture(background.Texture, path);
                        icon.SetTexture(LoadTexture(path), 0);
                    }
                    using (var foreground = Composite(logo, icon.width, 0.68f, false, false))
                    {
                        var path = IconPath(prefix, icon.width, "Foreground");
                        SaveTexture(foreground.Texture, path);
                        icon.SetTexture(LoadTexture(path), 1);
                    }
                }
                else
                {
                    var circular = kind.Equals(AndroidPlatformIconKind.Round);
                    using (var texture = Composite(logo, icon.width, circular ? 0.70f : 0.82f,
                               true, circular))
                    {
                        var path = IconPath(prefix, icon.width, "Icon");
                        SaveTexture(texture.Texture, path);
                        icon.SetTexture(LoadTexture(path), 0);
                    }
                }
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
        }

        private static string IconPath(string prefix, int size, string layer)
        {
            return $"{BrandDirectory}/Icons/Android_{prefix}_{size}_{layer}.png";
        }

        private static TextureLease SolidTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Background;
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return new TextureLease(texture);
        }

        private static void SaveTexture(Texture2D texture, string path)
        {
            File.WriteAllBytes(path, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.SaveAndReimport();
            }
        }

        private static Texture2D LoadTexture(string path)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                throw new InvalidOperationException("品牌图片导入失败：" + path);
            }
            return texture;
        }

        private static void ConfigurePicoSplash()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PXR_Settings>(PicoSettingsPath);
            if (settings == null)
            {
                throw new InvalidOperationException("找不到 PXR_Settings");
            }
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("systemSplashScreen").objectReferenceValue = LoadTexture(SplashPath);
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static void CreateLogoMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = Shader.Find("Sprites/Default");
            material.mainTexture = LoadTexture(LogoPath);
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
        }

        private static void ApplySceneIdentities()
        {
            var originalPath = SceneManager.GetActiveScene().path;
            try
            {
                foreach (var scenePath in ScenePaths)
                {
                    var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                    if (HasBrandIdentity(scene))
                    {
                        continue;
                    }

                    var root = new GameObject(BrandRootName);
                    SceneManager.MoveGameObjectToScene(root, scene);
                    var anchor = root.AddComponent<WorldUiAnchor>();
                    anchor.panelId = WorldUiPanels.BrandLogo;
                    anchor.previewContent = null;

                    var logo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    logo.name = "Logo";
                    UnityEngine.Object.DestroyImmediate(logo.GetComponent<Collider>());
                    logo.transform.SetParent(root.transform, false);
                    var texture = LoadTexture(LogoPath);
                    logo.transform.localScale = new Vector3(1.1f,
                        1.1f * texture.height / texture.width, 1f);
                    var renderer = logo.GetComponent<MeshRenderer>();
                    renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                    renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                    if (scene.name == "StartScene")
                    {
                        root.transform.SetPositionAndRotation(new Vector3(0f, 2.15f, 1.8f),
                            Quaternion.identity);
                    }
                    else if (!UiTheme.PlaceFixedPanel(root.transform, -60f, 2.4f, 1.65f))
                    {
                        root.transform.SetPositionAndRotation(new Vector3(-1.8f, 2.15f, 1.6f),
                            Quaternion.Euler(0f, -48f, 0f));
                    }

                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(originalPath) &&
                    SceneManager.GetActiveScene().path != originalPath)
                {
                    EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
                }
            }
        }

        private static bool HasBrandIdentity(Scene scene)
        {
            var found = false;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != BrandRootName)
                {
                    continue;
                }
                if (found)
                {
                    throw new InvalidOperationException(scene.name + " 存在重复 BrandIdentity");
                }
                found = true;
            }
            return found;
        }

        private static string BackupInputs()
        {
            var directory = Path.Combine("Builds", "SceneBackups",
                "brand-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(directory);
            foreach (var path in ScenePaths)
            {
                File.Copy(path, Path.Combine(directory, Path.GetFileName(path)), true);
            }
            File.Copy("ProjectSettings/ProjectSettings.asset",
                Path.Combine(directory, "ProjectSettings.asset"), true);
            File.Copy(PicoSettingsPath, Path.Combine(directory, "PXR_Settings.asset"), true);
            return directory.Replace('\\', '/');
        }

        private static void WriteReport(string backupDirectory, Texture2D logo, RectInt crop)
        {
            var report = new StringBuilder();
            report.AppendLine("source=" + SourcePath);
            report.AppendLine($"crop={crop.x},{crop.y},{crop.width},{crop.height}");
            report.AppendLine($"sceneLogo={logo.width}x{logo.height}");
            report.AppendLine("picoSplash=1024x1024");
            report.AppendLine("androidIcons=Legacy/Round/Adaptive");
            report.AppendLine("backup=" + backupDirectory);
            foreach (var scenePath in ScenePaths)
            {
                report.AppendLine("scene=" + scenePath);
            }
            File.WriteAllText("Builds/brand-assets-report.txt", report.ToString());
        }
    }
}
