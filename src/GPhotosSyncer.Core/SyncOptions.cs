namespace GPhotosSyncer.Core;

/// <summary>How an existing destination file is judged equal to its source counterpart.</summary>
public enum ComparisonMode
{
    /// <summary>Same relative path + same byte size. Fast, no content read. Default.</summary>
    NameSize,
    /// <summary>NameSize plus last-write time (±2s tolerance for FAT/NTFS).</summary>
    NameSizeDate,
    /// <summary>Full SHA-256 content comparison. Correct but reads every byte.</summary>
    Hash
}

/// <summary>What to do with files that exist at the destination but not in the merged source.</summary>
public enum DeletionMode
{
    /// <summary>Never remove anything. Only add/overwrite. Safest.</summary>
    AddOnly,
    /// <summary>List orphans, take no action.</summary>
    Report,
    /// <summary>Move orphans to the quarantine folder on the destination drive.</summary>
    Quarantine,
    /// <summary>Mirror: orphans are quarantined, but ONLY when <see cref="SyncOptions.AllPartsPresent"/> is set.
    /// Without that flag this degrades to <see cref="Report"/>. v1 never hard-deletes.</summary>
    Mirror
}

public sealed class SyncOptions
{
    /// <summary>The built-in sidecar extensions skipped by default (single source of truth).</summary>
    public static readonly string[] DefaultSkipExtensions = { ".json" };

    /// <summary>The built-in trash folder names excluded by default (single source of truth).</summary>
    public static readonly string[] DefaultExcludedTopFolders = { "Papelera", "Trash", "Bin" };

    /// <summary>File extensions (with dot, case-insensitive) excluded from the sync. Default: Google sidecars.</summary>
    public HashSet<string> SkipExtensions { get; init; } =
        new(DefaultSkipExtensions, StringComparer.OrdinalIgnoreCase);

    /// <summary>Top-level folder names (relative to the photos base) excluded entirely.
    /// "Papelera"/"Trash"/"Bin" = Google Photos recycle bin.</summary>
    public HashSet<string> ExcludedTopFolders { get; init; } =
        new(DefaultExcludedTopFolders, StringComparer.OrdinalIgnoreCase);

    public ComparisonMode Comparison { get; init; } = ComparisonMode.NameSize;

    public DeletionMode Deletion { get; init; } = DeletionMode.AddOnly;

    /// <summary>
    /// If true, orphan handling also applies to folders that exist ONLY at the destination
    /// (e.g. legacy English "Photos from 2020"). Default false to protect those folders.
    /// </summary>
    public bool DeleteDestOnlyFolders { get; init; } = false;

    /// <summary>Safety gate: <see cref="DeletionMode.Mirror"/> only acts when the user confirms
    /// that every Takeout part is loaded. Otherwise a partial export would orphan real files.</summary>
    public bool AllPartsPresent { get; init; } = false;

    /// <summary>Concurrent copies. A single USB HDD rarely benefits from &gt;2; SSDs scale higher.</summary>
    public int MaxParallelCopies { get; init; } = 2;

    /// <summary>Prefix of the per-run quarantine folder created at the destination root
    /// (the executor appends a timestamp so re-runs never overwrite earlier rescues).</summary>
    public string QuarantineFolderName { get; init; } = "_SyncTrash";
}
