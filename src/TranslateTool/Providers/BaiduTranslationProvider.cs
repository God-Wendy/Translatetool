using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using TranslateTool.Models;

namespace TranslateTool.Providers;

public sealed class BaiduTranslationProvider(HttpClient httpClient) : ITranslationProvider
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<TranslationResult> TranslateAsync(TranslationRequest request, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var salt = Guid.NewGuid().ToString("N");
        var signRaw = $"{settings.BaiduTranslateAppId}{request.Text}{salt}{settings.BaiduTranslateKey}";
        var sign = ComputeMd5(signRaw);

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["q"] = request.Text;
        query["from"] = request.SourceLanguage;
        query["to"] = request.TargetLanguage;
        query["appid"] = settings.BaiduTranslateAppId;
        query["salt"] = salt;
        query["sign"] = sign;

        var uri = $"https://fanyi-api.baidu.com/api/trans/vip/translate?{query}";
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new TranslationResult(false, request.Text, string.Empty, "baidu", $"HTTP {(int)response.StatusCode}: {payload}");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("error_msg", out var error))
        {
            return new TranslationResult(false, request.Text, string.Empty, "baidu", error.GetString() ?? "未知错误");
        }

        if (!root.TryGetProperty("trans_result", out var results) || results.GetArrayLength() == 0)
        {
            return new TranslationResult(false, request.Text, string.Empty, "baidu", "百度翻译未返回结果。");
        }

        var merged = string.Join(Environment.NewLine, results.EnumerateArray().Select(x => x.GetProperty("dst").GetString()).Where(x => !string.IsNullOrWhiteSpace(x)));
        return new TranslationResult(true, request.Text, merged, "baidu", string.Empty);
    }

    private static string ComputeMd5(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = MD5.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.ToString();
    }
}
