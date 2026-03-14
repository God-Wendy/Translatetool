using System.Text;
using System.Text.Json;
using System.Web;
using TranslateTool.Models;

namespace TranslateTool.Providers;

public sealed class BaiduOcrProvider(HttpClient httpClient) : IOcrProvider
{
    private readonly HttpClient _httpClient = httpClient;
    private string _token = string.Empty;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public async Task<OcrResult> RecognizeAsync(OcrRequest request, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(settings, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new OcrResult(false, string.Empty, "baidu", "无法获取百度 OCR access_token。");
        }

        var imageBase64 = Convert.ToBase64String(request.ImageBytes);
        var form = $"image={HttpUtility.UrlEncode(imageBase64)}";
        using var content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded");

        var url = $"https://aip.baidubce.com/rest/2.0/ocr/v1/general_basic?access_token={token}";
        using var response = await _httpClient.PostAsync(url, content, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new OcrResult(false, string.Empty, "baidu", $"HTTP {(int)response.StatusCode}: {payload}");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("error_msg", out var error))
        {
            return new OcrResult(false, string.Empty, "baidu", error.GetString() ?? "未知错误");
        }

        if (!root.TryGetProperty("words_result", out var wordsResult))
        {
            return new OcrResult(false, string.Empty, "baidu", "未识别到任何文本。");
        }

        var lines = wordsResult.EnumerateArray()
            .Select(x => x.TryGetProperty("words", out var w) ? w.GetString() : string.Empty)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (lines.Count == 0)
        {
            return new OcrResult(false, string.Empty, "baidu", "OCR 结果为空。");
        }

        return new OcrResult(true, string.Join(Environment.NewLine, lines), "baidu", string.Empty);
    }

    private async Task<string> GetAccessTokenAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_token) && _tokenExpiry > DateTimeOffset.Now.AddMinutes(1))
        {
            return _token;
        }

        var query = HttpUtility.ParseQueryString(string.Empty);
        query["grant_type"] = "client_credentials";
        query["client_id"] = settings.BaiduOcrApiKey;
        query["client_secret"] = settings.BaiduOcrSecretKey;

        var tokenUrl = $"https://aip.baidubce.com/oauth/2.0/token?{query}";
        using var response = await _httpClient.GetAsync(tokenUrl, cancellationToken);
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return string.Empty;
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        if (!root.TryGetProperty("access_token", out var token))
        {
            return string.Empty;
        }

        _token = token.GetString() ?? string.Empty;
        var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 0;
        _tokenExpiry = DateTimeOffset.Now.AddSeconds(Math.Max(60, expiresIn));
        return _token;
    }
}
