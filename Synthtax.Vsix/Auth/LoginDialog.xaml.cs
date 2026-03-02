using System.Windows;
using System.Windows.Input;
using Microsoft.VisualStudio.Shell;
using Synthtax.Vsix.Client;

namespace Synthtax.Vsix.Auth;

/// <summary>
/// Code-behind för <c>LoginDialog.xaml</c>.
/// Anropar <see cref="SynthtaxApiClient.LoginAsync"/> och visar fel inline.
/// </summary>
public partial class LoginDialog : Window
{
    private readonly SynthtaxApiClient _api;
    private readonly AuthTokenService  _auth;
    private bool _isLoggingIn;

    public LoginDialog(SynthtaxApiClient api, AuthTokenService auth)
    {
        InitializeComponent();
        _api  = api;
        _auth = auth;
        Loaded += OnLoaded;
    }

    // ── Event handlers ────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        // Fyll i sparad API-URL och användarnamn
        ApiUrlBox.Text  = _auth.ApiBaseUrl;
        var savedUser   = _auth.GetSavedUserName();
        if (savedUser is not null)
        {
            UserNameBox.Text = savedUser;
            PasswordBox.Focus();
        }
        else
        {
            UserNameBox.Focus();
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _ = LoginAsync();
    }

    private void OnLoginClick(object sender, RoutedEventArgs e) =>
        _ = LoginAsync();

    private void OnCancelClick(object sender, RoutedEventArgs e) =>
        DialogResult = false;

    // ── Inloggningslogik ──────────────────────────────────────────────────

    private async Task LoginAsync()
    {
        if (_isLoggingIn) return;

        var apiUrl   = ApiUrlBox.Text.Trim().TrimEnd('/');
        var userName = UserNameBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrEmpty(userName))
        {
            ShowError("Ange användarnamn.");
            UserNameBox.Focus();
            return;
        }
        if (string.IsNullOrEmpty(password))
        {
            ShowError("Ange lösenord.");
            PasswordBox.Focus();
            return;
        }

        SetLoadingState(true);

        try
        {
            // Spara API-URL om den ändrats
            if (apiUrl != _auth.ApiBaseUrl)
                await _auth.SaveApiBaseUrlAsync(apiUrl);

            var result = await _api.LoginAsync(userName, password);
            await _auth.SaveUserNameAsync(result.UserName);

            DialogResult = true;
        }
        catch (UnauthorizedException)
        {
            ShowError("Fel användarnamn eller lösenord.");
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
        catch (SynthtaxApiException ex)
        {
            ShowError($"Anslutningsfel: {ex.Message}");
        }
        catch (Exception ex)
        {
            ShowError($"Oväntat fel: {ex.Message}");
        }
        finally
        {
            SetLoadingState(false);
        }
    }

    // ── UI-hjälpmetoder ───────────────────────────────────────────────────

    private void ShowError(string message)
    {
        ErrorText.Text       = message;
        ErrorText.Visibility = Visibility.Visible;
        LoadingBar.Visibility = Visibility.Collapsed;
    }

    private void SetLoadingState(bool loading)
    {
        _isLoggingIn = loading;
        LoginButton.IsEnabled  = !loading;
        CancelButton.IsEnabled = !loading;
        UserNameBox.IsEnabled  = !loading;
        PasswordBox.IsEnabled  = !loading;
        ApiUrlBox.IsEnabled    = !loading;

        LoadingBar.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ErrorText.Visibility  = loading ? Visibility.Collapsed : ErrorText.Visibility;
    }

    // ── Statisk fabrik ────────────────────────────────────────────────────

    /// <summary>Öppnar dialogen och returnerar true om inloggning lyckades.</summary>
    public static async Task<bool> ShowAsync(
        SynthtaxApiClient api, AuthTokenService auth, Window? owner = null)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var dlg = new LoginDialog(api, auth) { Owner = owner };
        return dlg.ShowDialog() == true;
    }
}
