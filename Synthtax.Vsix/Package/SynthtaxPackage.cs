using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Synthtax.Vsix.Auth;
using Synthtax.Vsix.Client;
using Synthtax.Vsix.Commands;
using Synthtax.Vsix.Services;
using Synthtax.Vsix.ToolWindow;

namespace Synthtax.Vsix.Package;

/// <summary>
/// Synthtax VSIX AsyncPackage.
///
/// <para><b>Registrerar:</b>
/// <list type="bullet">
///   <item>Tool Window: Synthtax Backlog (F6 / View → Other Windows → Synthtax Backlog)</item>
///   <item>Kommandon: OpenBacklog, RefreshBacklog, Login</item>
///   <item>Services: SynthtaxApiClient, AuthTokenService, PromptDispatchService</item>
/// </list>
/// </para>
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(SynthtaxPackageGuids.PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(
    typeof(BacklogToolWindow),
    Style           = VsDockStyle.Tabbed,
    Window          = "DocumentWell",
    Orientation     = ToolWindowOrientation.Right,
    MultiInstances  = false)]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionOpening_string, PackageAutoLoadFlags.BackgroundLoad)]
[ProvideOptionPage(
    typeof(SynthtaxOptionsPage),
    "Synthtax", "General",
    0, 0, true)]
public sealed class SynthtaxPackage : AsyncPackage
{
    // ── Singleton-tjänster (skapas en gång, lever med paketet) ────────────

    private SynthtaxApiClient?      _apiClient;
    private AuthTokenService?       _authTokenService;
    private PromptDispatchService?  _promptDispatch;

    public SynthtaxApiClient ApiClient =>
        _apiClient ?? throw new InvalidOperationException("Package not initialized");
    public AuthTokenService AuthTokenService =>
        _authTokenService ?? throw new InvalidOperationException("Package not initialized");
    public PromptDispatchService PromptDispatch =>
        _promptDispatch ?? throw new InvalidOperationException("Package not initialized");

    // ── Initialisering ────────────────────────────────────────────────────

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);

        // Byt till UI-tråd för VS-tjänster som kräver det
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        // ── Interna tjänster ──────────────────────────────────────────────
        _authTokenService = new AuthTokenService(this);
        _apiClient        = new SynthtaxApiClient(_authTokenService);
        _promptDispatch   = new PromptDispatchService();

        // ── Kommandon ─────────────────────────────────────────────────────
        await OpenBacklogCommand.InitializeAsync(this);
        await RefreshBacklogCommand.InitializeAsync(this);

        // ── Diagnostik-provider aktiveras automatiskt via MEF ────────────
        // SynthtaxDiagnosticProvider registreras som DiagnosticAnalyzer
        // via [ExportDiagnosticAnalyzer]-attributet — ingen manuell init.
    }

    // ── Statisk accessor för kommandon och providers ──────────────────────

    /// <summary>Returnerar paketet om det är laddat. Null annars.</summary>
    public static SynthtaxPackage? Instance { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _apiClient?.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Alternativ-sida i VS Tools → Options → Synthtax.
/// </summary>
internal sealed class SynthtaxOptionsPage : DialogPage
{
    [System.ComponentModel.Category("API")]
    [System.ComponentModel.DisplayName("API Base URL")]
    [System.ComponentModel.Description("Synthtax backend URL, t.ex. https://api.synthtax.io")]
    public string ApiBaseUrl { get; set; } = "https://api.synthtax.io";

    [System.ComponentModel.Category("Analysis")]
    [System.ComponentModel.DisplayName("Auto-refresh (seconds)")]
    [System.ComponentModel.Description("Hur ofta backloggen uppdateras automatiskt (0 = av).")]
    public int AutoRefreshSeconds { get; set; } = 60;

    [System.ComponentModel.Category("Analysis")]
    [System.ComponentModel.DisplayName("Enable squiggles")]
    [System.ComponentModel.Description("Visa Synthtax-issues som understrykning i editorn.")]
    public bool EnableSquiggles { get; set; } = true;
}
