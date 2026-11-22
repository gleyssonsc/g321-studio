using System.Globalization;
using System.IO;
using System.Resources;
using System.Security;
using System.Windows.Markup;
using Microsoft.Win32;

namespace G321Studio;

internal static class StudioText
{
    private const string SettingsKey = @"Software\G321 Studio";
    private static readonly ResourceManager Strings = new("G321Studio.Resources.Strings", typeof(StudioText).Assembly);
    private static CultureInfo culture = CultureInfo.GetCultureInfo("pt-BR");

    public static void Initialize()
    {
        string? language = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            language = key?.GetValue("Language") as string;
        }
        catch (Exception ex) when (ex is IOException or SecurityException or UnauthorizedAccessException)
        {
            // Older and portable builds can start without an installer language setting.
        }

        culture = CultureInfo.GetCultureInfo(
            string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase) ? "en-US" : "pt-BR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    public static string Get(string key) => Strings.GetString(key, culture)
        ?? throw new InvalidOperationException($"Missing Studio string: {key}.");

    public static string Format(string key, params object?[] values) => string.Format(culture, Get(key), values);
}

[MarkupExtensionReturnType(typeof(string))]
public sealed class TextExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => StudioText.Get(key);
}
