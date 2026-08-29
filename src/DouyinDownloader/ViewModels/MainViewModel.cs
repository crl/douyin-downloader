using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DouyinDownloader.Helpers;
using DouyinDownloader.Models;
using DouyinDownloader.Services;
using Microsoft.Win32;

namespace DouyinDownloader.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly DouyinClient _client = new();
    private readonly DownloadService _downloader;
    private readonly AppSettings _settings;
    private CancellationTokenSource? _cts;

    public MainViewModel()
    {
        _downloader = new DownloadService(_client);
        _settings = AppSettings.Load();
        _saveDirectory = string.IsNullOrWhiteSpace(_settings.SaveDirectory)
            ? AppSettings.DefaultSaveDirectory
            : _settings.SaveDirectory!;
        _statusText = "粘贴抖音分享文案后点击解析。";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ParseCommand))]
    private string _shareText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ParseCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    [NotifyPropertyChangedFor(nameof(HasWork))]
    [NotifyPropertyChangedFor(nameof(ShowQualityOptions))]
    [NotifyPropertyChangedFor(nameof(ShowPlayOverlay))]
    private WorkInfo? _work;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCover))]
    private BitmapImage? _coverImage;

    [ObservableProperty]
    private string _saveDirectory;

    [ObservableProperty]
    private string _statusText;

    [ObservableProperty]
    private bool _isError;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _progressIsIndeterminate;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenFolderCommand))]
    private string? _lastSavedPath;

    [ObservableProperty]
    private string _selectedQuality = "1080p";

    [ObservableProperty]
    private bool _isInfoExpanded = true;

    [ObservableProperty]
    private bool _isPlaying;

    [ObservableProperty]
    private bool _isPlaybackVisible;

    [ObservableProperty]
    private Uri? _playbackUri;

    private bool _triedRemotePlay;
    private bool _handlingMediaFailed;

    public bool HasWork => Work is not null;

    public bool HasCover => CoverImage is not null;

    public bool ShowQualityOptions => Work is { IsVideo: true };

    public bool ShowPlayOverlay => Work is { IsVideo: true };

    private bool CanParse() => !IsBusy && !string.IsNullOrWhiteSpace(ShareText);

    private bool CanDownload() => !IsBusy && Work is not null;

    private bool CanOpenFolder() => !string.IsNullOrWhiteSpace(LastSavedPath) && File.Exists(LastSavedPath);

    [RelayCommand]
    private void PasteFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                ShareText = Clipboard.GetText();
                SetStatus("已从剪贴板粘贴，点击解析。");
            }
            else
            {
                SetStatus("剪贴板中没有文本。", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"读取剪贴板失败：{ex.Message}", isError: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanParse))]
    private async Task ParseAsync()
    {
        ResetWork();
        await RunBusyAsync("正在解析分享链接…", async ct =>
        {
            var work = await _client.ParseAsync(ShareText, ct).ConfigureAwait(true);
            Work = work;
            SetStatus(work.Type == WorkType.Gallery
                ? $"解析成功：图集共 {work.ImageUrls.Count} 张。"
                : "解析成功，选择清晰度后可下载，或把鼠标移到封面上点击播放。");
            await LoadCoverAsync(work.CoverUrl, ct).ConfigureAwait(true);
        }).ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(CanDownload))]
    private async Task DownloadAsync()
    {
        if (Work is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SaveDirectory))
        {
            SetStatus("请先选择保存目录。", isError: true);
            return;
        }

        await RunBusyAsync("准备下载…", async ct =>
        {
            ProgressIsIndeterminate = true;
            var progress = new Progress<DownloadProgress>(OnDownloadProgress);
            var result = await _downloader.DownloadAsync(Work, SaveDirectory, SelectedQuality, progress, ct).ConfigureAwait(true);
            LastSavedPath = result.PrimaryPath;
            ProgressIsIndeterminate = false;
            ProgressValue = 100;
            var count = result.Files.Count;
            SetStatus(count == 1
                ? $"下载完成：{result.PrimaryPath}"
                : $"下载完成：共 {count} 个文件，保存在 {result.Directory}");
        }).ConfigureAwait(true);
    }

    private bool CanPlayPause()
        => Work is { IsVideo: true } && (!IsBusy || IsPlaybackVisible);

    [RelayCommand(CanExecute = nameof(CanPlayPause))]
    private async Task PlayPauseAsync()
    {
        if (Work is not { IsVideo: true })
        {
            return;
        }

        if (IsPlaybackVisible && PlaybackUri is not null)
        {
            IsPlaying = !IsPlaying;
            return;
        }

        await StartPlaybackAsync().ConfigureAwait(true);
    }

    public async Task OnMediaFailedAsync()
    {
        if (_handlingMediaFailed)
        {
            return;
        }

        if (PlaybackUri is null)
        {
            return;
        }

        _handlingMediaFailed = true;
        try
        {
            if (PlaybackUri is { IsFile: true })
            {
                StopPlayback();
                SetStatus("无法播放该视频。", isError: true);
                return;
            }

            StopPlayback(keepRemoteAttempt: true);
            await StartPlaybackAsync().ConfigureAwait(true);
        }
        finally
        {
            _handlingMediaFailed = false;
        }
    }

    public void OnMediaEnded()
    {
        IsPlaying = false;
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择保存目录",
            InitialDirectory = Directory.Exists(SaveDirectory) ? SaveDirectory : AppSettings.DefaultSaveDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            SaveDirectory = dialog.FolderName;
            PersistSaveDirectory();
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpenFolder))]
    private void OpenFolder()
    {
        if (string.IsNullOrWhiteSpace(LastSavedPath) || !File.Exists(LastSavedPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{LastSavedPath}\"",
            UseShellExecute = true
        });
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _client.Dispose();
        GC.SuppressFinalize(this);
    }

    partial void OnSaveDirectoryChanged(string value) => PersistSaveDirectory();

    private async Task RunBusyAsync(string busyMessage, Func<CancellationToken, Task> action)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsBusy = true;
        ProgressValue = 0;
        ProgressIsIndeterminate = true;
        SetStatus(busyMessage);

        try
        {
            await action(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SetStatus("已取消。");
        }
        catch (DouyinException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        catch (HttpRequestException)
        {
            SetStatus("网络请求失败，请检查网络后重试。", isError: true);
        }
        catch (Exception ex)
        {
            SetStatus($"发生错误：{ex.Message}", isError: true);
        }
        finally
        {
            IsBusy = false;
            ProgressIsIndeterminate = false;
        }
    }

    private void OnDownloadProgress(DownloadProgress progress)
    {
        if (!string.IsNullOrEmpty(progress.Message) && progress.BytesReceived == 0)
        {
            SetStatus(progress.Message);
        }

        if (progress.Percent is { } percent)
        {
            ProgressIsIndeterminate = false;
            ProgressValue = percent;
            SetStatus($"正在下载 {DownloadService.FormatBytes(progress.BytesReceived)} / {DownloadService.FormatBytes(progress.TotalBytes!.Value)}（{percent:0.#}%）");
        }
        else if (progress.BytesReceived > 0)
        {
            ProgressIsIndeterminate = true;
            SetStatus($"正在下载 {DownloadService.FormatBytes(progress.BytesReceived)}…");
        }
    }

    private async Task LoadCoverAsync(string? url, CancellationToken cancellationToken)
    {
        CoverImage = null;
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            var bytes = await _client.DownloadBytesAsync(url, cancellationToken).ConfigureAwait(true);
            if (bytes is null or { Length: 0 })
            {
                return;
            }

            var image = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            CoverImage = image;
        }
        catch
        {
            CoverImage = null;
        }
    }

    private async Task StartPlaybackAsync()
    {
        if (Work is not { IsVideo: true })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SaveDirectory))
        {
            SetStatus("请先选择保存目录。", isError: true);
            return;
        }

        var localPath = TryGetLocalVideoPath();
        if (localPath is not null)
        {
            BeginPlayback(new Uri(localPath), "正在播放。");
            return;
        }

        if (!_triedRemotePlay)
        {
            var urls = Work.GetVideoUrls(SelectedQuality);
            if (urls.Count > 0 && Uri.TryCreate(urls[0], UriKind.Absolute, out var remote))
            {
                _triedRemotePlay = true;
                BeginPlayback(remote, "正在播放…");
                return;
            }

            _triedRemotePlay = true;
        }

        await RunBusyAsync("准备播放…", async ct =>
        {
            ProgressIsIndeterminate = true;
            var progress = new Progress<DownloadProgress>(OnDownloadProgress);
            var result = await _downloader.DownloadAsync(Work, SaveDirectory, SelectedQuality, progress, ct).ConfigureAwait(true);
            LastSavedPath = result.PrimaryPath;
            ProgressIsIndeterminate = false;
            ProgressValue = 100;
            BeginPlayback(new Uri(result.PrimaryPath), "正在播放。");
        }).ConfigureAwait(true);
    }

    private string? TryGetLocalVideoPath()
    {
        if (Work is null || string.IsNullOrWhiteSpace(SaveDirectory))
        {
            return null;
        }

        var fileName = FileNameHelper.BuildVideoFileName(Work.Author, Work.Title, Work.AwemeId, SelectedQuality);
        var filePath = Path.Combine(SaveDirectory, fileName);
        return File.Exists(filePath) && new FileInfo(filePath).Length > 1024 ? filePath : null;
    }

    private void BeginPlayback(Uri uri, string status)
    {
        PlaybackUri = uri;
        IsPlaybackVisible = true;
        IsPlaying = true;
        PlayPauseCommand.NotifyCanExecuteChanged();
        SetStatus(status);
    }

    private void StopPlayback(bool keepRemoteAttempt = false)
    {
        IsPlaying = false;
        IsPlaybackVisible = false;
        PlaybackUri = null;
        if (!keepRemoteAttempt)
        {
            _triedRemotePlay = false;
        }

        PlayPauseCommand.NotifyCanExecuteChanged();
    }

    private void ResetWork()
    {
        StopPlayback();
        Work = null;
        CoverImage = null;
        LastSavedPath = null;
        ProgressValue = 0;
        SelectedQuality = "1080p";
    }

    private void SetStatus(string text, bool isError = false)
    {
        StatusText = text;
        IsError = isError;
    }

    private void PersistSaveDirectory()
    {
        _settings.SaveDirectory = SaveDirectory;
        _settings.Save();
    }
}
