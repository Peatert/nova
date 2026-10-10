using Nova.App.Services;

namespace Nova.App;

public partial class App : Application
{
    private readonly AppShell _shell;

    public App(AppShell shell, ApiClient api)
    {
        InitializeComponent();
        _shell = shell;

        // Refresh-токен отозван или истёк — возвращаем на экран входа, где бы пользователь ни был.
        api.SessionExpired += () => MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (Shell.Current is not null) await Shell.Current.GoToAsync("//login");
        });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell) { Title = "Nova" };
#if WINDOWS
        // На ПК — окно пропорций телефона: тот же интерфейс, что на S24.
        window.Width = 440;
        window.Height = 900;
#endif
        return window;
    }
}
