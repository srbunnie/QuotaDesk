using System.Globalization;

namespace AIUsageMonitor;

/// <summary>
/// Interface language: Portuguese (Brazil) or English. Every visible text is written inline as L.T("pt", "en"), so both
/// versions sit next to each other and a missing translation is a compile-time gap, not a runtime one. The language is
/// chosen once at startup ("auto" follows Windows); changing it restarts the app, because menus are built once.
/// </summary>
internal static class L
{
    private static readonly CultureInfo Portuguese = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static bool En { get; private set; }

    /// <summary>Dates, weekdays and numbers in the interface language.</summary>
    public static CultureInfo Culture => En ? English : Portuguese;

    /// <summary>Setting values, in the order the language picker shows them.</summary>
    public static readonly string[] Choices = ["auto", "pt", "en"];

    /// <summary>The picker labels stay the same in both languages, so anyone can find their own.</summary>
    public static readonly string[] ChoiceLabels = ["Automático / Automatic", "Português", "English"];

    public static void Configure(string? choice) => En = choice switch
    {
        "en" => true,
        "pt" => false,
        _ => !CultureInfo.CurrentUICulture.Name.StartsWith("pt", StringComparison.OrdinalIgnoreCase)
    };

    public static string T(string pt, string en) => En ? en : pt;

    /// <summary>A page of the online documentation in the interface language (docs/docs and docs/en/docs on GitHub Pages).</summary>
    public static string DocsUrl(string page = "index") =>
        $"https://srbunnie.github.io/QuotaDesk/{(En ? "en/" : "")}docs/{page}.html";

    /// <summary>Opens a documentation page in the default browser; a failure (no browser) is ignored.</summary>
    public static void OpenDocs(string page = "index")
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DocsUrl(page)) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    /// <summary>Short day and month: 28/10 in Portuguese, 10/28 in English.</summary>
    public static string DayMonth => En ? "MM/dd" : "dd/MM";
}
