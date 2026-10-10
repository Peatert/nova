using System.Globalization;

namespace Nova.App.Services;

/// <summary>Форматирование для интерфейса: деньги и даты по-русски.</summary>
public static class Fmt
{
    public static readonly CultureInfo Ru = new("ru-RU");

    /// <summary>Знак минуса — типографский «−» (U+2212), а не дефис: одинаково во всём приложении.</summary>
    public static string Money(decimal value) =>
        (value < 0 ? "−" : "") + Math.Abs(value).ToString("N2", Ru) + " ₽";

    public static string SignedMoney(decimal value) =>
        (value > 0 ? "+" : value < 0 ? "−" : "") + Money(Math.Abs(value));

    public static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("d MMM, HH:mm", Ru);

    /// <summary>«Смирнов Алексей Петрович» → «Алексей»: в приветствии — по имени.</summary>
    public static string FirstName(string fullName)
    {
        var parts = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : parts.FirstOrDefault() ?? "";
    }

    /// <summary>Принимает «1 500,50», «1500.5» — как набирают на телефоне.</summary>
    public static bool TryParseAmount(string? text, out decimal amount)
    {
        var cleaned = (text ?? "").Replace(" ", "").Replace(" ", "").Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
    }
}
