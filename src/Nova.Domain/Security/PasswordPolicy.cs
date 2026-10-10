namespace Nova.Domain.Security;

public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    /// <summary>Возвращает текст ошибки или null, если пароль подходит.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            return $"Пароль должен быть не короче {MinLength} символов";
        if (password.Length > MaxLength)
            return $"Пароль должен быть не длиннее {MaxLength} символов";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "Пароль должен содержать буквы и цифры";
        return null;
    }
}
