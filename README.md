# 抖音无水印下载

Windows 桌面工具：把抖音 App 里的分享文案贴进去，解析后即可保存视频或图集。视频走公开分享页的播放地址，图集保存原图。

当前版本：**v1.0.0**  
系统要求：Windows 10/11 x64。Release 包已自带运行时，不必单独安装 .NET。

## 功能

- 从完整分享文案中自动提取 `v.douyin.com` 短链或 `iesdouyin.com` 链接
- 支持视频和图集两种作品
- 视频可选 **1080p / 720p / 540p**
- 解析后显示封面、标题、作者、作品 ID
- 点击封面：按所选清晰度下载，并用系统默认播放器打开预览
- 记住上次保存目录（默认 `Downloads\抖音下载`）
- 同清晰度文件已存在时跳过下载
- 显示下载进度；下载完成后可在资源管理器中定位文件

## 使用方法

### 1. 安装

到 [Releases](https://github.com/crl/douyin-downloader/releases) 下载 `DouyinDownloader-v*-win-x64.zip`，解压后运行 `DouyinDownloader.exe`。

### 2. 复制分享内容

在抖音 App 打开作品 → 分享 → 复制链接。得到的一般是一段带短链的文案，例如：

```
6- 长按复制此条消息，打开抖音搜索，查看完整视频。 https://v.douyin.com/xxxxxxxx/
```

整段贴进输入框即可，不必自己抠出 URL。也支持只粘贴纯链接。

### 3. 解析并下载

1. 点击 **解析作品**
2. 确认封面、标题、作者无误
3. 视频可选择清晰度；图集没有清晰度选项
4. 需要改保存位置时点 **选择目录**
5. 点 **下载**，或点击封面预览
6. 完成后可用 **打开目录** 定位到文件

文件名大致为：

- 视频：`作者_标题_作品ID_清晰度.mp4`
- 图集：`作者_标题_作品ID_01.jpg` …

保存目录会写入 `%LOCALAPPDATA%\DouyinDownloader\settings.json`，下次启动自动恢复。

输入框内可用 `Ctrl+V` 粘贴。

## 项目结构

```
douyin/
├── DouyinDownloader.slnx          # 解决方案
├── .github/workflows/release.yml  # 打 tag 后自动发布 Release
└── src/DouyinDownloader/          # WPF 主项目（.NET 8）
    ├── App.xaml                   # 应用入口与全局样式
    ├── MainWindow.xaml            # 主界面
    ├── Assets/                    # 应用图标
    ├── Models/
    │   └── WorkInfo.cs            # 视频 / 图集数据
    ├── ViewModels/
    │   └── MainViewModel.cs       # 解析、下载、预览、目录
    ├── Services/
    │   ├── DouyinClient.cs        # 提取链接、解析分享页
    │   ├── DownloadService.cs     # 保存视频与图集
    │   └── DouyinException.cs
    └── Helpers/
        ├── AppSettings.cs         # 保存目录
        └── FileNameHelper.cs      # 文件名清理
```

解析流程：从文案取出链接 → 跟随短链到公开分享页 → 读取页面中的作品信息 → 再下载视频或图片。

## 本地构建

需要 Windows，以及 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（或更高、可 roll-forward 的运行时）。

```powershell
dotnet build DouyinDownloader.slnx -c Release
dotnet run --project src/DouyinDownloader/DouyinDownloader.csproj -c Release
```

自包含发布：

```powershell
dotnet publish src/DouyinDownloader/DouyinDownloader.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true
```

## 发布新版本

推送符合 `v*.*.*` 的标签后，GitHub Actions 会编译、打包 zip，并创建 Release：

```powershell
git tag v1.0.1
git push origin v1.0.1
```

也可在仓库的 Actions 里手动运行 **Release** 工作流。

## 说明

仅适用于公开可访问的分享链接。登录态、私密作品、直播等场景不支持。请只下载自己有权保存的内容，并遵守抖音用户协议与当地法律法规。
