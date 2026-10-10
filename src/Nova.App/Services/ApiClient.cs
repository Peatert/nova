using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nova.Contracts;

namespace Nova.App.Services;

public sealed class ApiException(int status, string message) : Exception(message)
{
    /// <summary>HTTP-статус; 0 — нет связи с сервером.</summary>
    public int Status { get; } = status;
}

/// <summary>
/// Клиент API Nova.
///  • Access-токен подставляется в каждый запрос.
///  • Получили 401 — один раз тихо обновляем токены через refresh и повторяем запрос.
///    Пользователь не замечает, что access-токен живёт всего 15 минут.
///  • Несколько запросов одновременно получили 401 — refresh выполняется один раз (семафор), остальные ждут.
///  • Refresh не прошёл — сессия закончена, событие SessionExpired отправляет на экран входа.
/// </summary>
public sealed class ApiClient(TokenStore tokens)
{
    /// <summary>
    /// По умолчанию 127.0.0.1: на телефоне этот адрес пробрасывается на ПК командой «adb reverse tcp:5080 tcp:5080».
    /// </summary>
    public const string DefaultServer = "http://127.0.0.1:5080";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public event Action? SessionExpired;

    public string Server
    {
        get => Preferences.Default.Get("server", DefaultServer);
        set => Preferences.Default.Set("server", string.IsNullOrWhiteSpace(value) ? DefaultServer : value.Trim().TrimEnd('/'));
    }

    // ───────────── Вход ─────────────

    public Task<LoginResponse> LoginAsync(string phone, string password) =>
        PostAnonymousAsync<LoginResponse>("/api/auth/client/login", new ClientLoginRequest(phone, password));

    public Task<LoginResponse> VerifyTwoFactorAsync(string mfaToken, string code) =>
        PostAnonymousAsync<LoginResponse>("/api/auth/2fa", new TwoFactorRequest(mfaToken, code));

    public async Task LogoutAsync()
    {
        if (tokens.RefreshToken is { } refresh)
        {
            try { await PostAnonymousRawAsync("/api/auth/logout", new RefreshRequest(refresh)); }
            catch (ApiException) { /* нет связи — всё равно забываем сессию локально */ }
        }
        await tokens.ClearAsync();
    }

    // ───────────── Данные ─────────────

    public Task<ProfileDto> MeAsync() => GetAsync<ProfileDto>("/api/me");

    public Task<List<AccountDto>> AccountsAsync() => GetAsync<List<AccountDto>>("/api/accounts");

    public Task<StatementDto> StatementAsync(Guid accountId, DateTimeOffset? before, int limit)
    {
        var path = $"/api/accounts/{accountId}/statement?limit={limit}";
        if (before is { } b) path += "&before=" + Uri.EscapeDataString(b.ToString("O"));
        return GetAsync<StatementDto>(path);
    }

    public Task<RecipientDto> RecipientAsync(string phone) =>
        GetAsync<RecipientDto>("/api/transfers/recipient?phone=" + Uri.EscapeDataString(phone));

    /// <summary>
    /// Перевод. Ключ идемпотентности создаёт вызывающий код — один на одну попытку перевода.
    /// Повтор после обрыва связи с тем же ключом вернёт уже сделанный перевод, а не спишет деньги второй раз.
    /// </summary>
    public Task<TransferResultDto> TransferAsync(TransferRequest request, Guid idempotencyKey) =>
        SendAuthorizedAsync<TransferResultDto>(() =>
        {
            var msg = new HttpRequestMessage(HttpMethod.Post, Url("/api/transfers"))
            {
                Content = JsonContent.Create(request, options: Json),
            };
            msg.Headers.Add("Idempotency-Key", idempotencyKey.ToString());
            return msg;
        });

    // ───────────── Транспорт ─────────────

    private Uri Url(string path) => new(Server + path);

    private Task<T> GetAsync<T>(string path) =>
        SendAuthorizedAsync<T>(() => new HttpRequestMessage(HttpMethod.Get, Url(path)));

    private async Task<T> SendAuthorizedAsync<T>(Func<HttpRequestMessage> build)
    {
        await tokens.LoadAsync();
        var response = await SendWithTokenAsync(build);

        if (response.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshAsync())
            response = await SendWithTokenAsync(build); // запрос пересоздаётся: старое тело уже прочитано

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            await tokens.ClearAsync();
            SessionExpired?.Invoke();
            throw new ApiException(401, "Сессия истекла, войдите снова");
        }
        return await ReadAsync<T>(response);
    }

    private async Task<HttpResponseMessage> SendWithTokenAsync(Func<HttpRequestMessage> build)
    {
        var request = build();
        if (tokens.AccessToken is { } access)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return await SendAsync(request);
    }

    private async Task<bool> TryRefreshAsync()
    {
        var staleAccess = tokens.AccessToken;
        await _refreshLock.WaitAsync();
        try
        {
            // Пока ждали семафор, другой запрос уже обновил токены — пользуемся ими.
            if (tokens.AccessToken != staleAccess && tokens.AccessToken is not null) return true;
            if (tokens.RefreshToken is not { } refresh) return false;

            var response = await SendAsync(new HttpRequestMessage(HttpMethod.Post, Url("/api/auth/refresh"))
            {
                Content = JsonContent.Create(new RefreshRequest(refresh), options: Json),
            });
            if (!response.IsSuccessStatusCode) return false;

            var pair = await response.Content.ReadFromJsonAsync<LoginResponse>(Json);
            if (pair?.AccessToken is null || pair.RefreshToken is null) return false;
            await tokens.SaveAsync(pair.AccessToken, pair.RefreshToken);
            return true;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<T> PostAnonymousAsync<T>(string path, object body) =>
        await ReadAsync<T>(await PostAnonymousRawAsync(path, body));

    private Task<HttpResponseMessage> PostAnonymousRawAsync(string path, object body) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Post, Url(path))
        {
            Content = JsonContent.Create(body, body.GetType(), options: Json),
        });

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        try
        {
            return await _http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(0, $"Нет связи с сервером {Server}. Запущен ли API и выполнен ли adb reverse?");
        }
        catch (TaskCanceledException)
        {
            throw new ApiException(0, "Сервер не ответил за 15 секунд");
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return await response.Content.ReadFromJsonAsync<T>(Json)
                   ?? throw new ApiException((int)response.StatusCode, "Пустой ответ сервера");

        // Ошибки API приходят в формате ProblemDetails: title — суть, detail — пояснение.
        ProblemDto? problem = null;
        try { problem = await response.Content.ReadFromJsonAsync<ProblemDto>(Json); } catch (JsonException) { }

        var message = problem?.Title ?? response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "Недостаточно прав",
            HttpStatusCode.TooManyRequests => "Слишком много попыток, подождите минуту",
            _ => $"Ошибка сервера ({(int)response.StatusCode})",
        };
        if (!string.IsNullOrWhiteSpace(problem?.Detail)) message += ". " + problem.Detail;
        throw new ApiException((int)response.StatusCode, message);
    }
}
