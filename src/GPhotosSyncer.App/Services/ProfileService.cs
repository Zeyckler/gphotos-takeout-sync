using System.Text.Json;
using GPhotosSyncer.App.Models;

namespace GPhotosSyncer.App.Services;

public sealed class ProfileService
{
    static string FilePath => Path.Combine(AppContext.BaseDirectory, "gpsync.profile.json");

    public SyncProfile? Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<SyncProfile>(File.ReadAllText(FilePath))
                : null;
        }
        catch { return null; }
    }

    public void Save(SyncProfile profile)
    {
        try
        {
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            // Write to a temp file then move into place so an interrupted write can never
            // truncate/corrupt the existing profile.
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* read-only media etc. — non-fatal */ }
    }
}
