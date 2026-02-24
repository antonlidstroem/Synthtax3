using System.Net.Http.Headers;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Synthtax.WPF.Services;
using Synthtax.WPF.ViewModels;
using Synthtax.WPF.Views;

namespace Synthtax.WPF;

public partial class App : Application
{
    private IServiceProvider? _services;

    public IServiceProvider Services =>
        _services ?? throw new InvalidOperationException("Services not initialized.");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _services = BuildServiceProvider();

        var apiClient = _services.GetRequiredService<ApiClient>();
        apiClient.SessionExpired += (_, _) => Dispatcher.Invoke(ShowLogin);

        ShowLogin();
    }

    public void ShowLogin()
    {
        var loginWindow = new LoginWindow(_services!);
        loginWindow.LoginSucceeded += OnLoginSucceeded;
        loginWindow.Show();

        foreach (Window w in Windows)
            if (w is MainWindow) w.Close();
    }

    private void OnLoginSucceeded(object? sender, EventArgs e)
    {
        if (sender is Window w) w.Close();
        new MainWindow(_services!).Show();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // ── Logging ───────────────────────────────────────────────────────
        services.AddLogging(b => b
            .AddDebug()
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));

        // ── Core singletons ───────────────────────────────────────────────
        services.AddSingleton<TokenStore>();
        services.AddSingleton<NavigationService>();

        // ── HTTP ──────────────────────────────────────────────────────────
        // Använd ENBART AddHttpClient<T> – ingen extra AddTransient<ApiClient>.
        // Typed client injicerar HttpClient automatiskt via konstruktorn.
        services.AddHttpClient<ApiClient>(client =>
        {
            // Matchar launchSettings.json → "applicationUrl": "http://localhost:5000"
            // Byt till https://localhost:5001 om du kör med HTTPS i dev.
            client.BaseAddress = new Uri("http://localhost:5000/");
            client.Timeout = TimeSpan.FromSeconds(120);
            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        });

        // ── ViewModels ────────────────────────────────────────────────────
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<CodeAnalysisViewModel>();
        services.AddTransient<MetricsViewModel>();
        services.AddTransient<GitViewModel>();
        services.AddTransient<SecurityViewModel>();
        services.AddTransient<UserProfileViewModel>();
        services.AddTransient<AdminViewModel>();
        services.AddTransient<StructureAnalysisViewModel>();
        services.AddTransient<PullRequestsViewModel>();
        services.AddTransient<MethodExplorerViewModel>();
        services.AddTransient<CommentExplorerViewModel>();
        services.AddTransient<AIDetectionViewModel>();

        services.AddTransient<BacklogViewModel>(sp =>
            new BacklogViewModel(
                sp.GetRequiredService<ApiClient>(),
                sp.GetRequiredService<TokenStore>(),
                sp));

        // ── Views ─────────────────────────────────────────────────────────
        services.AddTransient<CodeAnalysisView>();
        services.AddTransient<MetricsView>();
        services.AddTransient<GitView>();
        services.AddTransient<SecurityView>();
        services.AddTransient<BacklogView>();
        services.AddTransient<UserProfileView>();
        services.AddTransient<AdminView>();
        services.AddTransient<StructureAnalysisView>();
        services.AddTransient<PullRequestsView>();
        services.AddTransient<MethodExplorerView>();
        services.AddTransient<CommentExplorerView>();
        services.AddTransient<AIDetectionView>();

        return services.BuildServiceProvider();
    }
}
