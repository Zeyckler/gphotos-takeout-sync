namespace GPhotosSyncer.App.Models;

/// <summary>Persisted next to the exe (gpsync.profile.json) so config travels on the USB drive.
/// Note: the dangerous per-run flags (all-parts-present, delete-dest-only) are intentionally NOT
/// persisted — they must be re-confirmed each session.</summary>
public sealed class SyncProfile
{
    public List<string> Sources { get; set; } = new();
    public string Destination { get; set; } = "";
    public bool SkipJson { get; set; } = true;
    public bool ExcludeTrash { get; set; } = true;
    public int ComparisonIndex { get; set; }   // 0 name+size, 1 +date, 2 hash
    public int DeletionIndex { get; set; }      // 0 add-only, 1 report, 2 quarantine, 3 mirror
    public int Parallel { get; set; } = 2;
    public string Language { get; set; } = "";  // empty = auto-detect from the OS
}
