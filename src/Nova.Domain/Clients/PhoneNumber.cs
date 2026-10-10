namespace Nova.Domain.Clients;

/// <summary>Российский мобильный номер в формате E.164: +7XXXXXXXXXX.</summary>
public static class PhoneNumber
{
    /// <summary>Принимает «8 999 123-45-67», «+7 (999) 123 45 67», «9991234567» и т.п.</summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(input)) return false;

        Span<char> digits = stackalloc char[16];
        var n = 0;
        foreach (var ch in input)
        {
            if (char.IsAsciiDigit(ch))
            {
                if (n == digits.Length) return false;
                digits[n++] = ch;
            }
            else if (ch is not (' ' or '-' or '(' or ')' or '+')) return false;
        }

        var d = digits[..n];
        if (d.Length == 10) { normalized = "+7" + new string(d); }
        else if (d.Length == 11 && d[0] is '7' or '8') { normalized = "+7" + new string(d[1..]); }
        else return false;

        return normalized[2] == '9'; // мобильные номера РФ начинаются с 9
    }

    /// <summary>+79991234567 → +7999***4567 — для логов и аудита.</summary>
    public static string Mask(string phone) =>
        phone.Length >= 12 ? phone[..5] + "***" + phone[^4..] : "***";
}
