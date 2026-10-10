using System.Text;
using Nova.Domain.Clients;
using Nova.Domain.Entities;
using Nova.Domain.Security;
using Nova.Infrastructure.Security;

namespace Nova.Tests;

public class TotpTests
{
    // Тестовые векторы из RFC 6238, приложение B (SHA-1, ключ "12345678901234567890").
    private static readonly byte[] RfcKey = Encoding.ASCII.GetBytes("12345678901234567890");

    [Theory]
    [InlineData(59L, "94287082")]
    [InlineData(1111111109L, "07081804")]
    [InlineData(1111111111L, "14050471")]
    [InlineData(1234567890L, "89005924")]
    [InlineData(2000000000L, "69279037")]
    public void Matches_rfc6238_vectors(long unixTime, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixTime));
        Assert.Equal(expected, Totp.Compute(RfcKey, step, digits: 8));
    }

    [Fact]
    public void Base32_roundtrip()
    {
        var bytes = Encoding.ASCII.GetBytes("12345678901234567890");
        var encoded = Base32.Encode(bytes);
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", encoded);
        Assert.Equal(bytes, Base32.Decode(encoded));
    }

    [Fact]
    public void Accepts_current_and_adjacent_step_rejects_far()
    {
        var secret = Base32.Encode(RfcKey);
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var step = Totp.StepAt(now);
        var key = Base32.Decode(secret);

        Assert.True(Totp.Verify(secret, Totp.Compute(key, step), now, null, out _));
        Assert.True(Totp.Verify(secret, Totp.Compute(key, step - 1), now, null, out _));
        Assert.False(Totp.Verify(secret, Totp.Compute(key, step - 3), now, null, out _));
    }

    [Fact]
    public void Same_code_cannot_be_used_twice()
    {
        var secret = Base32.Encode(RfcKey);
        var now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var code = Totp.Compute(Base32.Decode(secret), Totp.StepAt(now));

        Assert.True(Totp.Verify(secret, code, now, null, out var used));
        Assert.False(Totp.Verify(secret, code, now, used, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("abcdef")]
    public void Malformed_code_is_rejected(string? code)
    {
        Assert.False(Totp.Verify(Base32.Encode(RfcKey), code, DateTimeOffset.UtcNow, null, out _));
    }
}

public class LoginPolicyTests
{
    private static Client NewClient() => new() { FullName = "Тест", Phone = "+79990000000", PasswordHash = "x" };

    [Fact]
    public void Locks_after_five_failures()
    {
        var c = NewClient();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < LoginPolicy.MaxFailedAttempts - 1; i++)
            Assert.False(c.RegisterFailure(now));

        Assert.True(c.RegisterFailure(now));
        Assert.True(c.IsLocked(now));
        Assert.False(c.IsLocked(now + LoginPolicy.LockoutDuration + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Success_resets_counter()
    {
        var c = NewClient();
        var now = DateTimeOffset.UtcNow;
        c.RegisterFailure(now);
        c.RegisterFailure(now);
        c.RegisterSuccess();
        Assert.Equal(0, c.FailedLoginCount);
        Assert.Null(c.LockedUntil);
    }
}

public class PhoneAndPasswordTests
{
    [Theory]
    [InlineData("+7 (999) 123-45-67", "+79991234567")]
    [InlineData("89991234567", "+79991234567")]
    [InlineData("9991234567", "+79991234567")]
    [InlineData("+79990000000", "+79990000000")]
    public void Normalizes_russian_mobile(string input, string expected)
    {
        Assert.True(PhoneNumber.TryNormalize(input, out var n));
        Assert.Equal(expected, n);
    }

    [Theory]
    [InlineData("")]
    [InlineData("+7 495 123-45-67")] // городской
    [InlineData("+1 999 123 4567")]
    [InlineData("+7999123456")]      // не хватает цифры
    [InlineData("+7999abc4567")]
    public void Rejects_bad_numbers(string input) => Assert.False(PhoneNumber.TryNormalize(input, out _));

    [Fact]
    public void Masks_phone() => Assert.Equal("+7999***4567", PhoneNumber.Mask("+79991234567"));

    [Theory]
    [InlineData("short1", false)]
    [InlineData("onlyletterspassword", false)]
    [InlineData("1234567890123", false)]
    [InlineData("Nova-client-2026!", true)]
    public void Password_policy(string password, bool ok) => Assert.Equal(ok, PasswordPolicy.Validate(password) is null);
}
