using System.ComponentModel;
using System.Globalization;
using System.Linq;

namespace GPhotosSyncer.App.Localization;

/// <summary>
/// Live UI localization. Bind text as {Binding Loc[Key]} (classic Binding to the indexer);
/// changing <see cref="Language"/> raises "Item[]" so every bound string refreshes instantly,
/// no restart needed. Works in single-file builds (strings are embedded, see <see cref="Strings"/>).
/// </summary>
public sealed class Localizer : INotifyPropertyChanged
{
    public static Localizer Instance { get; } = new();

    public IReadOnlyList<LanguageOption> Languages => Strings.Languages;

    string _language;

    private Localizer()
    {
        var os = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        _language = Strings.Languages.Any(l => l.Code == os) ? os : "en";
    }

    public string Language
    {
        get => _language;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == _language) return;
            if (Strings.Languages.All(l => l.Code != value)) return;
            _language = value;
            try
            {
                var ci = CultureInfo.GetCultureInfo(value);
                CultureInfo.CurrentCulture = ci;
                CultureInfo.CurrentUICulture = ci;
            }
            catch { /* keep our own strings even if the OS lacks the culture */ }

            PropertyChanged?.Invoke(this, ItemsChangedArgs);
            PropertyChanged?.Invoke(this, LanguageChangedArgs);
        }
    }

    public string this[string key] => Strings.Get(_language, key);

    public string Format(string key, params object[] args) => string.Format(Strings.Get(_language, key), args);

    static readonly PropertyChangedEventArgs ItemsChangedArgs = new("Item[]");
    static readonly PropertyChangedEventArgs LanguageChangedArgs = new(nameof(Language));

    public event PropertyChangedEventHandler? PropertyChanged;
}
