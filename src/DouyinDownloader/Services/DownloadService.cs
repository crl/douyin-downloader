using System.IO;
using System.Net.Http;
using DouyinDownloader.Models;

namespace DouyinDownloader.Services;

public sealed class DownloadProgress
{
    public DownloadProgress(long bytesReceived, long? totalBytes, string? message = null)
    {
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
        Message = message;
    }

    public long BytesReceived { get; }

    public long? TotalBytes { get; }

    public string? Message { get; }

    public double? Percent => TotalBytes is > 0 ? Math.Min(100, BytesReceived * 100.0 / TotalBytes.Value) : null;
}

public sealed class DownloadResult
{
    public required IReadOnlyList<string> Files { get; init; }

    public string PrimaryPath => Files[0];

    public string Directory => Path.GetDirectoryName(PrimaryPath) ?? string.Empty;
}

public sealed class DownloadService
{
    private readonly DouyinClient _client;

    public DownloadService(DouyinClient client)
    {
        _client = client;
    }

    public async Task<DownloadResult> DownloadAsync(
        WorkInfo work,
        string directory,
        string ratio = "1080p",
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);

        if (work.Type == WorkType.Gallery)
        {
            return await DownloadImagesAsync(work, directory, progress, cancellationToken).ConfigureAwait(false);
        }

        var quality = string.IsNullOrWhiteSpace(ratio) ? "1080p" : ratio;
        var fileName = Helpers.FileNameHelper.BuildVideoFileName(work.Author, work.Title, work.AwemeId, quality);
        var filePath = Path.Combine(directory, fileName);

        if (File.Exists(filePath) && new FileInfo(filePath).Length > 1024)
        {
            progress?.Report(new DownloadProgress(0, null, "已存在该清晰度文件，跳过下载。"));
            return new DownloadResult { Files = [filePath] };
        }

        var urls = work.GetVideoUrls(quality);
        Exception? lastError = null;

        for (var i = 0; i < urls.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new DownloadProgress(0, null, $"正在下载 {quality}（线路 {i + 1}/{urls.Count}）…"));
            try
            {
                await DownloadFileAsync(urls[i], filePath, progress, cancellationToken, requireVideo: true)
                    .ConfigureAwait(false);
                return new DownloadResult { Files = [filePath] };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastError = ex;
                TryDelete(filePath);
            }
        }

        throw lastError ?? new DouyinException("下载失败：没有可用的视频地址。");
    }

    private async Task<DownloadResult> DownloadImagesAsync(
        WorkInfo work,
        string directory,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var total = work.ImageUrls.Count;

        for (var i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = work.ImageUrls[i];
            var ext = Helpers.FileNameHelper.GuessImageExtension(url);
            var fileName = Helpers.FileNameHelper.BuildImageFileName(work.Author, work.Title, work.AwemeId, i + 1, total, ext);
            var filePath = Path.Combine(directory, fileName);
            if (File.Exists(filePath) && new FileInfo(filePath).Length > 1024)
            {
                files.Add(filePath);
                continue;
            }

            progress?.Report(new DownloadProgress(0, null, $"正在下载图集 {i + 1}/{total}…"));
            await DownloadFileAsync(url, filePath, progress, cancellationToken, requireVideo: false).ConfigureAwait(false);
            files.Add(filePath);
        }

        return new DownloadResult { Files = files };
    }

    private async Task DownloadFileAsync(
        string url,
        string filePath,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken,
        bool requireVideo)
    {
        using var request = DouyinClient.CreateMediaRequest(url);
        using var response = await _client.DownloadHttp
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new DouyinException($"下载失败（HTTP {(int)response.StatusCode}）。");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("html", StringComparison.OrdinalIgnoreCase) ||
            mediaType.Contains("text", StringComparison.OrdinalIgnoreCase))
        {
            throw new DouyinException("下载地址返回了网页而不是媒体文件，可能已失效。");
        }

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var destination = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long received = 0;
        int read;
        var headerChecked = !requireVideo;

        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (!headerChecked)
            {
                if (!LooksLikeMp4(buffer.AsSpan(0, read)))
                {
                    throw new DouyinException("下载内容不是有效的 MP4 视频。");
                }

                headerChecked = true;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new DownloadProgress(received, total));
        }

        if (received < 1024)
        {
            throw new DouyinException("下载文件过小，可能不是有效媒体。");
        }
    }

    private static bool LooksLikeMp4(ReadOnlySpan<byte> header)
    {
        if (header.Length < 8)
        {
            return true;
        }

        // ISO BMFF: size(4) + 'ftyp'
        for (var i = 0; i <= header.Length - 4; i++)
        {
            if (header[i] == (byte)'f' &&
                header[i + 1] == (byte)'t' &&
                header[i + 2] == (byte)'y' &&
                header[i + 3] == (byte)'p')
            {
                return true;
            }
        }

        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }
}
