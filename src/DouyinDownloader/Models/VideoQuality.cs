namespace DouyinDownloader.Models;

public readonly record struct QualityProbe(
    string Query,
    string Id,
    string Label,
    int Rank);

public sealed class VideoQuality
{
    public const string OriginalQuery =
        "ratio=default&line=0&watermark=0&media_type=4&vr_type=0&improve_bitrate=1&is_play_url=1&source=PackSourceEnum_AWEME_DETAIL";

    public required string Id { get; init; }

    public required string Label { get; init; }

    public required int Rank { get; init; }

    public required string Ratio { get; init; }

    public string? Query { get; init; }

    public long? SizeBytes { get; init; }

    public IReadOnlyList<string> DirectUrls { get; init; } = [];

    public int? Width { get; init; }

    public int? Height { get; init; }

    public string DisplayLabel => SizeBytes is > 0
        ? $"{Label}  {FormatSize(SizeBytes.Value)}"
        : Label;

    public static readonly IReadOnlyList<QualityProbe> ProbeCandidates =
    [
        new("ratio=540p&line=0", "540p", "540p", 540),
        new("ratio=720p&line=0", "720p", "720p", 720),
        new("ratio=1080p&line=0", "1080p", "1080p", 1080),
        new("ratio=2k&line=0", "2K", "2K", 1440),
        new("ratio=4k&line=0", "4K", "4K", 2160),
        new("ratio=2160p&line=0", "4K", "4K", 2160),
        new(OriginalQuery, "4K", "4K", 4000)
    ];

    public static VideoQuality FromDimensions(
        int? width,
        int? height,
        string? gearName = null,
        IReadOnlyList<string>? urls = null)
    {
        var (id, label, rank, ratio) = Classify(width, height, gearName);
        return new VideoQuality
        {
            Id = id,
            Label = label,
            Rank = rank,
            Ratio = ratio,
            Query = rank >= 2160 ? OriginalQuery : $"ratio={ratio}&line=0",
            DirectUrls = urls ?? [],
            Width = width,
            Height = height
        };
    }

    public static (string Id, string Label, int Rank, string Ratio) Classify(
        int? width,
        int? height,
        string? gearName)
    {
        var gear = gearName?.ToLowerInvariant() ?? string.Empty;
        if (gear.Contains("4k", StringComparison.Ordinal) || gear.Contains("2160", StringComparison.Ordinal))
        {
            return ("4K", "4K", 2160, "4k");
        }

        if (gear.Contains("2k", StringComparison.Ordinal) || gear.Contains("1440", StringComparison.Ordinal))
        {
            return ("2K", "2K", 1440, "2k");
        }

        if (gear.Contains("1080", StringComparison.Ordinal))
        {
            return ("1080p", "1080p", 1080, "1080p");
        }

        if (gear.Contains("720", StringComparison.Ordinal))
        {
            return ("720p", "720p", 720, "720p");
        }

        if (gear.Contains("540", StringComparison.Ordinal))
        {
            return ("540p", "540p", 540, "540p");
        }

        if (gear.Contains("360", StringComparison.Ordinal))
        {
            return ("360p", "360p", 360, "360p");
        }

        var shortSide = width is > 0 && height is > 0
            ? Math.Min(width.Value, height.Value)
            : Math.Max(width ?? 0, height ?? 0);
        var longSide = Math.Max(width ?? 0, height ?? 0);

        if (shortSide >= 2160 || longSide >= 3840)
        {
            return ("4K", "4K", 2160, "4k");
        }

        if (shortSide >= 1440 || longSide >= 2560)
        {
            return ("2K", "2K", 1440, "2k");
        }

        if (shortSide >= 1080)
        {
            return ("1080p", "1080p", 1080, "1080p");
        }

        if (shortSide >= 720)
        {
            return ("720p", "720p", 720, "720p");
        }

        if (shortSide >= 540)
        {
            return ("540p", "540p", 540, "540p");
        }

        if (shortSide >= 360)
        {
            return ("360p", "360p", 360, "360p");
        }

        if (shortSide > 0)
        {
            return ($"{shortSide}p", $"{shortSide}p", shortSide, $"{shortSide}p");
        }

        return ("1080p", "1080p", 1080, "1080p");
    }

    public static IReadOnlyList<string> PlayApiUrls(string videoId, string query)
    {
        var id = Uri.EscapeDataString(videoId);
        var q = query.Contains("video_id=", StringComparison.Ordinal)
            ? query
            : $"video_id={id}&{query.TrimStart('&')}";

        return
        [
            $"https://www.douyin.com/aweme/v1/play/?{q}",
            $"https://aweme.snssdk.com/aweme/v1/play/?{q}",
            $"https://www.iesdouyin.com/aweme/v1/play/?{q}"
        ];
    }

    public VideoQuality MergeFrom(VideoQuality other)
    {
        var urls = DirectUrls.Concat(other.DirectUrls).Distinct(StringComparer.Ordinal).ToArray();
        var preferOther = other.Rank > Rank;
        return new VideoQuality
        {
            Id = preferOther ? other.Id : Id,
            Label = preferOther ? other.Label : Label,
            Rank = Math.Max(Rank, other.Rank),
            Ratio = preferOther ? other.Ratio : Ratio,
            Query = preferOther ? other.Query ?? Query : Query ?? other.Query,
            SizeBytes = SizeBytes is > 0 && other.SizeBytes is > 0
                ? Math.Max(SizeBytes.Value, other.SizeBytes.Value)
                : SizeBytes ?? other.SizeBytes,
            DirectUrls = urls,
            Width = Width ?? other.Width,
            Height = Height ?? other.Height
        };
    }

    public static string FormatSize(long bytes)
    {
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB"];
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
