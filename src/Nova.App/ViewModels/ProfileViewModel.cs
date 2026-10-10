using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.App.Services;

namespace Nova.App.ViewModels;

public sealed partial class ProfileViewModel(ApiClient api) : ObservableObject
{
    [ObservableProperty] private string fullName = "";
    [ObservableProperty] private string initial = "";
    [ObservableProperty] private string security = "";
    [ObservableProperty] private string server = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string error = "";

    public bool HasError => !string.IsNullOrEmpty(Error);

    public async Task LoadAsync()
    {
        Server = api.Server;
        try
        {
            var me = await api.MeAsync();
            FullName = me.Name;
            Initial = Fmt.FirstName(me.Name) is { Length: > 0 } first ? first[..1] : "N";
            Security = me.AuthMethod == "mfa"
                ? "Вход выполнен с двухфакторной защитой"
                : "Вход по паролю. 2FA можно включить через API (/api/me/2fa/setup)";
        }
        catch (ApiException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        // Отзываем сессию на сервере (вся цепочка refresh-токенов), потом стираем её на устройстве.
        await api.LogoutAsync();
        await Shell.Current.GoToAsync(AppShell.Login);
    }
}
