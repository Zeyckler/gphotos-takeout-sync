namespace GPhotosSyncer.Core;

public static class FileScanner
{
    static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        // Skip OS cruft (Thumbs.db/desktop.ini are Hidden/System). NOTE: we deliberately do NOT
        // skip ReparsePoint — that attribute also marks OneDrive/cloud "online-only" placeholder
        // files, and skipping those would silently drop real photos from the scan.
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System
    };

    /// <summary>
    /// Streams every file under <paramref name="baseDir"/>. Size and timestamps come from the
    /// directory enumeration data (no extra stat call per file), so this stays fast on large trees.
    /// </summary>
    public static IEnumerable<ScannedFile> Enumerate(string baseDir, CancellationToken ct = default)
    {
        var root = new DirectoryInfo(baseDir);
        foreach (var fi in root.EnumerateFiles("*", Options))
        {
            ct.ThrowIfCancellationRequested();
            yield return new ScannedFile
            {
                AbsolutePath = fi.FullName,
                RelPath = Path.GetRelativePath(baseDir, fi.FullName),
                Size = fi.Length,
                LastWriteUtc = fi.LastWriteTimeUtc
            };
        }
    }
}
