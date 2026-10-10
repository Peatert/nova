using Nova.App.Views;

namespace Nova.App;

public partial class AppShell : Shell
{
    public const string Login = "//login";
    public const string Home = "//main/home/homepage";
    public const string Transfer = "//main/payments/transferpage";

    public AppShell()
    {
        InitializeComponent();
        // Выписка открывается поверх вкладок, с параметром accountId.
        Routing.RegisterRoute("statement", typeof(StatementPage));
    }
}
