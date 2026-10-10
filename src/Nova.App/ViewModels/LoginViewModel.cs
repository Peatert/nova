using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.App.Services;

namespace Nova.App.ViewModels;

/// <summary>Вход в два шага: телефон + пароль, затем (если включена 2FA) код из приложения.</summary>
public sealed partial class LoginViewModel(ApiClient api, TokenStore tokens) : ObservableObject
{
    [ObservableProperty] private string phone = "";
    [ObservableProperty] private string password = "";
    [ObservableProperty] private string code = "";
    [ObservableProperty] private string serverUrl = api.Server;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string error = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPasswordStep))]
    [NotifyPropertyChangedFor(nameof(SubmitText))]
    private bool isMfaStep;

    [ObservableProperty] private bool isBusy;

    private string? _mfaToken;

    public bool IsPasswordStep => !IsMfaStep;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public string SubmitText => IsMfaStep ? "Подтвердить" : "Войти";

    /// <summary>Сессия уже есть — сразу на главную, без ввода пароля.</summary>
    public async Task TryResumeAsync()
    {
        await tokens.LoadAsync();
        if (!tokens.HasSession) return;
        try
        {
            IsBusy = true;
            await api.MeAsync(); // заодно обновит access-токен, если он истёк
            await Shell.Current.GoToAsync(AppShell.Home);
        }
        catch (ApiException)
        {
            // Нет связи или сессия истекла — остаёмся на экране входа.
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        Error = "";
        api.Server = ServerUrl;
        IsBusy = true;
        try
        {
            var result = IsMfaStep
                ? await api.VerifyTwoFactorAsync(_mfaToken!, Code.Trim())
                : await api.LoginAsync(Phone, Password);

            if (result.Requires2fa && result.MfaToken is not null)
            {
                _mfaToken = result.MfaToken;
                IsMfaStep = true;
                return;
            }
            if (result.AccessToken is null || result.RefreshToken is null)
            {
                Error = "Сервер вернул неполный ответ";
                return;
            }

            await tokens.SaveAsync(result.AccessToken, result.RefreshToken);
            Password = Code = "";
            IsMfaStep = false;
            await Shell.Current.GoToAsync(AppShell.Home);
        }
        catch (ApiException e)
        {
            Error = e.Message;
            if (IsMfaStep && e.Status == 401 && e.Message.Contains("истекла"))
                IsMfaStep = false; // MFA-токен живёт 5 минут — начинаем вход заново
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back()
    {
        IsMfaStep = false;
        Code = "";
        Error = "";
    }
}
