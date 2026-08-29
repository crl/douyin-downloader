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

    public IReadOnlyList<string> VideoUrls { get; init; } = [];

    public IReadOnlyList<string> ImageUrls { get; init; } = [];

    public string TypeLabel => Type == WorkType.Gallery ? "图集" : "视频";

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "(无标题)" : Title;

    public string DisplayAuthor => string.IsNullOrWhiteSpace(Author) ? "未知作者" : Author;
}
