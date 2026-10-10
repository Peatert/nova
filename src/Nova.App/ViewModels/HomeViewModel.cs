using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.App.Services;
using Nova.Contracts;

namespace Nova.App.ViewModels;

public sealed partial class HomeViewModel(ApiClient api) : ObservableObject
{
    private List<AccountDto> _accounts = [];
    private bool _hidden;

    [ObservableProperty] private string name = "";
    [ObservableProperty] private string initial = "";
    [ObservableProperty] private string totalText = "";
    [ObservableProperty] private string hideText = "Скрыть";
    [ObservableProperty] private bool isRefreshing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string error = "";

    public bool HasError => !string.IsNullOrEmpty(Error);
    public ObservableCollection<AccountItem> Accounts { get; } = [];
    public ObservableCollection<OperationItem> Operations { get; } = [];

    public async Task LoadAsync()
    {
        Error = "";
        try
        {
            var me = await api.MeAsync();
            Name = Fmt.FirstName(me.Name);
            Initial = Name.Length > 0 ? Name[..1] : "N";

            _accounts = await api.AccountsAsync();
            RenderAccounts();

            // Лента — последние операции основного (дебетового) счёта.
            Operations.Clear();
            var main = _accounts.FirstOrDefault(a => a.Type == "Debit") ?? _accounts.FirstOrDefault();
            if (main is not null)
            {
                var statement = await api.StatementAsync(main.Id, before: null, limit: 10);
                foreach (var item in statement.Items) Operations.Add(OperationItem.From(item));
            }
        }
        catch (ApiException e)
        {
            Error = e.Message;
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void RenderAccounts()
    {
        Accounts.Clear();
        foreach (var a in _accounts)
            Accounts.Add(new AccountItem { Dto = a, Hidden = _hidden, Open = OpenAccountCommand });

        // «Все деньги» — собственные средства: дебет + накопительный. Кредитный лимит — не твои деньги.
        var own = _accounts.Where(a => a.Type is "Debit" or "Savings").Sum(a => a.Balance);
        TotalText = _hidden ? "•••• ₽" : Fmt.Money(own);
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    [RelayCommand]
    private void ToggleHide()
    {
        _hidden = !_hidden;
        HideText = _hidden ? "Показать" : "Скрыть";
        RenderAccounts();
    }

    [RelayCommand]
    private Task OpenAccountAsync(AccountItem? item) =>
        item is null
            ? Task.CompletedTask
            : Shell.Current.GoToAsync($"statement?accountId={item.Dto.Id}&title={Uri.EscapeDataString(item.Title)}");

    [RelayCommand]
    private Task GoTransferAsync() => Shell.Current.GoToAsync(AppShell.Transfer);
}
