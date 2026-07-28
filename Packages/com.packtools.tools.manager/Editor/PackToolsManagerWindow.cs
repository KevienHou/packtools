using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using StatusCode = UnityEditor.PackageManager.StatusCode;
using Client = UnityEditor.PackageManager.Client;

namespace PackTools.Manager
{
    /// <summary>
    /// 已知工具定义。新增工具时在此列表中添加条目。
    /// </summary>
    internal sealed class ToolDefinition
    {
        public string PackageName;
        public string DisplayName;
        public string Description;
        public string RepoPath; // 仓库内子路径，如 /Packages/com.packtools.tools.localization
    }

    /// <summary>
    /// PackTools 管理器窗口。
    /// 安装、更新、卸载 PackTools 工具，并提供快捷入口。
    /// </summary>
    public sealed class PackToolsManagerWindow : EditorWindow
    {
        private const string MenuPath = "Tools/PackTools 管理器";
        private const string PackagePrefix = "com.packtools.tools.";
        private const float WindowMinWidth = 480f;
        private const float WindowMinHeight = 360f;
        private const string DefaultGitURL = "https://github.com/KevienHou/Packtools.git";
        private const string GitURLPrefKey = "PackTools.GitURL";

        // ── 已知工具注册表（新增工具时在此添加）──
        private static readonly ToolDefinition[] KnownTools =
        {
            new ToolDefinition
            {
                PackageName = "com.packtools.tools.localization",
                DisplayName = "多语言本地化",
                Description = "图片多语言切换、Inspector 可视化配置、预览数据导出、Luna ZIP 自动注入",
                RepoPath = "/Packages/com.packtools.tools.localization"
            },
            // 新增工具在此添加，例如：
            // new ToolDefinition
            // {
            //     PackageName = "com.packtools.tools.analytics",
            //     DisplayName = "打点工具",
            //     Description = "游戏数据打点、事件追踪",
            //     RepoPath = "/Packages/com.packtools.tools.analytics"
            // },
        };

        private List<PackageInfo> _installedTools;
        private Vector2 _scrollPosition;
        private bool _loading;
        private bool _busy;
        private string _busyMessage;
        private string _gitURL;

        [MenuItem(MenuPath)]
        private static void Open()
        {
            PackToolsManagerWindow window =
                GetWindow<PackToolsManagerWindow>(false, "PackTools 管理器", true);
            window.minSize = new Vector2(WindowMinWidth, WindowMinHeight);
        }

        private void OnEnable()
        {
            _gitURL = EditorPrefs.GetString(GitURLPrefKey, DefaultGitURL);
            RefreshPackages();
        }

        // ── 包列表刷新 ──

        private void RefreshPackages()
        {
            _loading = true;
            _installedTools = new List<PackageInfo>();

            var request = Client.List(false, true);
            EditorApplication.update += OnListUpdate;

            void OnListUpdate()
            {
                if (!request.IsCompleted)
                {
                    return;
                }

                EditorApplication.update -= OnListUpdate;
                _loading = false;

                if (request.Status != StatusCode.Success)
                {
                    Debug.LogError($"[PackTools 管理器] 获取包列表失败: {request.Error?.message}");
                    return;
                }

                _installedTools.Clear();
                foreach (PackageInfo pkg in request.Result)
                {
                    if (pkg.name.StartsWith(PackagePrefix, System.StringComparison.Ordinal)
                        && pkg.name != "com.packtools.tools.manager")
                    {
                        _installedTools.Add(pkg);
                    }
                }

                Repaint();
            }
        }

        // ── 安装 / 更新 / 卸载 ──

        private void InstallTool(ToolDefinition tool)
        {
            string url = $"{_gitURL}?path={tool.RepoPath}";
            _busy = true;
            _busyMessage = $"正在安装 {tool.DisplayName}...";
            var request = Client.Add(url);
            EditorApplication.update += OnAddComplete;

            void OnAddComplete()
            {
                if (!request.IsCompleted)
                {
                    return;
                }
                EditorApplication.update -= OnAddComplete;
                _busy = false;

                if (request.Status == StatusCode.Success)
                {
                    Debug.Log($"[PackTools 管理器] {tool.DisplayName} 安装成功。");
                    RefreshPackages();
                }
                else
                {
                    Debug.LogError(
                        $"[PackTools 管理器] 安装 {tool.DisplayName} 失败: {request.Error?.message}"
                    );
                    EditorUtility.DisplayDialog(
                        "安装失败",
                        $"安装 {tool.DisplayName} 失败:\n{request.Error?.message}",
                        "确定"
                    );
                }
            }
        }

        private void UpdateTool(PackageInfo pkg, ToolDefinition tool)
        {
            // git 包更新：先移除再重新安装，强制 Unity 重新拉取最新版本
            _busy = true;
            _busyMessage = $"正在更新 {pkg.displayName}...";
            var removeRequest = Client.Remove(pkg.name);
            EditorApplication.update += OnRemoveComplete;

            void OnRemoveComplete()
            {
                if (!removeRequest.IsCompleted)
                {
                    return;
                }
                EditorApplication.update -= OnRemoveComplete;

                if (removeRequest.Status != StatusCode.Success)
                {
                    _busy = false;
                    Debug.LogError(
                        $"[PackTools 管理器] 更新失败（无法移除旧版本）: {removeRequest.Error?.message}"
                    );
                    return;
                }

                // 重新安装
                string url = $"{_gitURL}?path={tool.RepoPath}";
                var addRequest = Client.Add(url);
                EditorApplication.update += OnReAddComplete;

                void OnReAddComplete()
                {
                    if (!addRequest.IsCompleted)
                    {
                        return;
                    }
                    EditorApplication.update -= OnReAddComplete;
                    _busy = false;

                    if (addRequest.Status == StatusCode.Success)
                    {
                        Debug.Log($"[PackTools 管理器] {pkg.displayName} 更新成功。");
                        RefreshPackages();
                    }
                    else
                    {
                        Debug.LogError(
                            $"[PackTools 管理器] 更新失败（无法安装新版本）: {addRequest.Error?.message}"
                        );
                    }
                }
            }
        }

        private void UninstallTool(PackageInfo pkg)
        {
            if (pkg.source == UnityEditor.PackageManager.PackageSource.Embedded)
            {
                EditorUtility.DisplayDialog(
                    "无法卸载",
                    $"{pkg.displayName} 是嵌入式包（开发模式），无法通过管理器卸载。\n\n"
                        + "请手动删除项目 Packages/ 目录下对应的文件夹。",
                    "确定"
                );
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "确认卸载",
                    $"确定卸载 {pkg.displayName}（v{pkg.version}）吗？",
                    "卸载",
                    "取消"
                ))
            {
                return;
            }

            _busy = true;
            _busyMessage = $"正在卸载 {pkg.displayName}...";
            var request = Client.Remove(pkg.name);
            EditorApplication.update += OnRemoveComplete;

            void OnRemoveComplete()
            {
                if (!request.IsCompleted)
                {
                    return;
                }
                EditorApplication.update -= OnRemoveComplete;
                _busy = false;

                if (request.Status == StatusCode.Success)
                {
                    Debug.Log($"[PackTools 管理器] {pkg.displayName} 已卸载。");
                    RefreshPackages();
                }
                else
                {
                    Debug.LogError(
                        $"[PackTools 管理器] 卸载 {pkg.displayName} 失败: {request.Error?.message}"
                    );
                    EditorUtility.DisplayDialog(
                        "卸载失败",
                        $"卸载 {pkg.displayName} 失败:\n{request.Error?.message}",
                        "确定"
                    );
                }
            }
        }

        // ── GUI ──

        private void OnGUI()
        {
            DrawHeader();
            DrawGitURLField();

            EditorGUILayout.Space(4);

            if (_busy)
            {
                EditorGUILayout.HelpBox(_busyMessage, MessageType.Info);
            }

            if (_loading)
            {
                EditorGUILayout.HelpBox("正在加载工具列表...", MessageType.Info);
                return;
            }

            EditorGUI.BeginDisabledGroup(_busy);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            DrawInstalledSection();
            DrawAvailableSection();

            EditorGUILayout.EndScrollView();

            EditorGUI.EndDisabledGroup();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            EditorGUILayout.LabelField("PackTools 管理器", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton))
            {
                RefreshPackages();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawGitURLField()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("仓库地址", GUILayout.Width(60));
            string newURL = EditorGUILayout.TextField(_gitURL);
            if (newURL != _gitURL)
            {
                _gitURL = newURL;
                EditorPrefs.SetString(GitURLPrefKey, _gitURL);
            }
            EditorGUILayout.EndHorizontal();
        }

        // ── 已安装 ──

        private void DrawInstalledSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"已安装的工具（{_installedTools.Count}）", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            if (_installedTools.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "尚未安装任何 PackTools 工具。请在下方「可安装的工具」中选择安装。",
                    MessageType.Info
                );
                return;
            }

            for (int i = 0; i < _installedTools.Count; i++)
            {
                DrawInstalledToolEntry(_installedTools[i]);
            }
        }

        private void DrawInstalledToolEntry(PackageInfo pkg)
        {
            ToolDefinition toolDef = FindToolDefinition(pkg.name);

            EditorGUILayout.BeginHorizontal("box", GUILayout.Height(56));

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(pkg.displayName, EditorStyles.boldLabel);
            if (!string.IsNullOrWhiteSpace(pkg.description))
            {
                EditorGUILayout.LabelField(
                    pkg.description,
                    EditorStyles.wordWrappedMiniLabel
                );
            }
            EditorGUILayout.LabelField(
                $"v{pkg.version}  ·  {pkg.source}",
                EditorStyles.miniLabel
            );
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // 嵌入式包不能卸载/更新
            bool isEmbedded = pkg.source == UnityEditor.PackageManager.PackageSource.Embedded;

            using (new EditorGUI.DisabledScope(toolDef == null))
            {
                if (GUILayout.Button("更新", GUILayout.Width(50), GUILayout.Height(24)))
                {
                    UpdateTool(pkg, toolDef);
                }
            }

            if (GUILayout.Button("打开", GUILayout.Width(50), GUILayout.Height(24)))
            {
                OpenToolMenu(pkg);
            }

            using (new EditorGUI.DisabledScope(isEmbedded))
            {
                if (GUILayout.Button("卸载", GUILayout.Width(50), GUILayout.Height(24)))
                {
                    UninstallTool(pkg);
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);
        }

        // ── 可安装 ──

        private void DrawAvailableSection()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("可安装的工具", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            bool anyAvailable = false;

            for (int i = 0; i < KnownTools.Length; i++)
            {
                ToolDefinition tool = KnownTools[i];
                bool installed = IsToolInstalled(tool.PackageName);
                if (installed)
                {
                    continue;
                }
                anyAvailable = true;
                DrawAvailableToolEntry(tool);
            }

            if (!anyAvailable)
            {
                EditorGUILayout.HelpBox(
                    "所有已知工具均已安装。",
                    MessageType.Info
                );
            }
        }

        private void DrawAvailableToolEntry(ToolDefinition tool)
        {
            EditorGUILayout.BeginHorizontal("box", GUILayout.Height(56));

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(tool.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                tool.Description,
                EditorStyles.wordWrappedMiniLabel
            );
            EditorGUILayout.LabelField(tool.PackageName, EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("安装", GUILayout.Width(60), GUILayout.Height(28)))
            {
                InstallTool(tool);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(3);
        }

        // ── 辅助 ──

        private bool IsToolInstalled(string packageName)
        {
            for (int i = 0; i < _installedTools.Count; i++)
            {
                if (_installedTools[i].name == packageName)
                {
                    return true;
                }
            }
            return false;
        }

        private static ToolDefinition FindToolDefinition(string packageName)
        {
            for (int i = 0; i < KnownTools.Length; i++)
            {
                if (KnownTools[i].PackageName == packageName)
                {
                    return KnownTools[i];
                }
            }
            return null;
        }

        private static void OpenToolMenu(PackageInfo pkg)
        {
            switch (pkg.name)
            {
                case "com.packtools.tools.localization":
                    var switcherType = FindType("PackTools.LanguagLocalization.LanguageSwitcher");
                    if (switcherType != null)
                    {
                        var objects = Object.FindObjectsByType(
                            switcherType,
                            FindObjectsInactive.Exclude,
                            FindObjectsSortMode.None
                        );
                        if (objects.Length > 0)
                        {
                            Selection.activeGameObject = ((Component)objects[0]).gameObject;
                            EditorGUIUtility.PingObject(objects[0]);
                            return;
                        }
                    }
                    EditorUtility.DisplayDialog(
                        "多语言本地化",
                        "当前场景中没有 LanguageSwitcher 组件。\n"
                            + "请在场景中创建空对象并添加 LanguageSwitcher 组件。",
                        "确定"
                    );
                    break;

                default:
                    EditorUtility.DisplayDialog(
                        pkg.displayName,
                        $"请通过 Unity 菜单 Tools > 使用 {pkg.displayName} 功能。",
                        "确定"
                    );
                    break;
            }
        }

        private static System.Type FindType(string typeName)
        {
            foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(typeName);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }
    }
}
