using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Markup;

namespace OpenController.App.Localization;

/// <summary>
/// Runtime-switchable UI strings (English and Traditional Chinese). XAML binds through
/// <c>{l:T Key}</c>; code uses <see cref="T(string)"/>. Missing keys fall back to English, then to the key.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public static IReadOnlyList<(string Code, string Name)> Languages { get; } =
    [
        ("en", "English"),
        ("zh-TW", "繁體中文"),
    ];

    private IReadOnlyDictionary<string, string> _current = Strings.English;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changed (UI thread).</summary>
    public event Action? LanguageChanged;

    public string Language { get; private set; } = "en";

    public string this[string key] => Lookup(key);

    public void SetLanguage(string code)
    {
        Language = code == "zh-TW" ? "zh-TW" : "en";
        _current = Language == "zh-TW" ? Strings.TraditionalChinese : Strings.English;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke();
    }

    public static string T(string key) => Instance.Lookup(key);

    public static string T(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, Instance.Lookup(key), args);

    private string Lookup(string key) =>
        _current.TryGetValue(key, out string? s) ? s :
        Strings.English.TryGetValue(key, out string? en) ? en : key;
}

/// <summary>XAML markup extension: <c>Text="{l:T Dashboard}"</c>.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
