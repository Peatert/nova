using System.Security.Claims;
using Nova.Domain.Security;

namespace Nova.Admin;

/// <summary>Стартовая страница сотрудника — первый раздел, на который у его роли есть право.</summary>
public static class AdminHome
{
    private static readonly (string Permission, string Url)[] Sections =
    [
        (Permissions.BankDashboard, "/"),
        (Permissions.ClientsRead, "/clients"),
        (Permissions.ReportsRead, "/reconcile"),
        (Permissions.AuditRead, "/audit"),
    ];

    public static string For(ClaimsPrincipal user) => For(user.FindAll("perm").Select(c => c.Value));

    public static string For(IEnumerable<string> permissions)
    {
        var set = permissions.ToHashSet();
        foreach (var (permission, url) in Sections)
            if (set.Contains(permission)) return url;
        return "/denied";
    }
}
