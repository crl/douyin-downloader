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

    public IReadOnlyList<VideoQuality> Qualities { get; init; } = [];

    public string TypeLabel => Type == WorkType.Gallery ? "图集" : "视频";

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "(无标题)" : Title;

    public string DisplayAuthor => string.IsNullOrWhiteSpace(Author) ? "未知作者" : Author;

    public bool IsVideo => Type == WorkType.Video;

    public WorkInfo WithQualities(IReadOnlyList<VideoQuality> qualities)
        => new()
        {
            AwemeId = AwemeId,
            Title = Title,
            Author = Author,
            Type = Type,
            CoverUrl = CoverUrl,
            VideoId = VideoId,
            FallbackPlayUrl = FallbackPlayUrl,
            ImageUrls = ImageUrls,
            Qualities = qualities
        };

    public IReadOnlyList<string> GetVideoUrls(string ratio)
    {
        var selected = Qualities.FirstOrDefault(item =>
                          string.Equals(item.Id, ratio, StringComparison.OrdinalIgnoreCase))
                      ?? Qualities.FirstOrDefault();
        var ratioParam = selected?.Ratio ?? (string.IsNullOrWhiteSpace(ratio) ? "1080p" : ratio);
        var urls = new List<string>();

        if (!string.IsNullOrWhiteSpace(VideoId) && !VideoId.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(selected?.Query))
            {
                foreach (var url in VideoQuality.PlayApiUrls(VideoId, selected.Query))
                {
                    AddUnique(urls, url);
                }
            }

            foreach (var url in VideoQuality.PlayApiUrls(VideoId, $"ratio={ratioParam}&line=0"))
            {
                AddUnique(urls, url);
            }

            if (selected is { Rank: >= 2160 } ||
                string.Equals(ratioParam, "4k", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ratioParam, "4K", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var url in VideoQuality.PlayApiUrls(VideoId, VideoQuality.OriginalQuery))
                {
                    AddUnique(urls, url);
                }

                foreach (var extra in new[] { "4k", "2160p" })
                {
                    foreach (var url in VideoQuality.PlayApiUrls(VideoId, $"ratio={extra}&line=0"))
                    {
                        AddUnique(urls, url);
                    }
                }
            }
        }

        if (selected is not null)
        {
            foreach (var url in selected.DirectUrls)
            {
                AddUnique(urls, url);
            }
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
