using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DouyinDownloader.Helpers;

public static class FileNameHelper
{
    private static readonly Regex MultiUnderscore = new(@"_+", RegexOptions.Compiled);

    public static string Sanitize(string name, int maxLength = 80)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "douyin";
        }

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            if (ch < 32 || Path.GetInvalidFileNameChars().Contains(ch))
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(ch);
            }
        }

        var cleaned = MultiUnderscore.Replace(builder.ToString(), "_").Trim('_', ' ', '.');
        if (string.IsNullOrEmpty(cleaned))
        {
            return "douyin";
        }

        return cleaned.Length > maxLength ? cleaned[..maxLength].Trim('_', ' ', '.') : cleaned;
    }

    public static string BuildVideoFileName(string author, string title, string awemeId, string quality = "1080p")
        => $"{Sanitize($"{author}_{title}_{awemeId}_{quality}")}.mp4";

    public static string BuildImageFileName(string author, string title, string awemeId, int index, int total, string extension)
    {
        var ext = string.IsNullOrWhiteSpace(extension) ? ".jpg" : extension;
        if (!ext.StartsWith('.'))
        {
            ext = "." + ext;
        }

        var width = Math.Max(2, total.ToString().Length);
        return $"{Sanitize($"{author}_{title}_{awemeId}")}_{index.ToString().PadLeft(width, '0')}{ext}";
    }

    public static string GuessImageExtension(string url)
    {
        var path = url.Split('?', '#')[0].ToLowerInvariant();
        if (path.EndsWith(".png"))
        {
            return ".png";
        }

        if (path.EndsWith(".webp"))
        {
            return ".webp";
        }

        if (path.EndsWith(".jpeg"))
        {
            return ".jpeg";
        }

        return ".jpg";
    }
}
