using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;


namespace PackTools.LanguagLocalization
{


    [CustomEditor(typeof(LanguageSwitcher))]
    public sealed class LanguageSwitcherEditor : Editor
    {
        private const int ButtonsPerRow = 3;
        private const string TutorialAssetPath =
            "Packages/com.packtools.tools/Runtime/LanguagLocalization/README_使用教程.md";

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();

            LanguageSwitcher switcher = (LanguageSwitcher)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("已选择的语言（点击可删除）", EditorStyles.boldLabel);
            DrawSelectedLanguages(switcher);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("可添加的语言", EditorStyles.boldLabel);
            DrawSupportedLanguages(switcher);

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("功能", EditorStyles.boldLabel);

            if (GUILayout.Button("1. 增加多语言对象"))
            {
                ApplyChange(switcher, "增加多语言对象", switcher.CreateLocalizationItem);
            }

            if (GUILayout.Button("2. 自动查找语言对象"))
            {
                ApplyChange(switcher, "自动查找语言对象", switcher.AutoBindLanguageObjects);
            }

            if (GUILayout.Button("3. 保存多语言预览数据"))
            {
                LanguageDataExporter.ExportPreviewDataWithLog();
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "1. 增加一个多语言项，并填写 Item Name 与 Root。\n"
                    + "2. Root 下各语言对象的名称应与语言按钮括号前名称一致。\n"
                    + "3. 点击“自动查找语言对象”完成绑定。\n"
                    + "4. 点击“保存多语言预览数据”生成 LanguageData/LanguageData.json。",
                MessageType.Info
            );
            EditorGUILayout.HelpBox(
                "删除语言会同时删除所有 Language Item 中对应的配置。操作支持 Undo。",
                MessageType.Warning
            );

            if (GUILayout.Button("打开完整使用教程"))
            {
                OpenTutorial();
            }
        }

        [MenuItem("Tools/Language Localization/打开使用教程", false, 1)]
        private static void OpenTutorial()
        {
            Object tutorial = AssetDatabase.LoadAssetAtPath<Object>(TutorialAssetPath);
            if (tutorial == null)
            {
                Debug.LogError($"找不到使用教程：{TutorialAssetPath}");
                return;
            }

            Selection.activeObject = tutorial;
            EditorGUIUtility.PingObject(tutorial);
            AssetDatabase.OpenAsset(tutorial);
        }

        private static void DrawSelectedLanguages(LanguageSwitcher switcher)
        {
            IReadOnlyList<string> selected = switcher.selectedLanguages;
            if (selected == null || selected.Count == 0)
            {
                EditorGUILayout.HelpBox("尚未选择语言。", MessageType.None);
                return;
            }

            int index = 0;
            while (index < selected.Count)
            {
                EditorGUILayout.BeginHorizontal();
                for (int column = 0; column < ButtonsPerRow && index < selected.Count; column++, index++)
                {
                    string code = selected[index];
                    string displayName = switcher.supportedLanguages.TryGetValue(
                        code,
                        out string name
                    )
                        ? name
                        : code;

                    if (GUILayout.Button(displayName))
                    {
                        if (
                            EditorUtility.DisplayDialog(
                                "删除语言",
                                $"确定删除 {displayName}（{code}）及其全部配置吗？",
                                "删除",
                                "取消"
                            )
                        )
                        {
                            string capturedCode = code;
                            ApplyChange(
                                switcher,
                                "删除语言",
                                () => switcher.RemoveLanguageConfiguration(capturedCode)
                            );
                            EditorGUILayout.EndHorizontal();
                            GUIUtility.ExitGUI();
                        }
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private static void DrawSupportedLanguages(LanguageSwitcher switcher)
        {
            int index = 0;
            EditorGUILayout.BeginHorizontal();

            foreach (KeyValuePair<string, string> language in switcher.supportedLanguages)
            {
                bool alreadySelected =
                    switcher.selectedLanguages != null
                    && switcher.selectedLanguages.Exists(
                        code =>
                            string.Equals(
                                code,
                                language.Key,
                                System.StringComparison.OrdinalIgnoreCase
                            )
                    );

                using (new EditorGUI.DisabledScope(alreadySelected))
                {
                    if (GUILayout.Button(language.Value))
                    {
                        string code = language.Key;
                        string displayName = language.Value;
                        ApplyChange(
                            switcher,
                            "添加语言",
                            () => switcher.AddLanguageConfiguration(code, displayName)
                        );
                    }
                }

                index++;
                if (index % ButtonsPerRow == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void ApplyChange(
            LanguageSwitcher switcher,
            string undoName,
            System.Action action
        )
        {
            Undo.RecordObject(switcher, undoName);
            action();
            EditorUtility.SetDirty(switcher);
        }

        [MenuItem("GameObject/Language Switcher Tool/按图片名称重命名", false, 0)]
        private static void RenameItemsBySprite()
        {
            GameObject selectedObject = Selection.activeGameObject;
            if (selectedObject == null || selectedObject.GetComponent<RectTransform>() == null)
            {
                Debug.LogError("请选择一个 UI 根对象。");
                return;
            }

            Undo.RecordObjects(
                selectedObject.GetComponentsInChildren<Transform>(true),
                "按图片名称重命名"
            );

            RectTransform root = selectedObject.GetComponent<RectTransform>();
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                Image image = child.GetComponent<Image>();
                if (image != null && image.sprite != null)
                {
                    child.name = image.sprite.name;
                }
            }
        }
    }
}