using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using DouyinDownloader.Models;

namespace DouyinDownloader.Services;

public sealed class DouyinClient : IDisposable
{
    public const string MobileUserAgent =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 16_6 like Mac OS X) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/16.6 Mobile/15E148 Safari/604.1";

    private const int MaxRedirects = 8;

    private static readonly Regex UrlRegex = new(
        @"https?://(?:[A-Za-z0-9-]+\.)*(?:douyin|iesdouyin)\.com/[A-Za-z0-9_.?=&%/\-]*[A-Za-z0-9_/\-]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PathIdRegex = new(
        @"/(?:video|note|slides)/(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] ChallengeMarkers =
    [
        "waf-jschallenge",
        "out-sha256.js",
        "byted_acrawler"
    ];

    private static readonly HashSet<int> PhotoAwemeTypes = [2, 68];

    private readonly HttpClient _http;
    private readonly HttpClient _probe;
    private readonly HttpClientHandler _httpHandler;
    private readonly HttpClientHandler _probeHandler;

    public DouyinClient()
    {
        var cookies = new CookieContainer();

        _probeHandler = CreateHandler(cookies, allowRedirect: false);
        _probe = new HttpClient(_probeHandler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        ApplyDefaultHeaders(_probe);

        _httpHandler = CreateHandler(cookies, allowRedirect: true);
        _http = new HttpClient(_httpHandler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        ApplyDefaultHeaders(_http);
    }

    public HttpClient DownloadHttp => _http;

    public static string? ExtractShareUrl(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = UrlRegex.Match(text);
        return match.Success ? match.Value : null;
    }

    public async Task<WorkInfo> ParseAsync(string shareText, CancellationToken cancellationToken = default)
    {
        var url = ExtractShareUrl(shareText)
                  ?? throw new DouyinException("未在文本中找到抖音链接。请粘贴完整分享文案或 v.douyin.com 短链。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(45));

        try
        {
            var resolved = await ResolveShareAsync(url, timeout.Token).ConfigureAwait(false);
            var html = await FetchShareHtmlAsync(resolved, timeout.Token).ConfigureAwait(false);
            return ParseShareHtml(html, resolved.AwemeId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DouyinException("解析超时，请检查网络后重试。");
        }
        catch (HttpRequestException ex)
        {
            throw new DouyinException("网络请求失败，请检查网络后重试。", ex);
        }
    }

    public async Task<byte[]?> DownloadBytesAsync(string url, CancellationToken cancellationToken = default)
    {
        using var request = CreateMediaRequest(url);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _http.Dispose();
        _probe.Dispose();
    }

    internal static bool LooksLikeChallenge(string html)
        => ChallengeMarkers.Any(marker => html.Contains(marker, StringComparison.OrdinalIgnoreCase));

    internal static string? ExtractRouterJson(string html)
    {
        const string marker = "_ROUTER_DATA";
        var markerIndex = html.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return null;
        }

        var jsonStart = html.IndexOf('{', markerIndex);
        if (jsonStart < 0)
        {
            return null;
        }

        var scriptEnd = html.IndexOf("</script>", jsonStart, StringComparison.OrdinalIgnoreCase);
        if (scriptEnd < 0)
        {
            return null;
        }

        return html[jsonStart..scriptEnd].Trim().TrimEnd(';', ' ', '\r', '\n', '\t');
    }

    internal static WorkInfo ParseShareHtml(string html, string awemeId)
    {
        var json = ExtractRouterJson(html);
        if (string.IsNullOrEmpty(json))
        {
            if (LooksLikeChallenge(html))
            {
                throw new DouyinBlockedException("抖音返回了风控验证页，请稍后再试。");
            }

            throw new DouyinException("未能读取分享页数据，页面结构可能已变化。");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new DouyinException("分享页数据解析失败，页面结构可能已变化。", ex);
        }

        using (document)
        {
            var info = FindVideoInfo(document.RootElement)
                       ?? throw new DouyinException("分享页数据结构不符合预期，页面结构可能已变化。");
            var item = FirstItem(info, awemeId);
            return BuildWorkInfo(item, awemeId);
        }
    }

    private sealed record ResolvedShare(string AwemeId, string? ShareUrl);

    private async Task<ResolvedShare> ResolveShareAsync(string url, CancellationToken cancellationToken)
    {
        var current = url;
        for (var i = 0; i <= MaxRedirects; i++)
        {
            if (!IsDouyinHost(current))
            {
                throw new DouyinException("链接跳转到了非抖音站点，无法解析。");
            }

            var awemeId = ExtractAwemeId(current);
            if (!string.IsNullOrEmpty(awemeId))
            {
                return new ResolvedShare(awemeId, IsIesShareUrl(current) ? current : null);
            }

            var location = await GetRedirectLocationAsync(current, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(location))
            {
                break;
            }

            current = location;
        }

        throw new DouyinException("无法从分享链接中解析作品 ID。请确认链接有效且未过期。");
    }

    private static bool IsIesShareUrl(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri)
           && uri.Host.Contains("iesdouyin.com", StringComparison.OrdinalIgnoreCase)
           && uri.AbsolutePath.Contains("/share/", StringComparison.OrdinalIgnoreCase);

    private async Task<string?> GetRedirectLocationAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _probe.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if ((int)response.StatusCode is >= 300 and < 400)
        {
            var location = response.Headers.Location;
            if (location is null)
            {
                return null;
            }

            return location.IsAbsoluteUri ? location.ToString() : new Uri(new Uri(url), location).ToString();
        }

        return null;
    }

    private async Task<string> FetchShareHtmlAsync(ResolvedShare resolved, CancellationToken cancellationToken)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrEmpty(resolved.ShareUrl))
        {
            candidates.Add(resolved.ShareUrl);
        }

        candidates.Add($"https://www.iesdouyin.com/share/video/{resolved.AwemeId}/");
        candidates.Add($"https://www.iesdouyin.com/share/note/{resolved.AwemeId}/");

        string? lastHtml = null;
        foreach (var url in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var html = await GetHtmlAsync(url, cancellationToken).ConfigureAwait(false);
            lastHtml = html;
            if (html.Contains("videoInfoRes", StringComparison.Ordinal) &&
                html.Contains("item_list", StringComparison.Ordinal))
            {
                return html;
            }
        }

        // 首次请求有时只有壳页，带上已写入的 cookie 再拉一次。
        var retryHtml = await GetHtmlAsync(candidates[0], cancellationToken).ConfigureAwait(false);
        return retryHtml.Contains("videoInfoRes", StringComparison.Ordinal) ? retryHtml : lastHtml ?? retryHtml;
    }

    private async Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if ((int)response.StatusCode is >= 400)
        {
            if (LooksLikeChallenge(html))
            {
                throw new DouyinBlockedException("抖音返回了风控验证页，请稍后再试。");
            }

            throw new DouyinException($"获取分享页失败（HTTP {(int)response.StatusCode}）。");
        }

        return html;
    }

    private static WorkInfo BuildWorkInfo(JsonElement item, string awemeId)
    {
        var title = GetString(item, "desc")?.Trim() ?? string.Empty;
        var author = string.Empty;
        if (item.TryGetProperty("author", out var authorEl) && authorEl.ValueKind == JsonValueKind.Object)
        {
            author = GetString(authorEl, "nickname")?.Trim() ?? string.Empty;
        }

        var images = new List<string>();
        if (item.TryGetProperty("images", out var imagesEl) && imagesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var image in imagesEl.EnumerateArray())
            {
                var url = LastUrl(image, "url_list");
                if (!string.IsNullOrEmpty(url))
                {
                    images.Add(url);
                }
            }
        }

        var awemeType = 0;
        if (item.TryGetProperty("aweme_type", out var typeEl) && typeEl.TryGetInt32(out var parsedType))
        {
            awemeType = parsedType;
        }

        var isGallery = PhotoAwemeTypes.Contains(awemeType) || images.Count > 0;
        var coverUrl = FindCoverUrl(item, images);
        string? videoId = null;
        string? fallbackPlayUrl = null;

        if (!isGallery)
        {
            (videoId, fallbackPlayUrl) = ExtractPlayInfo(item);
            if (string.IsNullOrWhiteSpace(videoId) && string.IsNullOrWhiteSpace(fallbackPlayUrl))
            {
                throw new DouyinException("未找到可下载的无水印视频地址。");
            }
        }

        if (isGallery && images.Count == 0)
        {
            throw new DouyinException("未找到图集图片地址。");
        }

        return new WorkInfo
        {
            AwemeId = awemeId,
            Title = title,
            Author = author,
            Type = isGallery ? WorkType.Gallery : WorkType.Video,
            CoverUrl = coverUrl,
            VideoId = videoId,
            FallbackPlayUrl = fallbackPlayUrl,
            ImageUrls = images
        };
    }

    private static (string? VideoId, string? FallbackPlayUrl) ExtractPlayInfo(JsonElement item)
    {
        if (!item.TryGetProperty("video", out var video) || video.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        if (!video.TryGetProperty("play_addr", out var playAddr) || playAddr.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        var uri = GetString(playAddr, "uri");
        if (string.IsNullOrWhiteSpace(uri) || uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            uri = null;
        }

        string? fallback = null;
        var playUrl = FirstUrl(playAddr, "url_list");
        if (!string.IsNullOrEmpty(playUrl))
        {
            fallback = playUrl.Replace("playwm", "play", StringComparison.OrdinalIgnoreCase);
        }

        return (uri, fallback);
    }

    private static string? FindCoverUrl(JsonElement item, IReadOnlyList<string> images)
    {
        if (item.TryGetProperty("video", out var video) && video.ValueKind == JsonValueKind.Object)
        {
            foreach (var key in new[] { "cover", "origin_cover", "dynamic_cover" })
            {
                if (video.TryGetProperty(key, out var cover) && cover.ValueKind == JsonValueKind.Object)
                {
                    var url = FirstUrl(cover, "url_list");
                    if (!string.IsNullOrEmpty(url))
                    {
                        return url;
                    }
                }
            }
        }

        return images.Count > 0 ? images[0] : null;
    }

    private static JsonElement? FindVideoInfo(JsonElement root)
    {
        if (!root.TryGetProperty("loaderData", out var loader) || loader.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var page in loader.EnumerateObject())
        {
            if (page.Value.ValueKind == JsonValueKind.Object &&
                page.Value.TryGetProperty("videoInfoRes", out var info) &&
                info.ValueKind == JsonValueKind.Object)
            {
                return info;
            }
        }

        return null;
    }

    private static JsonElement FirstItem(JsonElement info, string awemeId)
    {
        if (info.TryGetProperty("item_list", out var list) &&
            list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    return item;
                }
            }
        }

        if (info.TryGetProperty("filter_list", out var filters) &&
            filters.ValueKind == JsonValueKind.Array)
        {
            foreach (var filter in filters.EnumerateArray())
            {
                var reason = GetString(filter, "detail_msg")
                             ?? GetString(filter, "notice")
                             ?? GetString(filter, "filter_reason");
                throw new DouyinUnavailableException(
                    string.IsNullOrWhiteSpace(reason)
                        ? "该作品不可用，可能已删除、设为私密或受地区限制。"
                        : $"该作品不可用：{reason}");
            }
        }

        throw new DouyinUnavailableException($"未获取到作品 {awemeId}，可能已删除或无法访问。");
    }

    private static string? ExtractAwemeId(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("modal_id", out var modalId) && modalId.All(char.IsDigit))
        {
            return modalId;
        }

        var match = PathIdRegex.Match(uri.AbsolutePath);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        var trimmed = query.TrimStart('?');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
            result[key] = value;
        }

        return result;
    }

    private static bool IsDouyinHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        return host == "douyin.com"
               || host == "iesdouyin.com"
               || host.EndsWith(".douyin.com", StringComparison.Ordinal)
               || host.EndsWith(".iesdouyin.com", StringComparison.Ordinal);
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static string? FirstUrl(JsonElement parent, string listProperty)
    {
        if (!parent.TryGetProperty(listProperty, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var url = item.GetString();
                if (!string.IsNullOrEmpty(url))
                {
                    return url;
                }
            }
        }

        return null;
    }

    private static string? LastUrl(JsonElement parent, string listProperty)
    {
        if (!parent.TryGetProperty(listProperty, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? last = null;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var url = item.GetString();
                if (!string.IsNullOrEmpty(url))
                {
                    last = url;
                }
            }
        }

        return last;
    }

    private static HttpClientHandler CreateHandler(CookieContainer cookies, bool allowRedirect)
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = allowRedirect,
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = cookies,
            UseCookies = true,
            MaxConnectionsPerServer = 8
        };
    }

    private static void ApplyDefaultHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", MobileUserAgent);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.8");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    }

    internal static HttpRequestMessage CreateMediaRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Clear();
        request.Headers.TryAddWithoutValidation("User-Agent", MobileUserAgent);
        request.Headers.Referrer = new Uri("https://www.douyin.com/");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return request;
    }
}
