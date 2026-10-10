namespace Nova.Domain.Cards;

/// <summary>Алгоритм Луна: контрольная цифра номера карты.</summary>
public static class Luhn
{
    /// <summary>Вычисляет контрольную цифру для номера без неё.</summary>
    public static int CheckDigit(ReadOnlySpan<char> digitsWithoutCheck)
    {
        var sum = 0;
        var doubleIt = true; // справа налево, начиная с цифры перед контрольной
        for (var i = digitsWithoutCheck.Length - 1; i >= 0; i--)
        {
            var d = digitsWithoutCheck[i] - '0';
            if ((uint)d > 9) throw new ArgumentException("Только цифры", nameof(digitsWithoutCheck));
            if (doubleIt)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
            doubleIt = !doubleIt;
        }
        return (10 - sum % 10) % 10;
    }

    public static bool IsValid(ReadOnlySpan<char> pan)
    {
        if (pan.Length < 2) return false;
        var last = pan[^1] - '0';
        return (uint)last <= 9 && CheckDigit(pan[..^1]) == last;
    }
}
