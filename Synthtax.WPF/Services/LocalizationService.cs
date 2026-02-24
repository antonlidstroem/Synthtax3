using System.Collections;
using System.Globalization;
using System.IO;
using System.Resources;
using System.Xml;
using System.ComponentModel;

namespace Synthtax.WPF.Services;

/// <summary>
/// Provides runtime-switchable localized strings from .resx files.
/// All ViewModels bind to this service via LocalizationService.Current.
/// </summary>
public class LocalizationService : INotifyPropertyChanged
{
    public static LocalizationService Current { get; } = new();

    private Dictionary<string, string> _strings = new();
    private string _currentLanguage = "sv-SE";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage == value) return;
            _currentLanguage = value;
            LoadStrings(value);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // Refresh all
        }
    }

    private LocalizationService()
    {
        LoadStrings("sv-SE");
    }

    public string this[string key]
        => _strings.TryGetValue(key, out var val) ? val : key;

    public string Get(string key, string? fallback = null)
        => _strings.TryGetValue(key, out var val) ? val : (fallback ?? key);

    private void LoadStrings(string language)
    {
        // Try to load from embedded resx-style XML file
        var fileName = $"Resources/Strings/Strings.{language}.resx";

        try
        {
            if (!File.Exists(fileName))
            {
                // Try relative to executable
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                fileName = Path.Combine(exeDir, $"Resources/Strings/Strings.{language}.resx");
            }

            if (File.Exists(fileName))
            {
                _strings = ParseResx(fileName);
                return;
            }
        }
        catch { /* fallthrough */ }

        // Inline fallback – essential strings only
        _strings = language == "sv-SE" ? BuildSwedishFallback() : BuildEnglishFallback();
    }

    private static Dictionary<string, string> ParseResx(string filePath)
    {
        var dict = new Dictionary<string, string>();
        var doc = new XmlDocument();
        doc.Load(filePath);

        foreach (XmlNode node in doc.SelectNodes("//data")!)
        {
            var name = node.Attributes?["name"]?.Value;
            var value = node.SelectSingleNode("value")?.InnerText;
            if (name is not null && value is not null)
                dict[name] = value;
        }

        return dict;
    }

    private static Dictionary<string, string> BuildSwedishFallback() => new()
    {
        ["AppTitle"] = "Synthtax",
        ["AppSubtitle"] = "Kodintelligens",
        ["Nav_CodeAnalysis"] = "Kodanalys",
        ["Nav_Metrics"] = "Metrics",
        ["Nav_Git"] = "Git",
        ["Nav_Security"] = "Säkerhetsanalys",
        ["Nav_Backlog"] = "Backlog",
        ["Nav_UserProfile"] = "Profil",
        ["Nav_Admin"] = "Administration",
        ["Nav_Structure"] = "Strukturanalys",
        ["Nav_PullRequests"] = "Pull Requests",
        ["Nav_MethodExplorer"] = "Metodutforskaren",
        ["Nav_CommentExplorer"] = "Kommentarsutforskaren",
        ["Nav_AIDetection"] = "AI-detektering",
        ["Action_Analyze"] = "Analysera",
        ["Action_Refresh"] = "Uppdatera",
        ["Action_Cancel"] = "Avbryt",
        ["Action_Save"] = "Spara",
        ["Action_BrowsePath"] = "Bläddra...",
        ["Login_Title"] = "Logga in",
        ["Login_Username"] = "Användarnamn",
        ["Login_Password"] = "Lösenord",
        ["Login_Button"] = "Logga in",
        ["Status_Analyzing"] = "Analyserar...",
        ["Status_Ready"] = "Redo",
        ["Status_Loading"] = "Laddar...",
        ["TopBar_Export"] = "Exportera",
        ["TopBar_Settings"] = "Inställningar",
        ["TopBar_LogOut"] = "Logga ut",
    };

    private static Dictionary<string, string> BuildEnglishFallback() => new()
    {
        ["AppTitle"] = "Synthtax",
        ["AppSubtitle"] = "Code Intelligence",
        ["Nav_CodeAnalysis"] = "Code Analysis",
        ["Nav_Metrics"] = "Metrics",
        ["Nav_Git"] = "Git",
        ["Nav_Security"] = "Security Analyzer",
        ["Nav_Backlog"] = "Backlog",
        ["Nav_UserProfile"] = "Profile",
        ["Nav_Admin"] = "Administration",
        ["Nav_Structure"] = "Structure Analysis",
        ["Nav_PullRequests"] = "Pull Requests",
        ["Nav_MethodExplorer"] = "Method Explorer",
        ["Nav_CommentExplorer"] = "Comment Explorer",
        ["Nav_AIDetection"] = "AI Detection",
        ["Action_Analyze"] = "Analyze",
        ["Action_Refresh"] = "Refresh",
        ["Action_Cancel"] = "Cancel",
        ["Action_Save"] = "Save",
        ["Action_BrowsePath"] = "Browse...",
        ["Login_Title"] = "Sign In",
        ["Login_Username"] = "Username",
        ["Login_Password"] = "Password",
        ["Login_Button"] = "Sign In",
        ["Status_Analyzing"] = "Analyzing...",
        ["Status_Ready"] = "Ready",
        ["Status_Loading"] = "Loading...",
        ["TopBar_Export"] = "Export",
        ["TopBar_Settings"] = "Settings",
        ["TopBar_LogOut"] = "Log Out",
    };
}
