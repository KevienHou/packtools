using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
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
    /// 安装、更新、卸载 PackTools 工具，并提示新版本。
    /// </summary>
    public sealed class PackToolsManagerWindow : EditorWindow
    {
        private const string MenuPath = "Tools/PackTools 管理器";
        private const string PackagePrefix = "com.packtools.tools.";
        private const float WindowMinWidth = 480f;
        private const float WindowMinHeight = 360f;
        private const string GitURL = "https://github.com/KevienHou/packtools.git";
        private const string RawBaseURL = "https://raw.githubusercontent.com/KevienHou/packtools/main";

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
        private Dictionary<string, string> _latestVersions; // packageName -> latest version
        private Vector2 _scrollPosition;
        private bool _loading;
        private bool _busy;
        private string _busyMessage;
        private bool _checkingVersions;

        [MenuItem(MenuPath)]
        private static void Open()
        {
            PackToolsManagerWindow window =
                GetWindow<PackToolsManagerWindow>(false, "PackTools 管理器", true);
            window.minSize = new Vector2(WindowMinWidth, WindowMinHeight);
        }

        private void OnEnable()
        {
            _latestVersions = new Dictionary<string, string>();
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

                // 加载完本地包后，检查远程版本
                CheckRemoteVersions();
            }
        }

        // ── 远程版本检查 ──

        private void CheckRemoteVersions()
        {
            _checkingVersions = true;
            _latestVersions.Clear();

            int pendingRequests = 0;

            for (int i = 0; i < KnownTools.Length; i++)
            {
                ToolDefinition tool = KnownTools[i];
                string rawURL = $"{RawBaseURL}{tool.RepoPath}/package.json";

                pendingRequests++;
                FetchRemoteVersion(tool.PackageName, rawURL);
            }

            // 如果没有已知工具，直接结束
            if (pendingRequests == 0)
            {
                _checkingVersions = false;
            }
        }

        private void FetchRemoteVersion(string packageName, string url)
        {
            var webRequest = UnityWebRequest.Get(url);
            webRequest.timeout = 10;
            var op = webRequest.SendWebRequest();

            EditorApplication.update += OnWebComplete;

            void OnWebComplete()
            {
                if (!op.isDone)
                {
                    return;
                }

                EditorApplication.update -= OnWebComplete;

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string version = ExtractVersionFromJson(webRequest.downloadHandler.text);
                    if (!string.IsNullOrEmpty(version))
                    {
                        _latestVersions[packageName] = version;
                    }
                }

                // 检查是否所有请求都完成了
                CheckAllRequestsDone();
            }
        }

        private int _pendingVersionChecks;

        private void CheckAllRequestsDone()
        {
            // 简单方式：每完成一个请求，检查是否还有未完成的
            // 由于每个请求独立注册了 EditorApplication.update，
            // 我们用一个简单计数器判断
            _pendingVersionChecks++;
            if (_pendingVersionChecks >= KnownTools.Length)
            {
                _checkingVersions = false;
                _pendingVersionChecks = 0;
                Repaint();
            }
        }

        private static string ExtractVersionFromJson(string json)
        {
            const string key = "\"version\"";
            int keyIndex = json.IndexOf(key, System.StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                return null;
            }

            int colonIndex = json.IndexOf(':', keyIndex + key.Length);
            if (colonIndex < 0)
            {
                return null;
            }

            int startQuote = json.IndexOf('"', colonIndex + 1);
            if (startQuote < 0)
            {
                return null;
            }

            int endQuote = json.IndexOf('"', startQuote + 1);
            if (endQuote < 0)
            {
                return null;
            }

            return json.Substring(startQuote + 1, endQuote - startQuote - 1);
        }

        // ── 版本比较 ──

        private static bool IsNewerVersion(string remote, string local)
        {
            if (string.IsNullOrEmpty(remote) || string.IsNullOrEmpty(local))
            {
                return false;
            }

            if (!System.Version.TryParse(remote, out var remoteVer))
            {
                return false;
            }
            if (!System.Version.TryParse(local, out var localVer))
            {
                return false;
            }

            return remoteVer > localVer;
        }

        private bool HasNewVersion(string packageName, string installedVersion)
        {
            if (!_latestVersions.TryGetValue(packageName, out string latest))
            {
                return false;
            }
            return IsNewerVersion(latest, installedVersion);
        }

        // ── 安装 / 更新 / 卸载 ──

        private void InstallTool(ToolDefinition tool)
        {
            string url = $"{GitURL}?path={tool.RepoPath}";
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

                string url = $"{GitURL}?path={tool.RepoPath}";
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

            if (_checkingVersions)
            {
                GUILayout.Label("检查版本中...", EditorStyles.miniLabel, GUILayout.Width(70));
            }

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton))
            {
                RefreshPackages();
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
            bool hasUpdate = HasNewVersion(pkg.name, pkg.version);
            string latestVer = _latestVersions.TryGetValue(pkg.name, out var lv) ? lv : null;

            EditorGUILayout.BeginHorizontal("box", GUILayout.Height(56));

            EditorGUILayout.BeginVertical();

            // 名称行：如果有新版本，名称后加绿色提示
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(pkg.displayName, EditorStyles.boldLabel);
            if (hasUpdate)
            {
                var greenLabel = new GUIStyle(EditorStyles.boldLabel);
                greenLabel.normal.textColor = new Color(0.2f, 0.7f, 0.2f);
                GUILayout.Label($"  有新版本 v{latestVer}", greenLabel);
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(pkg.description))
            {
                EditorGUILayout.LabelField(
                    pkg.description,
                    EditorStyles.wordWrappedMiniLabel
                );
            }

            // 版本行：显示当前版本 + 来源
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
                // 有新版本时更新按钮高亮
                if (hasUpdate)
                {
                    var greenBtn = new GUIStyle(GUI.skin.button);
                    greenBtn.normal.textColor = new Color(0.2f, 0.7f, 0.2f);
                    greenBtn.fontStyle = FontStyle.Bold;
                    if (GUILayout.Button("更新", greenBtn, GUILayout.Width(50), GUILayout.Height(24)))
                    {
                        UpdateTool(pkg, toolDef);
                    }
                }
                else
                {
                    if (GUILayout.Button("更新", GUILayout.Width(50), GUILayout.Height(24)))
                    {
                        UpdateTool(pkg, toolDef);
                    }
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
            // 获取远程版本
            string remoteVer = _latestVersions.TryGetValue(tool.PackageName, out var rv) ? rv : null;

            EditorGUILayout.BeginHorizontal("box", GUILayout.Height(56));

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(tool.DisplayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                tool.Description,
                EditorStyles.wordWrappedMiniLabel
            );

            if (!string.IsNullOrEmpty(remoteVer))
            {
                EditorGUILayout.LabelField($"v{remoteVer}  ·  {tool.PackageName}", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.LabelField(tool.PackageName, EditorStyles.miniLabel);
            }
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

        private void OpenToolMenu(PackageInfo pkg)
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
