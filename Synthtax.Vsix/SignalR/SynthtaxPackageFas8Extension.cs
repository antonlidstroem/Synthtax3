using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Synthtax.Vsix.Services;
using Synthtax.Vsix.SignalR;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client; // För HubConnectionState

namespace Synthtax.Vsix.Package;

public static class SignalRPackageInitializer
{
    private static ISynthtaxHubClient? _hubClient;
    private static StatusBarService? _statusBarService;
    private static RealTimeUpdateService? _realTimeService;

    public static ISynthtaxHubClient? HubClient => _hubClient;
    public static RealTimeUpdateService? RealTimeService => _realTimeService;

    public static async Task InitializeAsync(AsyncPackage package, CancellationToken ct)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(ct);

        // Hämta paketet för att komma åt AuthTokenService
        var synthtaxPkg = package as SynthtaxPackage;
        if (synthtaxPkg == null) return;

        var auth = synthtaxPkg.AuthTokenService;

        // Skapa Logger (VsOutputWindowLogger som du definierade)
        var logger = new VsOutputWindowLogger("SignalR", package);

        // Initiera klienten
        _hubClient = new SynthtaxHubClient(auth, logger);
        // ... resten av initieringen
    }
}