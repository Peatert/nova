using Nova.App.ViewModels;

namespace Nova.App.Views;

public partial class StatementPage : ContentPage
{
    public StatementPage(StatementViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm; // параметры маршрута (accountId) Shell передаст модели через IQueryAttributable
    }
}
