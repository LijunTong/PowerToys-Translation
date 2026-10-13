// Copyright (c) Joont. All rights reserved.
// Licensed under the MIT license.

namespace Community.PowerToys.Run.Plugin.Translate
{
    /// <summary>
    /// All user facing strings. Kept in one place so the wording can be reviewed and localized
    /// without hunting through the logic.
    /// </summary>
    internal static class PluginStrings
    {
        public const string PluginName = "翻译";

        public const string PluginDescription = "使用百度翻译 API 进行多语互译，回车复制译文。";

        /// <summary>Shown by the fast query pass while the delayed pass performs the request.</summary>
        public const string PlaceholderTitle = "正在翻译…";

        public const string UsageTitle = "翻译 · 用法";

        public const string UsageSubTitle =
            "tr <文本>  自动判断方向   ·   tr en:ja <文本>  指定源与目标   ·   tr :ja <文本>  仅指定目标语言";

        public const string MissingTextTitle = "缺少要翻译的内容";

        public const string SameLanguageTitle = "源语言与目标语言相同";

        public const string NotConfiguredTitle = "尚未配置百度翻译 API 密钥";

        public const string NotConfiguredSubTitle =
            "运行仓库根目录的 install.ps1 -Reconfigure（选择 baidu 并填入 appid 与 secretKey），或直接编辑插件目录下的 config.json";

        public const string ConfigInvalidPrefix = "config.json 有误已回退默认值：";

        public const string TranslateFailedTitle = "翻译失败";

        public const string ClipboardFailedTitle = "复制失败";

        public const string ClipboardFailedSubTitle = "剪贴板被其他程序占用，请重试。";

        public const string TruncatedNote = "已截断";

        public const string CachedNote = "缓存";

        public const string CancelledTitle = "已取消（输入已更新）";

        public const string ContextMenuCopyTranslation = "复制译文";

        public const string ContextMenuCopySource = "复制原文";

        public const string ContextMenuSwap = "交换语言并重译";

        public const string ContextMenuOpenWeb = "打开百度翻译网页";

        public const string ContextMenuOpenConfigFolder = "打开配置文件夹";

        public const string ActionCopyTranslation = "回车复制译文";

        /// <summary>Usage line listing the languages the inline syntax accepts.</summary>
        public static string SupportedLanguages => "支持的语言代码：" + LanguageCatalog.BuildHelpList();
    }
}
