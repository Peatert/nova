namespace Nova.Domain.Entities;

public class Staff
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Login { get; set; }
    public required string FullName { get; set; }
    public required string PasswordHash { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public bool IsActive { get; set; } = true;
    public string? TotpSecretEnc { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Role
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public string[] Permissions { get; set; } = [];
}
