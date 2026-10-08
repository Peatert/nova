using Nova.Domain;
using Nova.Domain.Entities;

namespace Nova.Tests;

public class AccountTests
{
    private static Account NewAccount(decimal balance = 0, decimal creditLimit = 0)
    {
        var a = new Account { Number = "40817810000000000001", CreditLimit = creditLimit };
        if (balance > 0) a.Credit(balance);
        return a;
    }

    [Fact]
    public void Debit_decreases_balance()
    {
        var a = NewAccount(1000m);
        a.Debit(250.50m);
        Assert.Equal(749.50m, a.Balance);
    }

    [Fact]
    public void Debit_more_than_balance_throws_on_debit_account()
    {
        var a = NewAccount(100m);
        var ex = Assert.Throws<DomainException>(() => a.Debit(100.01m));
        Assert.Equal("insufficient_funds", ex.Code);
        Assert.Equal(100m, a.Balance);
    }

    [Fact]
    public void Credit_account_can_go_negative_within_limit()
    {
        var a = NewAccount(0m, creditLimit: 5000m);
        a.Debit(5000m);
        Assert.Equal(-5000m, a.Balance);
        Assert.Throws<DomainException>(() => a.Debit(0.01m));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    [InlineData(0.001)]
    public void Invalid_amount_is_rejected(double raw)
    {
        var amount = (decimal)raw;
        var a = NewAccount(1000m);
        Assert.Throws<DomainException>(() => a.Debit(amount));
        Assert.Throws<DomainException>(() => a.Credit(amount));
    }

    [Fact]
    public void Frozen_account_cannot_be_debited_but_can_receive()
    {
        var a = NewAccount(1000m);
        a.Status = AccountStatus.Frozen;
        Assert.Throws<DomainException>(() => a.Debit(10m));
        a.Credit(10m);
        Assert.Equal(1010m, a.Balance);
    }
}
