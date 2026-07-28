using System;
using System.Collections.Generic;
using UnityEngine;

namespace PackTools.LanguagLocalization
{



    [DisallowMultipleComponent]
    public sealed class LanguageSwitcher : MonoBehaviour
    {
        public List<string> selectedLanguages = new List<string>();
        public List<LanguageItem> languageItems = new List<LanguageItem>();

        [HideInInspector, NonSerialized]
        public Dictionary<string, string> supportedLanguages = CreateSupportedLanguages();

        public string language = "en-US";

        private const string DefaultLanguage = "en-US";

        private static readonly Dictionary<string, string> DefaultRegions =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
            { "en", "US" },
            { "es", "ES" },
            { "pt", "BR" },
            { "de", "DE" },
            { "fr", "FR" },
            { "ja", "JP" },
            { "ko", "KR" },
            { "ru", "RU" },
            { "ar", "SA" },
            { "bg", "BG" },
            { "cs", "CZ" },
            { "da", "DK" },
            { "el", "GR" },
            { "fi", "FI" },
            { "he", "IL" },
            { "hr", "HR" },
            { "hu", "HU" },
            { "id", "ID" },
            { "it", "IT" },
            { "lt", "LT" },
            { "ms", "MY" },
            { "nl", "NL" },
            { "pl", "PL" },
            { "ro", "RO" },
            { "sk", "SK" },
            { "sv", "SE" },
            { "th", "TH" },
            { "tr", "TR" },
            { "uk", "UA" },
            { "vi", "VN" },
            { "zh", "CN" },
            { "nb", "NO" },
            { "az", "AZ" },
            { "be", "BY" },
            { "kk", "KZ" },
            { "uz", "UZ" },
            { "tl", "PH" },
            { "is", "IS" },
            { "hi", "IN" },
            };

        private static readonly Dictionary<string, string> LanguageAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
            { "zh-HK", "zh-TW" },
            { "zh-MO", "zh-TW" },
            { "zh-Hant", "zh-TW" },
            { "zh-Hans", "zh-CN" },
            { "pt-PT", "pt-BR" },
            };

        private void Start()
        {
            string requestedLanguage = language;
#if !UNITY_EDITOR
        requestedLanguage = ReadPlatformPreferredLocale();
#endif
            ApplyLocalizedContent(requestedLanguage);
        }

        /// <summary>
        /// 游戏运行中切换语言。返回最终实际使用的语言代码。
        /// </summary>
        public string ApplyLocalizedContent(string requestedLanguage)
        {
            language = SelectBestConfiguredLocale(requestedLanguage);

            if (languageItems == null)
            {
                return language;
            }

            for (int i = 0; i < languageItems.Count; i++)
            {
                LanguageItem item = languageItems[i];
                if (item != null)
                {
                    item.ActivateMatchingObject(language);
                }
            }

            return language;
        }

        /// <summary>
        /// 兼容 UnityEvent 的 void 入口。
        /// </summary>
        public void ChangeActiveLocale(string requestedLanguage)
        {
            ApplyLocalizedContent(requestedLanguage);
        }

        public string ReadPlatformPreferredLocale()
        {
#if UNITY_EDITOR
            string preferredLanguage = language;
#else
        string preferredLanguage = Luna.Unity.Playable.GetPreferredLanguage();
#endif
            return CanonicalizeLocaleCode(preferredLanguage);
        }

        public string SelectBestConfiguredLocale(string requestedLanguage)
        {
            string normalized = CanonicalizeLocaleCode(requestedLanguage);

            if (selectedLanguages == null || selectedLanguages.Count == 0)
            {
                return normalized;
            }

            string exactMatch = FindSelectedLanguage(normalized);
            if (!string.IsNullOrEmpty(exactMatch))
            {
                return exactMatch;
            }

            int separatorIndex = normalized.IndexOf('-');
            string languageCode =
                separatorIndex > 0 ? normalized.Substring(0, separatorIndex) : normalized;

            // 平台返回了未配置的地区时，优先匹配相同语种，例如 en-GB -> en-US。
            for (int i = 0; i < selectedLanguages.Count; i++)
            {
                string candidate = selectedLanguages[i];
                if (
                    !string.IsNullOrWhiteSpace(candidate)
                    && candidate.StartsWith(languageCode + "-", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return candidate;
                }
            }

            string defaultMatch = FindSelectedLanguage(DefaultLanguage);
            if (!string.IsNullOrEmpty(defaultMatch))
            {
                return defaultMatch;
            }

            for (int i = 0; i < selectedLanguages.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(selectedLanguages[i]))
                {
                    return selectedLanguages[i];
                }
            }

            return DefaultLanguage;
        }

        public static string CanonicalizeLocaleCode(string languageCode)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                return DefaultLanguage;
            }

            string normalized = languageCode.Trim().Replace('_', '-');
            if (LanguageAliases.TryGetValue(normalized, out string alias))
            {
                return alias;
            }

            string[] parts = normalized.Split('-');
            string neutralCode = ConvertAsciiToLower(parts[0]);

            if (parts.Length == 1)
            {
                return DefaultRegions.TryGetValue(neutralCode, out string defaultRegion)
                    ? neutralCode + "-" + defaultRegion
                    : neutralCode;
            }

            string region = parts[1];
            if (region.Length == 2)
            {
                region = ConvertAsciiToUpper(region);
            }
            else if (region.Length == 4)
            {
                region =
                    ConvertAsciiToUpper(region.Substring(0, 1))
                    + ConvertAsciiToLower(region.Substring(1));
            }

            normalized = neutralCode + "-" + region;
            return LanguageAliases.TryGetValue(normalized, out alias) ? alias : normalized;
        }

        public void RemoveLanguageConfiguration(string key)
        {
            if (selectedLanguages == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            selectedLanguages.RemoveAll(
                value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase)
            );

            if (languageItems == null)
            {
                return;
            }

            for (int i = 0; i < languageItems.Count; i++)
            {
                LanguageItem item = languageItems[i];
                if (item?.config == null)
                {
                    continue;
                }

                item.config.RemoveAll(
                    config =>
                        config == null
                        || string.Equals(config.code, key, StringComparison.OrdinalIgnoreCase)
                );
            }
        }

        public void RemoveLocalizationItem(LanguageItem item)
        {
            languageItems?.Remove(item);
        }

        public void AddLanguageConfiguration(string code, string displayName)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }

            selectedLanguages ??= new List<string>();
            languageItems ??= new List<LanguageItem>();

            if (FindSelectedLanguage(code) != null)
            {
                return;
            }

            selectedLanguages.Add(code);
            for (int i = 0; i < languageItems.Count; i++)
            {
                LanguageItem item = languageItems[i];
                if (item == null)
                {
                    continue;
                }

                item.config ??= new List<LanguageItemConfig>();
                if (item.FindLanguageConfiguration(code) == null)
                {
                    item.config.Add(
                        new LanguageItemConfig
                        {
                            code = code,
                            debugShow = displayName,
                        }
                    );
                }
            }
        }

        public void CreateLocalizationItem()
        {
            selectedLanguages ??= new List<string>();
            languageItems ??= new List<LanguageItem>();

            LanguageItem item = new LanguageItem();
            for (int i = 0; i < selectedLanguages.Count; i++)
            {
                string code = selectedLanguages[i];
                supportedLanguages.TryGetValue(code, out string displayName);
                item.config.Add(
                    new LanguageItemConfig
                    {
                        code = code,
                        debugShow = displayName ?? code,
                    }
                );
            }

            languageItems.Add(item);
        }

        public void AutoBindLanguageObjects()
        {
            if (languageItems == null)
            {
                return;
            }

            for (int i = 0; i < languageItems.Count; i++)
            {
                languageItems[i]?.AutoBindChildObjects();
            }
        }

        private string FindSelectedLanguage(string code)
        {
            if (selectedLanguages == null || string.IsNullOrWhiteSpace(code))
            {
                return null;
            }

            for (int i = 0; i < selectedLanguages.Count; i++)
            {
                if (string.Equals(selectedLanguages[i], code, StringComparison.OrdinalIgnoreCase))
                {
                    return selectedLanguages[i];
                }
            }

            return null;
        }

        private static string ConvertAsciiToUpper(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            char[] characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                char character = characters[i];
                if (character >= 'a' && character <= 'z')
                {
                    characters[i] = (char)(character - ('a' - 'A'));
                }
            }

            return new string(characters);
        }

        private static string ConvertAsciiToLower(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            char[] characters = value.ToCharArray();
            for (int i = 0; i < characters.Length; i++)
            {
                char character = characters[i];
                if (character >= 'A' && character <= 'Z')
                {
                    characters[i] = (char)(character + ('a' - 'A'));
                }
            }

            return new string(characters);
        }

        private static Dictionary<string, string> CreateSupportedLanguages()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "en-US", "英语(美国)" },
            { "es-ES", "西班牙语(西班牙)" },
            { "pt-BR", "葡萄牙语(巴西)" },
            { "de-DE", "德语(德国)" },
            { "fr-FR", "法语(法国)" },
            { "ja-JP", "日语(日本)" },
            { "ko-KR", "韩语(韩国)" },
            { "ru-RU", "俄语(俄罗斯)" },
            { "ar-SA", "阿拉伯语(沙特阿拉伯)" },
            { "bg-BG", "保加利亚语(保加利亚)" },
            { "cs-CZ", "捷克语(捷克)" },
            { "da-DK", "丹麦语(丹麦)" },
            { "el-GR", "希腊语(希腊)" },
            { "fi-FI", "芬兰语(芬兰)" },
            { "he-IL", "希伯来语(以色列)" },
            { "hr-HR", "克罗地亚语(克罗地亚)" },
            { "hu-HU", "匈牙利语(匈牙利)" },
            { "id-ID", "印尼语(印度尼西亚)" },
            { "it-IT", "意大利语(意大利)" },
            { "lt-LT", "立陶宛语(立陶宛)" },
            { "ms-MY", "马来语(马来西亚)" },
            { "nl-NL", "荷兰语(荷兰)" },
            { "pl-PL", "波兰语(波兰)" },
            { "ro-RO", "罗马尼亚语(罗马尼亚)" },
            { "sk-SK", "斯洛伐克语(斯洛伐克)" },
            { "sv-SE", "瑞典语(瑞典)" },
            { "th-TH", "泰语(泰国)" },
            { "tr-TR", "土耳其语(土耳其)" },
            { "uk-UA", "乌克兰语(乌克兰)" },
            { "vi-VN", "越南语(越南)" },
            { "zh-CN", "简中(中文简体)" },
            { "nb-NO", "挪威语(挪威)" },
            { "az-AZ", "阿塞拜疆语(阿塞拜疆)" },
            { "be-BY", "白俄罗斯语(白俄罗斯)" },
            { "kk-KZ", "哈萨克语(哈萨克斯坦)" },
            { "uz-UZ", "乌兹别克语(乌兹别克斯坦)" },
            { "tl-PH", "他加禄语(菲律宾)" },
            { "zh-TW", "繁中(中文繁体)" },
            { "is-IS", "冰岛语(冰岛)" },
            { "hi-IN", "印地语(印度)" },
        };
        }
    }

    [Serializable]
    public sealed class LanguageItem
    {
        public string itemName;
        public Transform root;
        public List<LanguageItemConfig> config = new List<LanguageItemConfig>();

        public bool ActivateMatchingObject(string languageCode)
        {
            if (config == null || config.Count == 0)
            {
                return false;
            }

            GameObject firstAvailable = null;
            GameObject selected = null;

            for (int i = 0; i < config.Count; i++)
            {
                LanguageItemConfig configItem = config[i];
                if (configItem?.img == null)
                {
                    continue;
                }

                firstAvailable ??= configItem.img;
                if (
                    selected == null
                    && string.Equals(configItem.code, languageCode, StringComparison.OrdinalIgnoreCase)
                )
                {
                    selected = configItem.img;
                }
            }

            selected ??= firstAvailable;

            for (int i = 0; i < config.Count; i++)
            {
                GameObject imageObject = config[i]?.img;
                if (imageObject == null)
                {
                    continue;
                }

                bool shouldBeActive = imageObject == selected;
                if (imageObject.activeSelf != shouldBeActive)
                {
                    imageObject.SetActive(shouldBeActive);
                }
            }

            return selected != null;
        }

        public int AutoBindChildObjects()
        {
            if (root == null || config == null)
            {
                Debug.LogWarning($"多语言项“{itemName}”没有配置 Root，无法自动查找。");
                return 0;
            }

            int foundCount = 0;
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < config.Count; i++)
            {
                LanguageItemConfig configItem = config[i];
                if (configItem == null || configItem.img != null)
                {
                    continue;
                }

                string objectName = GetObjectName(configItem);
                for (int transformIndex = 0; transformIndex < descendants.Length; transformIndex++)
                {
                    Transform candidate = descendants[transformIndex];
                    if (
                        candidate != root
                        && string.Equals(candidate.name, objectName, StringComparison.Ordinal)
                    )
                    {
                        configItem.img = candidate.gameObject;
                        foundCount++;
                        break;
                    }
                }

                if (configItem.img == null)
                {
                    Debug.LogWarning(
                        $"多语言项“{itemName}”在 Root“{root.name}”下未找到“{objectName}”。"
                    );
                }
            }

            return foundCount;
        }

        public LanguageItemConfig FindLanguageConfiguration(string languageCode)
        {
            if (config == null)
            {
                return null;
            }

            for (int i = 0; i < config.Count; i++)
            {
                LanguageItemConfig configItem = config[i];
                if (
                    configItem != null
                    && string.Equals(
                        configItem.code,
                        languageCode,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return configItem;
                }
            }

            return null;
        }

        private static string GetObjectName(LanguageItemConfig configItem)
        {
            string displayName = string.IsNullOrWhiteSpace(configItem.debugShow)
                ? configItem.code
                : configItem.debugShow;
            int bracketIndex = displayName.IndexOf('(');
            return bracketIndex > 0 ? displayName.Substring(0, bracketIndex) : displayName;
        }
    }

    [Serializable]
    public sealed class LanguageItemConfig
    {
        public string debugShow;
        public string code;
        public GameObject img;
    }
}