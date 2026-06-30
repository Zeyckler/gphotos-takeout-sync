using System.Security.Cryptography;

namespace GPhotosSyncer.Core;

/// <summary>
/// Builds a <see cref="SyncPlan"/> by merging N source roots into one virtual tree
/// (keyed by relative path), filtering out sidecars / excluded folders, comparing against
/// the destination, and classifying destination-only files as orphans.
/// </summary>
public sealed class SyncPlanner
{
    const string TempSuffix = ".synctmp";

    readonly SyncOptions _opt;

    public SyncPlanner(SyncOptions opt) => _opt = opt;

    public SyncPlan Build(
        IReadOnlyList<string> sourceBases,
        string destBase,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        var plan = new SyncPlan();

        // 1) Merge sources into one index keyed by relative path.
        //    The "deletion zone" (sourceTopFolders) only includes folders that actually
        //    contribute MEDIA, so JSON-only album folders never put dest files at risk.
        var merged = new Dictionary<string, ScannedFile>(StringComparer.OrdinalIgnoreCase);
        var sourceTopFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < sourceBases.Count; i++)
        {
            log?.Report($"Escaneando origen {i + 1}/{sourceBases.Count}: {sourceBases[i]}");
            foreach (var f in FileScanner.Enumerate(sourceBases[i], ct))
            {
                var top = TopFolder(f.RelPath);
                if (top is not null && _opt.ExcludedTopFolders.Contains(top))
                    continue; // e.g. Papelera/Trash — never copied, never considered.

                if (!IsIncluded(f))
                    continue; // sidecars (.json) and other skipped extensions.

                if (top is not null)
                    sourceTopFolders.Add(top);

                if (merged.TryGetValue(f.RelPath, out var existing))
                {
                    if (existing.Size != f.Size)
                        plan.Conflicts.Add(new ConflictItem
                        {
                            RelPath = f.RelPath,
                            Detail = $"Mismo nombre con tamaños distintos entre partes ({existing.Size} vs {f.Size} bytes)."
                        });
                    // Keep the first occurrence; duplicate parts are expected to be identical.
                }
                else
                {
                    merged[f.RelPath] = f;
                }
            }
        }

        // 2) Compare each merged source file against the destination.
        log?.Report("Comparando con el destino…");
        foreach (var (rel, src) in merged)
        {
            ct.ThrowIfCancellationRequested();
            var destPath = Path.Combine(destBase, rel); // identity mapping (v1: mirror structure)
            var dest = SafeInfo(destPath);

            if (dest is null)
                plan.ToCopy.Add(new CopyOp { Source = src, DestPath = destPath, Reason = CopyReason.New });
            else if (!SameFile(src, dest))
                plan.ToCopy.Add(new CopyOp { Source = src, DestPath = destPath, Reason = CopyReason.Changed });
            else
                plan.Unchanged.Add(src);
        }

        // 3) Orphans: destination files with no merged-source counterpart.
        if (_opt.Deletion != DeletionMode.AddOnly && Directory.Exists(destBase))
        {
            log?.Report("Buscando huérfanos en el destino…");
            foreach (var d in FileScanner.Enumerate(destBase, ct))
            {
                var top = TopFolder(d.RelPath);

                // Never treat our own (timestamped) quarantine folders as orphans.
                if (top is not null && top.StartsWith(_opt.QuarantineFolderName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Ignore our own leftover temp files from an interrupted copy.
                if (d.RelPath.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (merged.ContainsKey(d.RelPath))
                    continue; // expected here.

                bool inDeletionZone = top is not null && sourceTopFolders.Contains(top);
                OrphanAction action;
                string? note = null;

                if (!inDeletionZone && !_opt.DeleteDestOnlyFolders)
                {
                    action = OrphanAction.Protected;
                    note = "Carpeta sólo en destino (protegida).";
                }
                else
                {
                    action = _opt.Deletion switch
                    {
                        DeletionMode.Report => OrphanAction.Report,
                        DeletionMode.Quarantine => OrphanAction.Quarantine,
                        DeletionMode.Mirror => _opt.AllPartsPresent ? OrphanAction.Quarantine : OrphanAction.Report,
                        _ => OrphanAction.Report
                    };
                    if (_opt.Deletion == DeletionMode.Mirror && !_opt.AllPartsPresent)
                        note = "Modo espejo sin confirmar las 7 partes: sólo se reporta.";
                }

                plan.Orphans.Add(new OrphanOp
                {
                    DestPath = d.AbsolutePath,
                    RelPath = d.RelPath,
                    Size = d.Size,
                    Action = action,
                    Note = note
                });
            }
        }

        return plan;
    }

    static string? TopFolder(string relPath)
    {
        int idx = relPath.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        return idx < 0 ? null : relPath[..idx];
    }

    bool IsIncluded(ScannedFile f) => !_opt.SkipExtensions.Contains(Path.GetExtension(f.RelPath));

    static FileInfo? SafeInfo(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? fi : null;
        }
        catch { return null; }
    }

    bool SameFile(ScannedFile src, FileInfo dest)
    {
        if (src.Size != dest.Length) return false;
        return _opt.Comparison switch
        {
            ComparisonMode.NameSize => true,
            ComparisonMode.NameSizeDate => Math.Abs((src.LastWriteUtc - dest.LastWriteTimeUtc).TotalSeconds) <= 2,
            ComparisonMode.Hash => HashEquals(src.AbsolutePath, dest.FullName),
            _ => true
        };
    }

    static bool HashEquals(string a, string b)
    {
        using var fa = File.OpenRead(a);
        using var fb = File.OpenRead(b);
        Span<byte> ha = stackalloc byte[32];
        Span<byte> hb = stackalloc byte[32];
        SHA256.HashData(fa, ha);
        SHA256.HashData(fb, hb);
        return ha.SequenceEqual(hb);
    }
}
