namespace Nova.Domain.Security;

/// <summary>Атомарные права. Роль = набор прав; проверки в коде идут по правам, не по ролям.</summary>
public static class Permissions
{
    /// <summary>Все права — для регистрации политик авторизации.</summary>
    public static IEnumerable<string> All() =>
        typeof(Permissions).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!);

    public const string ClientsRead = "clients.read";
    public const string ClientsWrite = "clients.write";
    public const string ClientsPersonalData = "clients.personal_data";
    public const string AccountsOpen = "accounts.open";
    public const string AccountsFreeze = "accounts.freeze";
    public const string CardsManage = "cards.manage";
    public const string CardsBlock = "cards.block";
    public const string CreditsApprove = "credits.approve";
    public const string FraudReview = "fraud.review";
    public const string ReportsRead = "reports.read";
    public const string AuditRead = "audit.read";
    public const string StaffManage = "staff.manage";
    public const string RatesPropose = "rates.propose";
    public const string RatesApprove = "rates.approve";
    public const string BankDashboard = "bank.dashboard";
    public const string BankEmergencyStop = "bank.emergency_stop";
    public const string BalanceAdjust = "balance.adjust";
}

/// <summary>Коды ролей и матрица прав (засевается в БД миграцией).</summary>
public static class Roles
{
    public const string Owner = "owner";
    public const string Operator = "operator";
    public const string Support = "support";
    public const string CreditOfficer = "credit_officer";
    public const string RiskManager = "risk_manager";
    public const string Accountant = "accountant";
    public const string Auditor = "auditor";
    public const string SysAdmin = "sysadmin";

    /// <summary>Роли, которым без второго фактора не выдаётся ни одного права (только настройка 2FA).</summary>
    public static readonly IReadOnlySet<string> RequireMfa = new HashSet<string> { Owner };

    public sealed record Definition(Guid Id, string Code, string Name, string[] Permissions);

    public static readonly Definition[] All =
    [
        new(Guid.Parse("00000000-0000-0000-0000-000000000001"), Owner, "Владелец банка",
        [
            Permissions.BankDashboard, Permissions.ReportsRead, Permissions.AuditRead,
            Permissions.StaffManage, Permissions.RatesPropose, Permissions.BankEmergencyStop,
            Permissions.BalanceAdjust, Permissions.ClientsRead
        ]),
        new(Guid.Parse("00000000-0000-0000-0000-000000000002"), Operator, "Операционист",
            [Permissions.ClientsRead, Permissions.ClientsWrite, Permissions.AccountsOpen, Permissions.CardsManage]),
        new(Guid.Parse("00000000-0000-0000-0000-000000000003"), Support, "Специалист поддержки",
            [Permissions.ClientsRead, Permissions.CardsBlock]),
        new(Guid.Parse("00000000-0000-0000-0000-000000000004"), CreditOfficer, "Кредитный специалист",
            [Permissions.ClientsRead, Permissions.ClientsPersonalData, Permissions.CreditsApprove]),
        new(Guid.Parse("00000000-0000-0000-0000-000000000005"), RiskManager, "Риск-менеджер",
            [Permissions.ClientsRead, Permissions.FraudReview, Permissions.AccountsFreeze, Permissions.BankEmergencyStop]),
        new(Guid.Parse("00000000-0000-0000-0000-000000000006"), Accountant, "Бухгалтер",
            [Permissions.ReportsRead, Permissions.RatesApprove]),
        new(Guid.Parse("00000000-0000-0000-0000-000000000007"), Auditor, "Аудитор",
            [Permissions.AuditRead, Permissions.ReportsRead]),
        new(Guid.Parse("00000000-0000-0000-0000-000000000008"), SysAdmin, "Системный администратор",
            [Permissions.StaffManage]),
    ];
}
