using CommunityToolkit.Mvvm.ComponentModel;
using Synthtax.WPF.Services;

namespace Synthtax.WPF.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected readonly ApiClient Api;
    protected readonly TokenStore TokenStore;
    protected readonly LocalizationService L = LocalizationService.Current;

    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private bool _hasError;
    private string _errorMessage = string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        protected set => SetProperty(ref _isBusy, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        protected set => SetProperty(ref _statusMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        protected set => SetProperty(ref _hasError, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        protected set => SetProperty(ref _errorMessage, value);
    }

    protected ViewModelBase(ApiClient api, TokenStore tokenStore)
    {
        Api = api;
        TokenStore = tokenStore;
    }

    protected void SetBusy(string? statusKey = null)
    {
        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = statusKey is null ? L["Status_Loading"] : L[statusKey];
    }

    protected void SetReady(string? statusKey = null)
    {
        IsBusy = false;
        StatusMessage = statusKey is null ? L["Status_Ready"] : L[statusKey];
    }

    protected void SetError(string message)
    {
        IsBusy = false;
        HasError = true;
        ErrorMessage = message;
        StatusMessage = L["Status_Error"];
    }

    protected async Task RunSafeAsync(Func<Task> action, string? busyStatusKey = null)
    {
        try
        {
            SetBusy(busyStatusKey);
            await action();
            SetReady();
        }
        catch (OperationCanceledException)
        {
            SetReady();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }
}
