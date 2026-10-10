using Nova.App.Services;
using Nova.App.ViewModels;
using Nova.App.Views;

namespace Nova.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        // Сервисы: одна сессия и один HTTP-клиент на всё приложение.
        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddSingleton<ApiClient>();
        builder.Services.AddSingleton<AppShell>();

        // Экраны и их модели. Shell создаёт страницы через DI, зависимости приходят в конструктор.
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<TransferViewModel>();
        builder.Services.AddTransient<TransferPage>();
        builder.Services.AddTransient<StatementViewModel>();
        builder.Services.AddTransient<StatementPage>();
        builder.Services.AddTransient<ProfileViewModel>();
        builder.Services.AddTransient<ProfilePage>();

        return builder.Build();
    }
}
