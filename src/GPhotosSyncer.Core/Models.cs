namespace GPhotosSyncer.Core;

/// <summary>A file found while scanning a source or the destination.</summary>
public sealed class ScannedFile
{
    public required string AbsolutePath { get; init; }
    /// <summary>Path relative to its scan base, using the OS separator.</summary>
    public required string RelPath { get; init; }
    public required long Size { get; init; }
    public DateTime LastWriteUtc { get; init; }
}

public enum CopyReason { New, Changed }

public enum OrphanAction
{
    /// <summary>In a folder that exists only at the destination; left untouched.</summary>
    Protected,
    /// <summary>Listed for the user, no action taken.</summary>
    Report,
    /// <summary>Moved to the quarantine folder.</summary>
    Quarantine
}

public sealed class CopyOp
{
    public required ScannedFile Source { get; init; }
    public required string DestPath { get; init; }
    public required CopyReason Reason { get; init; }
}

public sealed class OrphanOp
{
    public required string DestPath { get; init; }
    public required string RelPath { get; init; }
    public required long Size { get; init; }
    public required OrphanAction Action { get; init; }
    public string? Note { get; init; }
}

public sealed class ConflictItem
{
    public required string RelPath { get; init; }
    public required string Detail { get; init; }
}

/// <summary>Orphans split into the buckets the UI/CLI report (computed once, see <see cref="SyncPlan.CategorizeOrphans"/>).</summary>
public sealed record OrphanBuckets(
    IReadOnlyList<OrphanOp> ZoneJson,
    IReadOnlyList<OrphanOp> ZoneMedia,
    IReadOnlyList<OrphanOp> DestOnly)
{
    public long ZoneJsonBytes => ZoneJson.Sum(o => o.Size);
    public long ZoneMediaBytes => ZoneMedia.Sum(o => o.Size);
    public long DestOnlyBytes => DestOnly.Sum(o => o.Size);
}

/// <summary>The full computed plan for a sync run (a dry-run produces just this).</summary>
public sealed class SyncPlan
{
    public List<CopyOp> ToCopy { get; } = new();
    public List<ScannedFile> Unchanged { get; } = new();
    public List<OrphanOp> Orphans { get; } = new();
    public List<ConflictItem> Conflicts { get; } = new();

    public int CopyCount => ToCopy.Count;
    public long CopyBytes => ToCopy.Sum(c => c.Source.Size);
    public int UnchangedCount => Unchanged.Count;
    public long UnchangedBytes => Unchanged.Sum(u => u.Size);

    /// <summary>Splits orphans into "json to clean" / "media in the mirror zone" / "protected dest-only".
    /// Shared by the CLI and the app so both classify orphans identically.</summary>
    public OrphanBuckets CategorizeOrphans()
    {
        var inZone = Orphans.Where(o => o.Action is OrphanAction.Report or OrphanAction.Quarantine).ToList();
        var destOnly = Orphans.Where(o => o.Action == OrphanAction.Protected).ToList();
        static bool IsJson(OrphanOp o) => string.Equals(Path.GetExtension(o.RelPath), ".json", StringComparison.OrdinalIgnoreCase);
        return new OrphanBuckets(
            inZone.Where(IsJson).ToList(),
            inZone.Where(o => !IsJson(o)).ToList(),
            destOnly);
    }
}

public sealed class SyncProgress
{
    public long FilesDone { get; init; }
    public long FilesTotal { get; init; }
    public long BytesDone { get; init; }
    public long BytesTotal { get; init; }
    public string? CurrentFile { get; init; }
    public double BytesPerSecond { get; init; }
}

public sealed class SyncResult
{
    public int Copied;
    public long CopiedBytes;
    public int Quarantined;
    public long QuarantinedBytes;
    public List<string> Errors { get; } = new();
}
