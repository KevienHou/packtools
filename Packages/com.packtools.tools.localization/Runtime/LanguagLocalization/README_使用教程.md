# Language Switcher 多语言工具使用教程

## 1. 工具用途

Language Switcher 用于管理 Playable 游戏中的图片多语言资源，主要包含三部分功能：

1. 根据平台语言或游戏内选择，激活对应语言的 UI 对象。
2. 在 Unity Inspector 中增加语言、创建配置项并自动绑定语言对象。
3. 导出 `LanguageData.json`，并在 Luna 生成预览 ZIP 后自动把 JSON 写入 ZIP，供预览平台检查多语言图片是否配置完整。

当前工具主要面向使用 `UnityEngine.UI.Image` 的图片本地化。如果对象使用
`SpriteRenderer`、TextMeshPro 文本或动态下载图片，需要单独扩展。

---

## 2. 环境要求

- Unity：2022.3 LTS，当前项目使用 `2022.3.14f1c1`。
- Luna / Playworks：使用项目现有版本。
- 只使用工具：不需要安装额外 Unity Package。
- 重新编译 EditorCore DLL：需要 .NET SDK 8 或更高版本。
- 首次构建混淆 DLL 时需要连接 NuGet，用于恢复 Obfuscar 项目本地工具。

---

## 3. 文件结构

```text
Assets/DEV/LanguagLocalization/
├── LanguageSwitcher.cs
├── README_使用教程.md
└── Editor/
    ├── LanguageSwitcherEditor.cs
    ├── LanguageDataExporter.cs
    ├── LunaLanguageBuildWatcher.cs
    └── Plugins/
        └── LanguageLocalization.EditorCore.dll

Tools/LanguageLocalization.EditorCore/
├── LanguageLocalization.EditorCore.csproj
├── ZipLanguageDataInjector.cs
├── obfuscar.xml
└── build.sh
```

- `LanguageSwitcher.cs`：游戏运行时语言解析和对象切换。
- `LanguageSwitcherEditor.cs`：Inspector 编辑器界面。
- `LanguageDataExporter.cs`：收集图片并生成预览 JSON。
- `LunaLanguageBuildWatcher.cs`：监听 Luna ZIP 构建结果。
- `LanguageLocalization.EditorCore.dll`：混淆后的 ZIP 注入核心。

---

## 4. 五分钟快速配置

### 第一步：添加 LanguageSwitcher

1. 在场景中新建一个空对象，建议命名为 `LanguageSwitcher`。
2. 添加 `LanguageSwitcher` 组件。
3. 确保组件处于启用状态，否则运行时不会自动读取和切换语言。
4. `Language` 字段在 Editor 运行时可用于指定测试语言，例如 `en-US`。

建议每个场景只放一个主要的 `LanguageSwitcher`。JSON 导出支持多个已加载场景中的
LanguageSwitcher，但需要保证所有 Item Name 唯一，避免预览平台无法区分。

### 第二步：选择需要支持的语言

在 Inspector 的“可添加的语言”区域点击需要的语言，例如：

- 英语（美国）：`en-US`
- 日语（日本）：`ja-JP`
- 韩语（韩国）：`ko-KR`
- 简体中文：`zh-CN`
- 繁体中文：`zh-TW`

已经添加的语言会出现在“已选择的语言”区域。

点击已选择语言的按钮会删除该语言，同时删除所有 Language Item 中对应的配置。
删除操作支持 Unity Undo。

### 第三步：准备多语言图片对象

推荐的层级结构：

```text
PlayNowButton_Localized
├── 英语
├── 日语
├── 韩语
├── 简中
└── 繁中
```

每个语言子对象应满足：

- 包含 `Image` 组件。
- `Image.sprite` 已配置图片资源。
- 子对象名称与 Inspector 语言名称中括号前的名称完全一致。
- 同一个 Root 下不要出现两个同名语言对象。

当前常用对象名：

| 语言代码 | 自动查找对象名 |
| --- | --- |
| `en-US` | `英语` |
| `ja-JP` | `日语` |
| `ko-KR` | `韩语` |
| `ru-RU` | `俄语` |
| `zh-CN` | `简中` |
| `zh-TW` | `繁中` |

其他语言的自动查找名称，以 Inspector 按钮中括号前的文字为准。

### 第四步：创建 Language Item

1. 点击“1. 增加多语言对象”。
2. 展开 `Language Items` 新增的元素。
3. 填写 `Item Name`，例如 `PlayNowButton`。
4. 把多语言对象的根节点拖到 `Root`。
5. 点击“2. 自动查找语言对象”。
6. 检查每个 Config 的 `Img` 是否绑定成功。

`Item Name` 会作为 JSON 中的 `RootName`，推荐使用稳定、唯一且不带空格的英文名称。

如果自动查找失败，可以直接把对应 GameObject 手动拖到 `Img` 字段。

### 第五步：运行验证

进入 Play Mode。Editor 环境会使用组件的 `Language` 字段，例如：

```text
en-US
ja-JP
zh-CN
zh-TW
```

正常情况下，每个 Language Item 只会激活当前语言对应的对象，其他语言对象会关闭。

如果指定语言未配置，工具按以下顺序回退：

1. 精确匹配语言和地区。
2. 匹配相同语种的已配置地区，例如 `en-GB` 匹配 `en-US`。
3. 使用 `en-US`。
4. 如果没有 `en-US`，使用已选择语言中的第一个有效配置。

---

## 5. 游戏内切换语言

### UnityEvent / Button 推荐调用

`ChangeActiveLocale` 是无返回值入口，适合绑定到 Button 或其他 UnityEvent：

```csharp
using UnityEngine;

public sealed class LanguageMenu : MonoBehaviour
{
    [SerializeField] private LanguageSwitcher languageSwitcher;

    public void SelectEnglish()
    {
        languageSwitcher.ChangeActiveLocale("en-US");
    }

    public void SelectJapanese()
    {
        languageSwitcher.ChangeActiveLocale("ja-JP");
    }

    public void SelectTraditionalChinese()
    {
        languageSwitcher.ChangeActiveLocale("zh-TW");
    }
}
```

### 获取实际生效的语言

`ApplyLocalizedContent` 会返回最终实际使用的语言代码：

```csharp
string activeLocale = languageSwitcher.ApplyLocalizedContent("en-GB");
Debug.Log($"实际使用语言：{activeLocale}");
```

如果项目只配置了 `en-US`，上面的结果会是 `en-US`。

### 语言代码标准化

```csharp
string locale = LanguageSwitcher.CanonicalizeLocaleCode("zh_HK");
// 返回 zh-TW
```

工具内置以下别名：

| 平台语言 | 最终语言 |
| --- | --- |
| `zh-HK` | `zh-TW` |
| `zh-MO` | `zh-TW` |
| `zh-Hant` | `zh-TW` |
| `zh-Hans` | `zh-CN` |
| `pt-PT` | `pt-BR` |

---

## 6. 导出 LanguageData.json

可以通过以下任意入口导出：

- LanguageSwitcher Inspector 中的“3. 保存多语言预览数据”。
- Unity 顶部菜单：
  `Tools > Language Localization > 导出预览数据`。

输出路径：

```text
项目根目录/LanguageData/LanguageData.json
```

JSON 结构保持为：

```json
{
  "languageSpriteDatas": [
    {
      "RootName": "PlayNowButton",
      "languageSprites": [
        {
          "LanguageName": "en-US",
          "LanguageTextureBase64": "..."
        }
      ]
    }
  ]
}
```

导出规则：

- 每个 Language Item 生成一个 `languageSpriteDatas` 元素。
- 每个语言 Config 只生成一条记录。
- 所有图片都会根据 Unity 当前导入后的 Sprite 重新编码为预览 PNG；不再直接把硬盘上的
  高分辨率原始文件写入 JSON。
- Sprite 最长边超过 512 像素时，会先等比缩小到最长边 512 像素。
- 缩略图保留透明通道，只影响导出的网页预览，不修改项目中的原始 Sprite、图片或图集。
- 重新编码后的文件如果没有比原图更小，会自动保留原始文件，避免体积反而增大。
- 缩略图生成失败时会自动回退原始文件，并在 Console 显示对应资源路径。
- `Img`、`Image`、`Sprite` 或资源文件缺失时，Base64 为空，并在 Console 统计缺失数量。
- 只导出当前已加载场景中的 LanguageSwitcher，不导出 Project 中未实例化的 Prefab。

JSON 的字段名和层级没有变化，网页预览工具无需因为图片压缩调整解析代码。导出完成后，
Console 会显示原始图片总量、缩略图总量和最终 JSON 大小，便于确认压缩效果。

注意：Base64 仍会比对应二进制图片大约多三分之一，但导出器会先缩小大图，因此 Logo
等高分辨率图片不会再以原始尺寸写入 JSON。

---

## 7. Luna ZIP 自动注入流程

Unity Editor 打开时，工具会监听：

```text
项目根目录/LunaTemp/stage4/create-hub/*.zip
```

检测到新的或发生变化的 ZIP 后，会自动执行：

1. 等待 ZIP 至少稳定 1 秒。
2. 检查文件是否仍被 Luna 占用。
3. 重新导出 `LanguageData.json`。
4. 检查 ZIP 类型。
5. 创建临时候选 ZIP。
6. 写入 `LanguageData.json`。
7. 成功后安全替换原 ZIP；失败时保留或恢复原文件。

以下情况会跳过写入：

- ZIP 根目录包含 `js` 目录，工具将其视为渠道包。
- JSON 或 ZIP 文件无效。

重要事项：

- ZIP 已有 `LanguageData.json` 时会安全替换为最新版本，不会保留旧数据或产生重复条目。
- 自动监听仅在 Unity Editor 打开时有效。
- Unity 启动前已经存在且没有再次变化的 ZIP 不会自动触发，应重新构建。
- 不要在 Luna 正在写入 ZIP 时手动移动或修改文件。
- Console 会输出“已写入”“已更新”“渠道包跳过”或错误原因。

---

## 8. Inspector 功能说明

### 已选择的语言

- 显示当前参与配置的语言。
- 点击按钮可删除语言及所有 Item 中对应配置。

### 可添加的语言

- 显示工具内置的全部语言。
- 已添加语言的按钮会禁用。

### 增加多语言对象

- 新建一个 `LanguageItem`。
- 自动为所有已选择语言创建 Config。

### 自动查找语言对象

- 搜索 Root 下所有层级的子对象，包括未激活对象。
- 根据语言显示名称中括号前的文本进行精确名称匹配。
- 已经手动绑定的 `Img` 不会被覆盖。
- 找不到对象时会在 Console 输出明确警告。

### 保存多语言预览数据

- 生成最新 `LanguageData.json`。
- 不需要先保存场景，但正式构建前建议保存场景，确保配置可追溯。

---

## 9. API 速查

| API | 用途 |
| --- | --- |
| `ChangeActiveLocale(string)` | 游戏内切换语言，适合 UnityEvent |
| `ApplyLocalizedContent(string)` | 切换语言并返回最终语言代码 |
| `ReadPlatformPreferredLocale()` | 读取并标准化 Luna 平台语言 |
| `SelectBestConfiguredLocale(string)` | 根据已选择语言计算最佳匹配 |
| `CanonicalizeLocaleCode(string)` | 标准化语言代码与别名 |
| `AddLanguageConfiguration(string, string)` | 增加语言及所有 Item 配置 |
| `RemoveLanguageConfiguration(string)` | 删除语言及所有 Item 配置 |
| `CreateLocalizationItem()` | 创建一个多语言项 |
| `RemoveLocalizationItem(LanguageItem)` | 删除指定多语言项 |
| `AutoBindLanguageObjects()` | 自动绑定所有 Item 的语言对象 |

旧脚本的方法名未保留。如果其他脚本曾直接调用旧方法，需要改成上表中的新 API。

---

## 10. 重新构建混淆 DLL

只有修改了以下核心源码时才需要重新构建 DLL：

```text
Tools/LanguageLocalization.EditorCore/ZipLanguageDataInjector.cs
```

在终端运行：

```bash
cd "<项目根目录>/Tools/LanguageLocalization.EditorCore"
./build.sh
```

脚本会：

1. 编译 `.NET Standard 2.1` DLL。
2. 恢复项目本地 Obfuscar 2.2.50。
3. 保留公开 API 名称。
4. 混淆私有类型、私有方法、参数名和字符串。
5. 把最终 DLL 复制到：

```text
Assets/DEV/LanguagLocalization/Editor/Plugins/
LanguageLocalization.EditorCore.dll
```

修改 Inspector、导出适配器或 ZIP 监听源码时，不需要手动构建 DLL，Unity 会自动编译。

混淆只能提高逆向分析成本，不能保护密码、Token 或密钥。不要把敏感信息写入 DLL。

---

## 11. 常见问题

### 运行后所有图片都没有显示

依次检查：

1. LanguageSwitcher 组件是否启用。
2. `Language Items` 是否为空。
3. Item 的 Config 是否存在。
4. Config 的 `Img` 是否已经绑定。
5. `Img` 是否被其他脚本再次关闭。

如果当前语言不存在，工具会尝试激活该 Item 中第一个有效对象。

### 自动查找失败

检查：

- Item 的 `Root` 是否为空。
- 子对象名称是否与语言名称括号前文本完全一致。
- 是否存在多余空格。
- 是否把根节点本身当成语言对象；自动查找只绑定 Root 的子孙对象。
- 同一个 Root 下是否有重复名称。

### JSON 中 Base64 为空

检查：

- Config 的 `Img` 是否为空。
- 对象是否包含 `UnityEngine.UI.Image`。
- `Image.sprite` 是否为空。
- Sprite 是否是 AssetDatabase 可定位的资源。
- 原图片文件是否仍然存在。

### JSON 中同一语言重复

新版导出逻辑每个 Config 只添加一次。如果仍然重复，应检查同一个 Item 的 Config
列表中是否手动创建了重复语言代码。

### ZIP 没有 LanguageData.json

检查：

1. 构建时 Unity Editor 是否打开。
2. ZIP 是否生成在 `LunaTemp/stage4/create-hub`。
3. ZIP 是否属于根目录含 `js` 的渠道包。
4. Console 是否有导出失败或文件占用信息。
5. ZIP 是否在 Unity 启动前就已经存在且没有再次变化。

### 修改 DLL 后 Unity 报 API 不存在

重新运行 `build.sh`，确认输出 DLL 的时间已更新，然后回到 Unity 等待重新导入。
如果修改了 DLL 公开 API，还必须同步更新 `LunaLanguageBuildWatcher.cs` 中的调用。

---

## 12. 发布前检查清单

- [ ] 场景中存在且启用了 LanguageSwitcher。
- [ ] `selectedLanguages` 包含发布需要的全部语言。
- [ ] 每个 Language Item 都有唯一、可读的 Item Name。
- [ ] 每个 Language Item 都配置了 Root。
- [ ] 自动查找后所有需要的 Img 均已绑定。
- [ ] 每个 Img 都有 Image 和 Sprite。
- [ ] 使用每一种发布语言进入 Play Mode 检查过显示结果。
- [ ] 手动导出 JSON 后 Console 中缺失图片数量为 0。
- [ ] 预览 ZIP 中包含根目录 `LanguageData.json`。
- [ ] 预览平台能显示所有 Item 和语言图片。
- [ ] 场景和工具修改已纳入版本控制。
