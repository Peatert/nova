using Nova.Domain.Cards;

namespace Nova.Tests;

public class LuhnTests
{
    [Theory]
    [InlineData("4111111111111111")] // классический тестовый номер Visa
    [InlineData("5555555555554444")] // тестовый Mastercard
    [InlineData("79927398713")]      // пример из описания алгоритма
    public void Known_valid_numbers_pass(string pan) => Assert.True(Luhn.IsValid(pan));

    [Theory]
    [InlineData("4111111111111112")]
    [InlineData("79927398710")]
    [InlineData("1")]
    public void Invalid_numbers_fail(string pan) => Assert.False(Luhn.IsValid(pan));

    [Fact]
    public void Check_digit_completes_valid_number()
    {
        var body = "999999123456789";
        var pan = body + Luhn.CheckDigit(body);
        Assert.True(Luhn.IsValid(pan));
    }
}
