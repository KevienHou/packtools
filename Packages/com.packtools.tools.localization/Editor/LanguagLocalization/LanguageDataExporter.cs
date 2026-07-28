using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PackTools.LanguagLocalization
{


    internal static class LanguageDataExporter
    {
        private const int PreviewMaxDimension = 512;

        internal static string OutputPath =>
            Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? Environment.CurrentDirectory,
                "LanguageData",
                "LanguageData.json"
            );

        internal static bool ExportPreviewDataWithLog()
        {
            if (!TryExportPreviewData(out LanguageExportReport report))
            {
                Debug.LogError(report.Message);
                return false;
            }

            Debug.Log(
                $"多语言预览数据已保存：{report.OutputPath}\n"
                    + $"语言项 {report.ItemCount} 个，图片 {report.ImageCount} 张，"
                    + $"优化 {report.OptimizedImageCount} 张，"
                    + $"缺失/无效图片 {report.MissingImageCount} 个。\n"
                    + $"图片数据：{FormatByteSize(report.OriginalImageBytes)} → "
                    + $"{FormatByteSize(report.PreviewImageBytes)}，"
                    + $"JSON：{FormatByteSize(report.JsonBytes)}。"
            );

            if (report.CompressionFallbackCount > 0)
            {
                Debug.LogWarning(
                    $"{report.CompressionFallbackCount} 张图片无法生成缩略图，"
                        + "已回退为原始图片数据；请查看上方警告。"
                );
            }

            return true;
        }

        [MenuItem("Tools/Language Localization/导出预览数据")]
        private static void ExportFromMainMenu()
        {
            ExportPreviewDataWithLog();
        }

        internal static bool TryExportPreviewData(out LanguageExportReport report)
        {
            report = new LanguageExportReport();

            try
            {
                LanguageSwitcher[] switchers = FindLoadedSceneSwitchers();
                if (switchers.Length == 0)
                {
                    report.Message = "当前已加载场景中没有找到 LanguageSwitcher。";
                    return false;
                }

                LanguageData data = new LanguageData();
                for (int switcherIndex = 0; switcherIndex < switchers.Length; switcherIndex++)
                {
                    AppendSwitcherData(switchers[switcherIndex], data, report);
                }

                string outputPath = OutputPath;
                string outputDirectory = Path.GetDirectoryName(outputPath);
                if (string.IsNullOrEmpty(outputDirectory))
                {
                    report.Message = "无法确定 LanguageData.json 的输出目录。";
                    return false;
                }

                Directory.CreateDirectory(outputDirectory);
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(outputPath, json, new UTF8Encoding(false));

                report.OutputPath = outputPath;
                report.JsonBytes = Encoding.UTF8.GetByteCount(json);
                report.Message = "保存成功。";
                return true;
            }
            catch (Exception exception)
            {
                report.Message = $"保存多语言预览数据失败：{exception}";
                return false;
            }
        }

        private static void AppendSwitcherData(
            LanguageSwitcher switcher,
            LanguageData data,
            LanguageExportReport report
        )
        {
            if (switcher.languageItems == null)
            {
                return;
            }

            for (int itemIndex = 0; itemIndex < switcher.languageItems.Count; itemIndex++)
            {
                LanguageItem item = switcher.languageItems[itemIndex];
                if (item == null)
                {
                    continue;
                }

                LanguageData.LanguageSpriteData spriteData =
                    new LanguageData.LanguageSpriteData
                    {
                        RootName = string.IsNullOrWhiteSpace(item.itemName)
                            ? $"Unnamed_{itemIndex}"
                            : item.itemName,
                    };

                if (item.config != null)
                {
                    for (int configIndex = 0; configIndex < item.config.Count; configIndex++)
                    {
                        LanguageItemConfig config = item.config[configIndex];
                        if (config == null)
                        {
                            report.MissingImageCount++;
                            continue;
                        }

                        LanguageData.LanguageSprite languageSprite =
                            new LanguageData.LanguageSprite
                            {
                                LanguageName = config.code ?? string.Empty,
                                LanguageTextureBase64 = TryReadSpriteBase64(config, report),
                            };

                        // 每个语言配置只加入一次，避免旧实现中有图配置重复写入。
                        spriteData.languageSprites.Add(languageSprite);
                    }
                }

                data.languageSpriteDatas.Add(spriteData);
                report.ItemCount++;
            }
        }

        private static string TryReadSpriteBase64(
            LanguageItemConfig config,
            LanguageExportReport report
        )
        {
            if (config.img == null)
            {
                report.MissingImageCount++;
                return string.Empty;
            }

            Image image = config.img.GetComponent<Image>();
            if (image == null || image.sprite == null)
            {
                report.MissingImageCount++;
                return string.Empty;
            }

            string assetPath = AssetDatabase.GetAssetPath(image.sprite);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                report.MissingImageCount++;
                return string.Empty;
            }

            string projectRoot =
                Directory.GetParent(Application.dataPath)?.FullName ?? Environment.CurrentDirectory;
            string fullPath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!File.Exists(fullPath))
            {
                report.MissingImageCount++;
                return string.Empty;
            }

            byte[] sourceBytes = File.ReadAllBytes(fullPath);
            byte[] previewBytes = sourceBytes;
            report.OriginalImageBytes += sourceBytes.LongLength;

            try
            {
                byte[] optimizedBytes = CreateOptimizedPreviewPng(image.sprite, assetPath);
                if (
                    optimizedBytes != null
                    && optimizedBytes.Length > 0
                    && optimizedBytes.Length < sourceBytes.Length
                )
                {
                    previewBytes = optimizedBytes;
                    report.OptimizedImageCount++;
                }
            }
            catch (Exception exception)
            {
                report.CompressionFallbackCount++;
                Debug.LogWarning(
                    $"无法压缩多语言预览图，已使用原图：{assetPath}\n{exception.Message}"
                );
            }

            report.ImageCount++;
            report.PreviewImageBytes += previewBytes.LongLength;
            return Convert.ToBase64String(previewBytes);
        }

        private static byte[] CreateOptimizedPreviewPng(Sprite sprite, string assetPath)
        {
            Texture2D sourceTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (sourceTexture == null)
            {
                throw new InvalidOperationException($"无法加载源纹理：{assetPath}");
            }

            Rect sourceRect = sprite.rect;
            if (
                sourceRect.width <= 0f
                || sourceRect.height <= 0f
                || sourceTexture.width <= 0
                || sourceTexture.height <= 0
            )
            {
                throw new InvalidOperationException($"Sprite 尺寸无效：{assetPath}");
            }

            float resizeScale = Mathf.Min(
                1f,
                (float)PreviewMaxDimension / Mathf.Max(sourceRect.width, sourceRect.height)
            );
            int targetWidth = Mathf.Max(1, Mathf.RoundToInt(sourceRect.width * resizeScale));
            int targetHeight = Mathf.Max(1, Mathf.RoundToInt(sourceRect.height * resizeScale));

            RenderTexture previousTarget = RenderTexture.active;
            RenderTexture previewTarget = null;
            Texture2D previewTexture = null;

            try
            {
                previewTarget = RenderTexture.GetTemporary(
                    targetWidth,
                    targetHeight,
                    0,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB
                );
                previewTarget.filterMode = FilterMode.Bilinear;

                Vector2 sourceScale =
                    new Vector2(
                        sourceRect.width / sourceTexture.width,
                        sourceRect.height / sourceTexture.height
                    );
                Vector2 sourceOffset =
                    new Vector2(
                        sourceRect.x / sourceTexture.width,
                        sourceRect.y / sourceTexture.height
                    );

                Graphics.Blit(sourceTexture, previewTarget, sourceScale, sourceOffset);
                RenderTexture.active = previewTarget;

                previewTexture = new Texture2D(
                    targetWidth,
                    targetHeight,
                    TextureFormat.RGBA32,
                    false,
                    false
                );
                previewTexture.ReadPixels(
                    new Rect(0f, 0f, targetWidth, targetHeight),
                    0,
                    0,
                    false
                );
                previewTexture.Apply(false, false);

                byte[] encodedBytes = previewTexture.EncodeToPNG();
                if (encodedBytes == null || encodedBytes.Length == 0)
                {
                    throw new InvalidOperationException($"PNG 编码结果为空：{assetPath}");
                }

                return encodedBytes;
            }
            finally
            {
                RenderTexture.active = previousTarget;

                if (previewTarget != null)
                {
                    RenderTexture.ReleaseTemporary(previewTarget);
                }

                if (previewTexture != null)
                {
                    UnityEngine.Object.DestroyImmediate(previewTexture);
                }
            }
        }

        private static LanguageSwitcher[] FindLoadedSceneSwitchers()
        {
            LanguageSwitcher[] allSwitchers = Resources.FindObjectsOfTypeAll<LanguageSwitcher>();
            List<LanguageSwitcher> sceneSwitchers = new List<LanguageSwitcher>(allSwitchers.Length);

            for (int i = 0; i < allSwitchers.Length; i++)
            {
                LanguageSwitcher switcher = allSwitchers[i];
                if (
                    switcher != null
                    && !EditorUtility.IsPersistent(switcher)
                    && switcher.gameObject.scene.IsValid()
                    && switcher.gameObject.scene.isLoaded
                )
                {
                    sceneSwitchers.Add(switcher);
                }
            }

            sceneSwitchers.Sort(
                (left, right) =>
                    string.CompareOrdinal(
                        GetHierarchyPath(left.transform),
                        GetHierarchyPath(right.transform)
                    )
            );
            return sceneSwitchers.ToArray();
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return transform.gameObject.scene.path + ":" + path;
        }

        private static string FormatByteSize(long byteCount)
        {
            const double Kilobyte = 1024d;
            const double Megabyte = Kilobyte * 1024d;

            if (byteCount >= Megabyte)
            {
                return $"{byteCount / Megabyte:0.00} MB";
            }

            if (byteCount >= Kilobyte)
            {
                return $"{byteCount / Kilobyte:0.00} KB";
            }

            return $"{byteCount} B";
        }
    }

    [Serializable]
    internal sealed class LanguageData
    {
        public List<LanguageSpriteData> languageSpriteDatas = new List<LanguageSpriteData>();

        [Serializable]
        internal sealed class LanguageSpriteData
        {
            public string RootName;
            public List<LanguageSprite> languageSprites = new List<LanguageSprite>();
        }

        [Serializable]
        internal sealed class LanguageSprite
        {
            public string LanguageName;
            public string LanguageTextureBase64;
        }
    }

    internal sealed class LanguageExportReport
    {
        internal string OutputPath;
        internal string Message;
        internal int ItemCount;
        internal int ImageCount;
        internal int OptimizedImageCount;
        internal int MissingImageCount;
        internal int CompressionFallbackCount;
        internal long OriginalImageBytes;
        internal long PreviewImageBytes;
        internal long JsonBytes;
    }

}