namespace DouyinDownloader.Models;

public enum WorkType
{
    Video,
    Gallery
}

public sealed class WorkInfo
{
    public required string AwemeId { get; init; }

    public required string Title { get; init; }

    public required string Author { get; init; }

    public required WorkType Type { get; init; }

    public string? CoverUrl { get; init; }

    public string? VideoId { get; init; }

    public string? FallbackPlayUrl { get; init; }

    public IReadOnlyList<string> ImageUrls { get; init; } = [];

    public string TypeLabel => Type == WorkType.Gallery ? "图集" : "视频";

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "(无标题)" : Title;

    public string DisplayAuthor => string.IsNullOrWhiteSpace(Author) ? "未知作者" : Author;

    public bool IsVideo => Type == WorkType.Video;

    public IReadOnlyList<string> GetVideoUrls(string ratio)
    {
        var quality = string.IsNullOrWhiteSpace(ratio) ? "1080p" : ratio;
        var urls = new List<string>();

        if (!string.IsNullOrWhiteSpace(VideoId) && !VideoId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var id = Uri.EscapeDataString(VideoId);
            urls.Add($"https://aweme.snssdk.com/aweme/v1/play/?video_id={id}&ratio={quality}&line=0");
            urls.Add($"https://www.iesdouyin.com/aweme/v1/play/?video_id={id}&ratio={quality}&line=0");
        }

        if (!string.IsNullOrEmpty(FallbackPlayUrl))
        {
            AddUnique(urls, FallbackPlayUrl);
        }

        return urls;
    }

    private static void AddUnique(List<string> urls, string url)
    {
        if (!urls.Contains(url, StringComparer.Ordinal))
        {
            urls.Add(url);
        }
    }
}
