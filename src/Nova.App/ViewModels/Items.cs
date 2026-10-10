using System.Windows.Input;
using Nova.App.Services;
using Nova.Contracts;

namespace Nova.App.ViewModels;

/// <summary>Счёт в списке: всё уже отформатировано для экрана.</summary>
public sealed class AccountItem
{
    private static readonly Color AccentColor = Color.FromArgb("#E5303F");
    private static readonly Color ChipColor = Color.FromArgb("#22222E");

    public required AccountDto Dto { get; init; }
    public bool Hidden { get; init; }
    public ICommand? Open { get; init; }

    public string Title => Dto.Type switch
    {
        "Debit" => "Дебетовая карта",
        "Savings" => "Накопительный счёт",
        "Credit" => "Кредитная карта",
        _ => "Счёт",
    };

    public string Tag => Dto.Type switch
    {
        "Debit" => "NOVA",
        "Savings" => "НАК",
        "Credit" => "КРЕД",
        _ => "•",
    };

    public Color TagColor => Dto.Type == "Debit" ? AccentColor : ChipColor;

    public string Subtitle => Dto.Cards.FirstOrDefault() is { } card
        ? $"•• {card.Last4}"
        : $"•• {Dto.Number[^4..]}";

    public string BalanceText => Hidden ? "•••• ₽" : Fmt.Money(Dto.Balance);

    /// <summary>Так счёт выглядит в выпадающем списке «Откуда».</summary>
    public override string ToString() => $"{Title} {Subtitle} · {Fmt.Money(Dto.Available)}";
}

/// <summary>Операция в ленте: буква-аватар, описание, сумма со знаком и цветом.</summary>
public sealed class OperationItem
{
    private static readonly Color Positive = Color.FromArgb("#6EE7A0");
    private static readonly Color Neutral = Color.FromArgb("#F5F5F7");
    private static readonly Color Accent = Color.FromArgb("#E5303F");
    private static readonly Color Chip = Color.FromArgb("#2A2A38");

    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string AmountText { get; init; }
    public required Color AmountColor { get; init; }
    public required string Letter { get; init; }
    public required Color BadgeColor { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    public static OperationItem From(StatementItemDto i)
    {
        var income = i.Amount > 0;
        var who = i.Counterparty is { Length: > 0 } c ? c + " · " : "";
        return new OperationItem
        {
            Title = i.Description,
            Subtitle = who + Fmt.Date(i.CreatedAt),
            AmountText = Fmt.SignedMoney(i.Amount),
            AmountColor = income ? Positive : Neutral,
            Letter = string.IsNullOrEmpty(i.Description) ? "•" : i.Description[..1].ToUpperInvariant(),
            BadgeColor = income ? Accent : Chip,
            CreatedAt = i.CreatedAt,
        };
    }
}
