using System.Diagnostics;
using GPhotosSyncer.Core;

// Minimal CLI front-end over GPhotosSyncer.Core. Lets us validate the engine against real
// data with zero risk (the WinUI app will reuse the exact same Core types).
//
//   gpsync analyze --dest <d> --src <s1> [<s2> ...] [options]   (dry-run, never writes)
//   gpsync sync    --dest <d> --src <s1> [<s2> ...] [options] --yes
//
// Options: --deletion addonly|report|quarantine|mirror   --all-parts   --delete-dest-only
//          --keep-json   --include-trash   --comparison namesize|namesizedate|hash
//          --parallel <n>

return Cli.Run(args);

static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0) { Usage(); return 1; }

        var cmd = args[0].ToLowerInvariant();
        if (cmd is not ("analyze" or "sync")) { Usage(); return 1; }

        var p = new ArgParser(args);
        var dest = p.Get("--dest");
        var sources = p.GetMulti("--src");

        if (dest is null || sources.Count == 0)
        {
            Console.Error.WriteLine("ERROR: faltan --dest o --src.");
            Usage();
            return 1;
        }

        // Resolve the photos base under each provided source path.
        var bases = new List<string>();
        foreach (var s in sources)
        {
            var resolved = TakeoutLocator.Resolve(s);
            if (resolved is null) { Console.Error.WriteLine($"ERROR: origen no encontrado: {s}"); return 1; }
            bases.Add(resolved);
            Console.WriteLine($"  origen → {resolved}");
        }
        Console.WriteLine($"  destino → {dest}");
        Console.WriteLine();

        var options = new SyncOptions
        {
            Deletion = p.Get("--deletion")?.ToLowerInvariant() switch
            {
                "report" => DeletionMode.Report,
                "quarantine" => DeletionMode.Quarantine,
                "mirror" => DeletionMode.Mirror,
                _ => DeletionMode.AddOnly
            },
            AllPartsPresent = p.Has("--all-parts"),
            DeleteDestOnlyFolders = p.Has("--delete-dest-only"),
            Comparison = p.Get("--comparison")?.ToLowerInvariant() switch
            {
                "namesizedate" => ComparisonMode.NameSizeDate,
                "hash" => ComparisonMode.Hash,
                _ => ComparisonMode.NameSize
            },
            MaxParallelCopies = int.TryParse(p.Get("--parallel"), out var par) ? par : 2,
            SkipExtensions = p.Has("--keep-json")
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(SyncOptions.DefaultSkipExtensions, StringComparer.OrdinalIgnoreCase),
            ExcludedTopFolders = p.Has("--include-trash")
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(SyncOptions.DefaultExcludedTopFolders, StringComparer.OrdinalIgnoreCase)
        };

        Console.WriteLine($"Opciones: borrado={options.Deletion}  comparación={options.Comparison}  "
                          + $"saltar={(options.SkipExtensions.Count == 0 ? "(nada)" : string.Join(",", options.SkipExtensions))}  "
                          + $"paralelo={options.MaxParallelCopies}");
        Console.WriteLine(new string('-', 70));

        var sw = Stopwatch.StartNew();
        var planner = new SyncPlanner(options);
        var plan = planner.Build(bases, dest, new Progress<string>(m => Console.WriteLine($"… {m}")));
        sw.Stop();

        PrintPlan(plan, options, sw.Elapsed);

        if (cmd == "analyze")
        {
            Console.WriteLine("\n(analyze = simulación; no se ha escrito nada.)");
            return 0;
        }

        // cmd == sync
        if (!p.Has("--yes"))
        {
            Console.WriteLine("\nPara EJECUTAR esta sincronización vuelve a lanzar el comando añadiendo  --yes");
            return 0;
        }

        Console.WriteLine("\nEjecutando…");
        var executor = new SyncExecutor(options);
        var progress = new Progress<SyncProgress>(pr =>
        {
            var pct = pr.BytesTotal == 0 ? 100 : (int)(100.0 * pr.BytesDone / pr.BytesTotal);
            Console.Write($"\r  {pct,3}%  {pr.FilesDone}/{pr.FilesTotal} archivos  "
                          + $"{Fmt(pr.BytesDone)}/{Fmt(pr.BytesTotal)}  {Fmt((long)pr.BytesPerSecond)}/s        ");
        });
        var result = executor.ExecuteAsync(plan, dest, progress).GetAwaiter().GetResult();
        Console.WriteLine();
        Console.WriteLine(new string('-', 70));
        Console.WriteLine($"Copiados:     {result.Copied} ({Fmt(result.CopiedBytes)})");
        Console.WriteLine($"Cuarentena:   {result.Quarantined} ({Fmt(result.QuarantinedBytes)})");
        Console.WriteLine($"Errores:      {result.Errors.Count}");
        foreach (var e in result.Errors.Take(20)) Console.WriteLine($"   ! {e}");
        return result.Errors.Count == 0 ? 0 : 2;
    }

    static void PrintPlan(SyncPlan plan, SyncOptions opt, TimeSpan elapsed)
    {
        Console.WriteLine();
        Console.WriteLine("=== RESUMEN DEL PLAN ===");
        Console.WriteLine($"  A copiar:     {plan.CopyCount,7}  ({Fmt(plan.CopyBytes)})");
        Console.WriteLine($"     · nuevos:  {plan.ToCopy.Count(c => c.Reason == CopyReason.New),7}");
        Console.WriteLine($"     · cambiados:{plan.ToCopy.Count(c => c.Reason == CopyReason.Changed),7}");
        Console.WriteLine($"  Sin cambios:  {plan.UnchangedCount,7}  ({Fmt(plan.UnchangedBytes)})  → se omiten");
        Console.WriteLine($"  Conflictos:   {plan.Conflicts.Count,7}");
        Console.WriteLine($"  (plan calculado en {elapsed.TotalSeconds:0.0}s)");

        Sample("PRIMEROS A COPIAR", plan.ToCopy.Take(8).Select(c => $"[{c.Reason}] {c.Source.RelPath}  ({Fmt(c.Source.Size)})"));

        if (opt.Deletion != DeletionMode.AddOnly)
        {
            var b = plan.CategorizeOrphans();
            bool willMove = (opt.Deletion == DeletionMode.Mirror && opt.AllPartsPresent) || opt.Deletion == DeletionMode.Quarantine;

            Console.WriteLine();
            Console.WriteLine("  HUÉRFANOS (en destino, sin equivalente en el origen fusionado):");
            Console.WriteLine($"    zona espejo · .json a limpiar:      {b.ZoneJson.Count,7}  ({Fmt(b.ZoneJsonBytes)})");
            Console.WriteLine($"    zona espejo · MEDIA:                {b.ZoneMedia.Count,7}  ({Fmt(b.ZoneMediaBytes)})  <- ¡sólo borrar con las 7 partes!");
            Console.WriteLine($"    protegidos · carpetas sólo-destino: {b.DestOnly.Count,7}  ({Fmt(b.DestOnlyBytes)})");
            Console.WriteLine($"    acción actual sobre la zona espejo: {(willMove ? "MOVER a cuarentena" : "sólo reportar")}");

            Sample("MEDIA EN ZONA ESPEJO (muestra — comprueba que no falten partes)", b.ZoneMedia.Take(8).Select(o => $"{o.RelPath}  ({Fmt(o.Size)})"));
            Sample("CARPETAS SÓLO-DESTINO (muestra — protegidas)", b.DestOnly.Take(6).Select(o => o.RelPath));
        }

        Sample("CONFLICTOS", plan.Conflicts.Take(8).Select(c => $"{c.RelPath}  — {c.Detail}"));
    }

    static void Sample(string title, IEnumerable<string> items)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        Console.WriteLine($"\n  {title}:");
        foreach (var i in list) Console.WriteLine($"     {i}");
    }

    static string Fmt(long bytes) => ByteFormat.Humanize(bytes);

    static void Usage()
    {
        Console.WriteLine(
@"gpsync — sincronizador de Google Takeout (Fotos) → disco

  gpsync analyze --dest <carpeta> --src <p1> [<p2> ...] [opciones]
  gpsync sync    --dest <carpeta> --src <p1> [<p2> ...] [opciones] --yes

Opciones:
  --deletion addonly|report|quarantine|mirror   (def. addonly)
  --all-parts            confirma que están las 7 partes (necesario para mirror real)
  --delete-dest-only     incluye carpetas que sólo existen en destino (PELIGRO)
  --keep-json            NO omitir los .json
  --include-trash        incluir la carpeta Papelera/Trash
  --comparison namesize|namesizedate|hash       (def. namesize)
  --parallel <n>         copias simultáneas (def. 2)
  --yes                  confirma la ejecución real (sólo en 'sync')");
    }
}

/// <summary>Tiny flag parser: --flag value, repeated --src values, and boolean --flags.</summary>
sealed class ArgParser
{
    readonly string[] _a;
    public ArgParser(string[] a) => _a = a;

    public string? Get(string flag)
    {
        int i = Array.IndexOf(_a, flag);
        return i >= 0 && i + 1 < _a.Length ? _a[i + 1] : null;
    }

    public bool Has(string flag) => Array.IndexOf(_a, flag) >= 0;

    /// <summary>Values following <paramref name="flag"/> up to the next --option.</summary>
    public List<string> GetMulti(string flag)
    {
        var result = new List<string>();
        int i = Array.IndexOf(_a, flag);
        if (i < 0) return result;
        for (int j = i + 1; j < _a.Length && !_a[j].StartsWith("--"); j++)
            result.Add(_a[j]);
        return result;
    }
}
