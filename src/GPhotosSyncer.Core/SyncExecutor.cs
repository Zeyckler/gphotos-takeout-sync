using System.Diagnostics;

namespace GPhotosSyncer.Core;

/// <summary>Executes a <see cref="SyncPlan"/>: copies new/changed files and quarantines orphans.</summary>
public sealed class SyncExecutor
{
    const int BufferSize = 1024 * 1024; // 1 MiB

    readonly SyncOptions _opt;

    public SyncExecutor(SyncOptions opt) => _opt = opt;

    public async Task<SyncResult> ExecuteAsync(
        SyncPlan plan,
        string destBase,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        var result = new SyncResult();
        long doneBytes = 0, doneFiles = 0;
        long totalBytes = plan.CopyBytes;
        long totalFiles = plan.ToCopy.Count;

        var clock = Stopwatch.StartNew();
        var emitLock = new object();
        long lastEmitMs = -1000;

        void Emit(string? current, bool force)
        {
            lock (emitLock)
            {
                long ms = clock.ElapsedMilliseconds;
                if (!force && ms - lastEmitMs < 200) return;
                lastEmitMs = ms;
                double secs = Math.Max(0.001, clock.Elapsed.TotalSeconds);
                long bytes = Interlocked.Read(ref doneBytes);
                progress?.Report(new SyncProgress
                {
                    FilesDone = Interlocked.Read(ref doneFiles),
                    FilesTotal = totalFiles,
                    BytesDone = bytes,
                    BytesTotal = totalBytes,
                    CurrentFile = current,
                    BytesPerSecond = bytes / secs
                });
            }
        }

        using var sem = new SemaphoreSlim(Math.Max(1, _opt.MaxParallelCopies));
        var tasks = new List<Task>(plan.ToCopy.Count);

        try
        {
            foreach (var op in plan.ToCopy)
            {
                ct.ThrowIfCancellationRequested();
                await sem.WaitAsync(ct).ConfigureAwait(false);
                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(op.DestPath)!);
                        await CopyFileAsync(op.Source.AbsolutePath, op.DestPath, op.Source.LastWriteUtc,
                            n => { Interlocked.Add(ref doneBytes, n); Emit(op.Source.RelPath, false); }, ct).ConfigureAwait(false);
                        Interlocked.Increment(ref doneFiles);
                        lock (result) { result.Copied++; result.CopiedBytes += op.Source.Size; }
                        Emit(op.Source.RelPath, false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        lock (result) result.Errors.Add($"{op.Source.RelPath}: {ex.Message}");
                    }
                    finally { sem.Release(); }
                }, ct));
            }
        }
        finally
        {
            // Always drain the copies we already started, even if dispatch was cancelled,
            // so no detached task keeps writing files / racing on shared state after we return.
            try { await Task.WhenAll(tasks).ConfigureAwait(false); }
            catch { /* per-file errors already recorded; cancellation handled below */ }
        }

        ct.ThrowIfCancellationRequested();

        // Quarantine orphans into a per-run, timestamped folder so re-runs never overwrite
        // files rescued by an earlier run. v1 never hard-deletes — everything stays on the drive.
        var runTrash = $"{_opt.QuarantineFolderName}_{DateTime.Now:yyyyMMdd_HHmmss}";
        foreach (var orphan in plan.Orphans.Where(o => o.Action == OrphanAction.Quarantine))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var qPath = Path.Combine(destBase, runTrash, orphan.RelPath);
                Directory.CreateDirectory(Path.GetDirectoryName(qPath)!);
                File.Move(orphan.DestPath, qPath, overwrite: true);
                lock (result) { result.Quarantined++; result.QuarantinedBytes += orphan.Size; }
            }
            catch (Exception ex)
            {
                lock (result) result.Errors.Add($"cuarentena {orphan.RelPath}: {ex.Message}");
            }
        }

        Emit(null, true);
        return result;
    }

    /// <summary>Copies to a temp file then moves it into place with overwrite (atomic on one volume),
    /// so an interrupted run never leaves a half-written file that name+size would mistake for complete.
    /// The temp file is removed if the copy fails or is cancelled.</summary>
    static async Task CopyFileAsync(string src, string dest, DateTime lastWriteUtc, Action<int> onChunk, CancellationToken ct)
    {
        var tmp = dest + ".synctmp";
        try
        {
            await using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize,
                             FileOptions.SequentialScan | FileOptions.Asynchronous))
            await using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize,
                             FileOptions.Asynchronous))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(0, BufferSize), ct).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                    onChunk(read);
                }
            }

            File.Move(tmp, dest, overwrite: true);
            try { File.SetLastWriteTimeUtc(dest, lastWriteUtc); } catch { /* dest FS may not support it */ }
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* best-effort cleanup */ }
            throw;
        }
    }
}
