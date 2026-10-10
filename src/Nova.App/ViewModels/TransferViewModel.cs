using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.App.Services;
using Nova.Contracts;

namespace Nova.App.ViewModels;

/// <summary>
/// Перевод по номеру телефона.
/// Ключ идемпотентности живёт столько же, сколько «черновик» перевода:
///  • изменили получателя, сумму или счёт — это новый перевод, новый ключ;
///  • нажали «Перевести» ещё раз после ошибки связи — тот же ключ, и сервер не спишет деньги дважды.
/// </summary>
public sealed partial class TransferViewModel(ApiClient api) : ObservableObject
{
    private Guid _idempotencyKey = Guid.NewGuid();

    public ObservableCollection<AccountItem> SourceAccounts { get; } = [];

    [ObservableProperty] private AccountItem? selectedAccount;
    [ObservableProperty] private string phone = "";
    [ObservableProperty] private string recipientName = "";
    [ObservableProperty] private string amount = "";
    [ObservableProperty] private string comment = "";
    [ObservableProperty] private bool isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string error = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsForm))]
    private bool isDone;

    [ObservableProperty] private string resultAmount = "";
    [ObservableProperty] private string resultRecipient = "";
    [ObservableProperty] private string resultBalance = "";

    public bool HasError => !string.IsNullOrEmpty(Error);
    public bool IsForm => !IsDone;

    partial void OnPhoneChanged(string value)
    {
        RecipientName = "";
        _idempotencyKey = Guid.NewGuid();
    }

    partial void OnAmountChanged(string value) => _idempotencyKey = Guid.NewGuid();

    partial void OnSelectedAccountChanged(AccountItem? value) => _idempotencyKey = Guid.NewGuid();

    public async Task LoadAsync()
    {
        if (IsDone) return;
        try
        {
            var previous = SelectedAccount?.Dto.Id;
            var accounts = await api.AccountsAsync();
            SourceAccounts.Clear();
            foreach (var a in accounts.Where(a => a.Status == "Active"))
                SourceAccounts.Add(new AccountItem { Dto = a });
            SelectedAccount = SourceAccounts.FirstOrDefault(a => a.Dto.Id == previous)
                              ?? SourceAccounts.FirstOrDefault(a => a.Dto.Type == "Debit")
                              ?? SourceAccounts.FirstOrDefault();
        }
        catch (ApiException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private async Task FindRecipientAsync()
    {
        Error = "";
        if (string.IsNullOrWhiteSpace(Phone)) return;
        try
        {
            var r = await api.RecipientAsync(Phone);
            RecipientName = $"{r.Name} · {r.Bank}";
        }
        catch (ApiException e)
        {
            RecipientName = "";
            Error = e.Message;
        }
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        Error = "";
        if (SelectedAccount is null) { Error = "Выберите счёт списания"; return; }
        if (string.IsNullOrWhiteSpace(Phone)) { Error = "Введите номер получателя"; return; }
        if (!Fmt.TryParseAmount(Amount, out var sum) || sum <= 0) { Error = "Введите сумму"; return; }

        IsBusy = true;
        try
        {
            var result = await api.TransferAsync(
                new TransferRequest(SelectedAccount.Dto.Id, Phone, null, sum, string.IsNullOrWhiteSpace(Comment) ? null : Comment),
                _idempotencyKey);

            ResultAmount = Fmt.Money(result.Amount);
            ResultRecipient = "Отправлено: " + result.Recipient;
            ResultBalance = "Остаток на счёте " + Fmt.Money(result.BalanceAfter);
            IsDone = true;
        }
        catch (ApiException e)
        {
            // Ключ НЕ меняем: повторное нажатие после обрыва связи безопасно.
            Error = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task NewTransferAsync()
    {
        IsDone = false;
        Phone = Amount = Comment = RecipientName = Error = "";
        _idempotencyKey = Guid.NewGuid();
        await LoadAsync(); // балансы после перевода изменились
    }

    [RelayCommand]
    private Task GoHomeAsync()
    {
        IsDone = false;
        Phone = Amount = Comment = RecipientName = "";
        return Shell.Current.GoToAsync(AppShell.Home);
    }
}
