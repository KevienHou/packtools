using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace PackTools.LanguagLocalization
{
    using LanguageLocalization.EditorCore;

    [InitializeOnLoad]
    internal static class LunaLanguageBuildWatcher
    {
        private static readonly TimeSpan StableDelay = TimeSpan.FromSeconds(1);
        private static readonly ConcurrentDictionary<string, DateTime> PendingFiles =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, FileSignature> LastProcessedFiles =
            new Dictionary<string, FileSignature>(StringComparer.OrdinalIgnoreCase);

        private static FileSystemWatcher watcher;
        private static bool isProcessing;

        static LunaLanguageBuildWatcher()
        {
            EditorApplication.update += ProcessPendingFiles;
            EditorApplication.quitting += Dispose;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            StartWatcher();
        }

        private static void StartWatcher()
        {
            string watchPath = Path.Combine(
                Directory.GetParent(Application.dataPath)?.FullName ?? Environment.CurrentDirectory,
                "LunaTemp",
                "stage4",
                "create-hub"
            );

            try
            {
                Directory.CreateDirectory(watchPath);
                watcher = new FileSystemWatcher(watchPath, "*.zip")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter =
                        NotifyFilters.CreationTime
                        | NotifyFilters.FileName
                        | NotifyFilters.LastWrite
                        | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                watcher.Created += OnZipChanged;
                watcher.Changed += OnZipChanged;
                watcher.Renamed += OnZipRenamed;
                Debug.Log($"开始监视 Luna ZIP 输出：{watchPath}");
            }
            catch (Exception exception)
            {
                Debug.LogError($"启动 Luna ZIP 监视失败：{exception.Message}");
                Dispose();
            }
        }

        private static void OnZipChanged(object sender, FileSystemEventArgs args)
        {
            PendingFiles[args.FullPath] = DateTime.UtcNow;
        }

        private static void OnZipRenamed(object sender, RenamedEventArgs args)
        {
            PendingFiles[args.FullPath] = DateTime.UtcNow;
        }

        private static void ProcessPendingFiles()
        {
            if (isProcessing || PendingFiles.IsEmpty)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            foreach (KeyValuePair<string, DateTime> pending in PendingFiles)
            {
                if (now - pending.Value < StableDelay)
                {
                    continue;
                }

                if (!PendingFiles.TryRemove(pending.Key, out _))
                {
                    continue;
                }

                if (!TryGetStableSignature(pending.Key, out FileSignature signature))
                {
                    PendingFiles[pending.Key] = DateTime.UtcNow;
                    continue;
                }

                if (
                    LastProcessedFiles.TryGetValue(pending.Key, out FileSignature previous)
                    && previous.Equals(signature)
                )
                {
                    continue;
                }

                ProcessZip(pending.Key);
                if (TryGetStableSignature(pending.Key, out FileSignature processedSignature))
                {
                    LastProcessedFiles[pending.Key] = processedSignature;
                }
                break;
            }
        }

        private static void ProcessZip(string zipPath)
        {
            isProcessing = true;
            try
            {
                if (!LanguageDataExporter.TryExportPreviewData(out LanguageExportReport report))
                {
                    Debug.LogError(report.Message);
                    return;
                }

                ZipInjectionStatus status = ZipLanguageDataInjector.EmbedLanguagePreview(
                    zipPath,
                    report.OutputPath,
                    out string message
                );

                switch (status)
                {
                    case ZipInjectionStatus.Injected:
                        Debug.Log(message);
                        break;
                    case ZipInjectionStatus.AlreadyContainsLanguageData:
                    case ZipInjectionStatus.ChannelPackageSkipped:
                        Debug.Log(message);
                        break;
                    default:
                        Debug.LogError(message);
                        break;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"处理 Luna ZIP 失败：{exception}");
            }
            finally
            {
                isProcessing = false;
            }
        }

        private static bool TryGetStableSignature(string path, out FileSignature signature)
        {
            signature = default;
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                using (
                    FileStream stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.None
                    )
                )
                {
                    if (stream.Length == 0)
                    {
                        return false;
                    }
                }

                FileInfo file = new FileInfo(path);
                signature = new FileSignature(file.Length, file.LastWriteTimeUtc.Ticks);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        private static void Dispose()
        {
            EditorApplication.update -= ProcessPendingFiles;
            EditorApplication.quitting -= Dispose;
            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;

            if (watcher == null)
            {
                return;
            }

            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnZipChanged;
            watcher.Changed -= OnZipChanged;
            watcher.Renamed -= OnZipRenamed;
            watcher.Dispose();
            watcher = null;
        }

        private readonly struct FileSignature : IEquatable<FileSignature>
        {
            private readonly long length;
            private readonly long lastWriteTicks;

            internal FileSignature(long length, long lastWriteTicks)
            {
                this.length = length;
                this.lastWriteTicks = lastWriteTicks;
            }

            public bool Equals(FileSignature other)
            {
                return length == other.length && lastWriteTicks == other.lastWriteTicks;
            }
        }
    }
}