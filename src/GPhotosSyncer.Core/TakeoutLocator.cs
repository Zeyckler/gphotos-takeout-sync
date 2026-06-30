namespace GPhotosSyncer.Core;

/// <summary>
/// Resolves the "photos base" (the directory that directly holds album/year folders)
/// from whatever path the user provides. Handles the common Google Takeout layouts:
///   X\Takeout\Google Fotos, X\Takeout\Google Photos, X\Google Fotos, or X itself.
/// </summary>
public static class TakeoutLocator
{
    static readonly string[] PhotoDirNames = { "Google Fotos", "Google Photos" };

    /// <summary>
    /// Strict probe: returns the photos base only if one is actually found under
    /// <paramref name="inputPath"/>, otherwise null. Used to detect real Takeout parts
    /// (e.g. when scanning a parent folder full of takeout-… subfolders).
    /// </summary>
    public static string? FindPhotoBase(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
            return null;

        var trimmed = inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // The path is already a "Google Fotos"/"Google Photos" directory.
        foreach (var name in PhotoDirNames)
            if (string.Equals(Path.GetFileName(trimmed), name, StringComparison.OrdinalIgnoreCase))
                return trimmed;

        // Probe the usual nestings.
        foreach (var name in PhotoDirNames)
        {
            var direct = Path.Combine(trimmed, name);
            if (Directory.Exists(direct)) return direct;

            var nested = Path.Combine(trimmed, "Takeout", name);
            if (Directory.Exists(nested)) return nested;
        }

        return null;
    }

    /// <summary>Lenient: the strict result, or the input itself for custom/manual layouts.</summary>
    public static string? Resolve(string inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath) || !Directory.Exists(inputPath))
            return null;

        return FindPhotoBase(inputPath)
               ?? inputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
