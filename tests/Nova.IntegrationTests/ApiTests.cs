using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nova.IntegrationTests;

/// <summary>Сквозной сценарий через HTTP: так же, как приложение будет ходить в API.</summary>
[Collection(NovaCollection.Name)]
public class ApiTests(NovaFixture f)
{
    private const string Password = "TestPassword123";

    private async Task<HttpClient> LoginAsync(string phone)
    {
        var http = f.Factory.CreateClient();
        var res = await http.PostAsJsonAsync("/api/auth/client/login", new { phone, password = Password });
        res.EnsureSuccessStatusCode();
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", body.GetProperty("accessToken").GetString());
        return http;
    }

    [Fact]
    public async Task Transfer_by_phone_end_to_end()
    {
        var alice = await f.CreateClientAsync(2_000m, Password);
        var bob = await f.CreateClientAsync(0m, Password);
        var http = await LoginAsync(alice.Phone);

        // 1. Получатель по телефону — видно только имя и первую букву фамилии.
        var recipient = await http.GetFromJsonAsync<JsonElement>($"/api/transfers/recipient?phone={Uri.EscapeDataString(bob.Phone)}");
        Assert.Equal("Тест Т.", recipient.GetProperty("name").GetString());

        // 2. Перевод с ключом идемпотентности.
        var key = Guid.NewGuid().ToString();
        var request = new { fromAccountId = alice.AccountId, toPhone = bob.Phone, amount = 750m, comment = "За обед" };

        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/transfers") { Content = JsonContent.Create(request) };
        first.Headers.Add("Idempotency-Key", key);
        var created = await http.SendAsync(first);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var transfer = await created.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1_250m, transfer.GetProperty("balanceAfter").GetDecimal());

        // 3. Повтор того же запроса (обрыв сети, повторное нажатие) — 200 и тот же перевод.
        using var retry = new HttpRequestMessage(HttpMethod.Post, "/api/transfers") { Content = JsonContent.Create(request) };
        retry.Headers.Add("Idempotency-Key", key);
        var repeated = await http.SendAsync(retry);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        var again = await repeated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(transfer.GetProperty("id").GetGuid(), again.GetProperty("id").GetGuid());
        Assert.Equal(1_250m, await f.BalanceAsync(alice.AccountId));

        // 4. Выписка: последняя операция — наш перевод со знаком минус и комментарием.
        var statement = await http.GetFromJsonAsync<JsonElement>($"/api/accounts/{alice.AccountId}/statement");
        var last = statement.GetProperty("items")[0];
        Assert.Equal(-750m, last.GetProperty("amount").GetDecimal());
        Assert.Equal("За обед", last.GetProperty("description").GetString());
        Assert.Equal("Тест Т.", last.GetProperty("counterparty").GetString());
    }

    [Fact]
    public async Task Transfer_without_idempotency_key_is_rejected()
    {
        var alice = await f.CreateClientAsync(1_000m, Password);
        var bob = await f.CreateClientAsync(0m, Password);
        var http = await LoginAsync(alice.Phone);

        var res = await http.PostAsJsonAsync("/api/transfers",
            new { fromAccountId = alice.AccountId, toPhone = bob.Phone, amount = 10m });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(1_000m, await f.BalanceAsync(alice.AccountId));
    }

    [Fact]
    public async Task Client_cannot_read_foreign_statement_or_system_data()
    {
        var alice = await f.CreateClientAsync(0m, Password);
        var bob = await f.CreateClientAsync(0m, Password);
        var http = await LoginAsync(alice.Phone);

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/api/accounts/{bob.AccountId}/statement")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/system/stats")).StatusCode);
    }

    [Fact]
    public async Task Anonymous_request_is_unauthorized()
    {
        var http = f.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/accounts")).StatusCode);
    }
}
