namespace Nova.Domain;

/// <summary>Нарушение бизнес-правила (недостаточно средств, счёт заморожен и т.п.).</summary>
public sealed class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
