using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nova.App.Services;

namespace Nova.App.ViewModels;

/// <summary>Выписка за 90 дней с подгрузкой при прокрутке (курсор next из API).</summary>
public sealed partial class StatementViewModel(ApiClient api) : ObservableObject, IQueryAttributable
{
    private Guid _accountId;
    private DateTimeOffset? _next;
    private bool _end;
    private bool _loading;

    public ObservableCollection<OperationItem> Items { get; } = [];

    [ObservableProperty] private string title = "Выписка";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isEmpty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string error = "";

    public bool HasError => !string.IsNullOrEmpty(Error);

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("accountId", out var id) && Guid.TryParse(id?.ToString(), out var guid))
            _accountId = guid;
        if (query.TryGetValue("title", out var t) && t?.ToString() is { Length: > 0 } text)
            Title = Uri.UnescapeDataString(text);
        _ = LoadAsync(reset: true);
    }

    private async Task LoadAsync(bool reset)
    {
        if (_loading || _accountId == Guid.Empty) return;
        if (!reset && _end) return;

        _loading = true;
        IsBusy = true;
        Error = "";
        try
        {
            var page = await api.StatementAsync(_accountId, reset ? null : _next, limit: 30);
            if (reset) Items.Clear();
            foreach (var item in page.Items) Items.Add(OperationItem.From(item));
            _next = page.Next;
            _end = page.Next is null;
            IsEmpty = Items.Count == 0;
        }
        catch (ApiException e)
        {
            Error = e.Message;
        }
        finally
        {
            _loading = false;
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task LoadMoreAsync() => LoadAsync(reset: false);

    [RelayCommand]
    private Task BackAsync() => Shell.Current.GoToAsync("..");
}
