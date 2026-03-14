using TranslateTool.Models;

namespace TranslateTool.Services;

public static class LanguageCatalog
{
    public static readonly IReadOnlyList<LanguageOption> SourceLanguages =
    [
        new("auto", "自动检测"),
        new("zh", "中文"),
        new("en", "英语"),
        new("jp", "日语"),
        new("kor", "韩语"),
        new("fra", "法语"),
        new("de", "德语"),
        new("spa", "西班牙语"),
        new("ru", "俄语"),
        new("pt", "葡萄牙语"),
        new("it", "意大利语"),
        new("ara", "阿拉伯语"),
        new("th", "泰语"),
        new("vie", "越南语"),
        new("id", "印尼语")
    ];

    public static readonly IReadOnlyList<LanguageOption> TargetLanguages =
    [
        new("zh", "中文"),
        new("en", "英语"),
        new("jp", "日语"),
        new("kor", "韩语"),
        new("fra", "法语"),
        new("de", "德语"),
        new("spa", "西班牙语"),
        new("ru", "俄语"),
        new("pt", "葡萄牙语"),
        new("it", "意大利语"),
        new("ara", "阿拉伯语"),
        new("th", "泰语"),
        new("vie", "越南语"),
        new("id", "印尼语")
    ];

    public static LanguageOption FindOrDefault(string? code, bool target)
    {
        var options = target ? TargetLanguages : SourceLanguages;
        if (string.IsNullOrWhiteSpace(code))
        {
            return options[0];
        }

        var match = options.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
        return match ?? options[0];
    }
}
