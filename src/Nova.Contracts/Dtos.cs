namespace Nova.Contracts;

// Формы запросов и ответов API в одном месте: приложение и API говорят на одном языке.
// JSON — camelCase, enum приходят строками ("Debit", "Credit").

public sealed record ClientLoginRequest(string Phone, string Password);
public sealed record TwoFactorRequest(string MfaToken, string Code);
public sealed record RefreshRequest(string RefreshToken);

/// <summary>Ответ входа: либо пара токенов, либо requires2fa + mfaToken.</summary>
public sealed record LoginResponse(
    string? AccessToken = null,
    string? RefreshToken = null,
    int ExpiresIn = 0,
    bool MfaSetupRequired = false,
    bool Requires2fa = false,
    string? MfaToken = null);

public sealed record ProfileDto(
    string Id, string Type, string Name, string? Role, string? AuthMethod, bool MfaSetupRequired, string[] Permissions);

public sealed record CardDto(Guid Id, string Last4, DateOnly ExpiresAt, string Status);

public sealed record AccountDto(
    Guid Id, string Number, string Type, string Currency,
    decimal Balance, decimal CreditLimit, decimal Available, string Status, CardDto[] Cards);

public sealed record StatementItemDto(
    Guid TransferId, DateTimeOffset CreatedAt, string Direction, decimal Amount,
    decimal BalanceAfter, string Description, string? Counterparty);

public sealed record StatementDto(
    Guid AccountId, DateTimeOffset From, DateTimeOffset To, StatementItemDto[] Items, DateTimeOffset? Next);

public sealed record RecipientDto(string Name, string Bank);

public sealed record TransferRequest(Guid FromAccountId, string? ToPhone, Guid? ToAccountId, decimal Amount, string? Comment);

public sealed record TransferResultDto(
    Guid Id, string Status, decimal Amount, string Currency, string? Comment,
    DateTimeOffset CreatedAt, string Recipient, decimal BalanceAfter);

public sealed record ProblemDto(string? Title, string? Detail, int? Status);
