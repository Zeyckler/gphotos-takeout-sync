namespace GPhotosSyncer.Core;

/// <summary>Single source of truth for human-readable byte sizes (used by CLI and app).</summary>
public static class ByteFormat
{
    public static string Humanize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        int i = 0;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return $"{value:0.##} {units[i]}";
    }
}
