using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace AIUsageMonitor;

internal sealed record OpenRouterCredits(decimal TotalCredits, decimal TotalUsage)
{
    public decimal Remaining => Math.Max(0, TotalCredits - TotalUsage);
    public double UsedPercent => TotalCredits == 0 ? 100 : (double)Math.Clamp(TotalUsage / TotalCredits * 100m, 0m, 100m);
}

internal sealed class OpenRouterAuthorizationRequiredException : Exception
{
    public OpenRouterAuthorizationRequiredException() : base("MCP authorization is missing, expired, or revoked.") { }
}

/// <summary>Connects to OpenRouter's remote MCP endpoint and invokes only its read-only get-credits tool.</summary>
internal sealed class OpenRouterMcpClient
{
    private static readonly Uri Endpoint = new("https://mcp.openrouter.ai/mcp");
    private readonly OpenRouterTokenCache _tokenCache;

    public OpenRouterMcpClient(OpenRouterTokenCache tokenCache) => _tokenCache = tokenCache;

    public async Task<OpenRouterCredits> GetCreditsAsync(bool allowAuthorization, CancellationToken cancellationToken)
    {
        var result = await RemoteMcpTool.CallAsync(Endpoint, "OpenRouter", "get-credits", _tokenCache, allowAuthorization, cancellationToken)
            .ConfigureAwait(false);
        return ParseCredits(result);
    }

    private static OpenRouterCredits ParseCredits(CallToolResult result)
    {
        if (result.StructuredContent is { } structured && structured.ValueKind == JsonValueKind.Object &&
            TryParseCredits(structured, out var credits))
            return credits;

        var text = string.Join(Environment.NewLine, result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException("OpenRouter returned no credit data.");

        var trimmed = text.Trim();
        if (trimmed.StartsWith("```") && trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0) trimmed = trimmed[(firstNewline + 1)..^3].Trim();
        }
        try
        {
            using var document = JsonDocument.Parse(trimmed);
            if (TryParseCredits(document.RootElement, out credits)) return credits;
        }
        catch (JsonException) { }

        throw new InvalidDataException("OpenRouter returned an unexpected get-credits response shape.");
    }

    internal static bool TryParseCredits(JsonElement root, out OpenRouterCredits credits)
    {
        var total = FindNumber(root, "total_credits", "totalCredits");
        var used = FindNumber(root, "total_usage", "totalUsage");
        if (total is null || used is null || total < 0 || used < 0) // used > total: overspent account, shown as 100% used
        {
            credits = null!;
            return false;
        }

        credits = new OpenRouterCredits(total.Value, used.Value);
        return true;
    }

    private static decimal? FindNumber(JsonElement element, params string[] names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) && TryDecimal(property.Value, out var value))
                        return value;
                }
                foreach (var property in element.EnumerateObject())
                {
                    var nested = FindNumber(property.Value, names);
                    if (nested is not null) return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindNumber(item, names);
                    if (nested is not null) return nested;
                }
                break;
        }
        return null;
    }

    private static bool TryDecimal(JsonElement value, out decimal number)
    {
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetDecimal(out number);
        if (value.ValueKind == JsonValueKind.String)
            return decimal.TryParse(value.GetString(), NumberStyles.Number | NumberStyles.Currency,
                CultureInfo.InvariantCulture, out number);
        number = 0;
        return false;
    }
}

/// <summary>
/// Calls one read-only tool on a remote MCP server behind OAuth. Background refreshes never open a browser: without
/// cached tokens, or when the server asks for a new login, they fail with <see cref="OpenRouterAuthorizationRequiredException"/>.
/// </summary>
internal static class RemoteMcpTool
{
    private static readonly TimeSpan InteractiveMcpTimeout = TimeSpan.FromMinutes(16);

    public static async Task<CallToolResult> CallAsync(Uri endpoint, string provider, string tool, ITokenCache tokenCache,
        bool allowAuthorization, CancellationToken cancellationToken)
    {
        var existingTokens = await tokenCache.GetTokensAsync(cancellationToken).ConfigureAwait(false);
        if (existingTokens is null && !allowAuthorization)
            throw new OpenRouterAuthorizationRequiredException();

        using var callback = new OpenRouterLoopbackCallback(provider);
        var interactiveAuthorizationBlocked = false;
        var oauth = new ClientOAuthOptions
        {
            RedirectUri = callback.RedirectUri,
            TokenCache = tokenCache,
            DynamicClientRegistration = new DynamicClientRegistrationOptions
            {
                ClientName = "QuotaDesk",
                ClientUri = new Uri("https://github.com/srbunnie/QuotaDesk")
            },
            AuthorizationCallbackHandler = (context, token) =>
            {
                if (!allowAuthorization)
                {
                    interactiveAuthorizationBlocked = true;
                    return Task.FromResult<AuthorizationResult?>(null);
                }
                return callback.AuthorizeAsync(context, token);
            }
        };

        // OAuth runs inside the MCP HTTP request. Keep the HTTP and initialization deadlines
        // longer than the callback listener so a human login does not cancel the loopback listener.
        var requestTimeout = allowAuthorization ? InteractiveMcpTimeout : TimeSpan.FromSeconds(30);
        using var httpClient = new HttpClient { Timeout = requestTimeout };
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            Name = provider,
            TransportMode = HttpTransportMode.StreamableHttp,
            EnableStandaloneGetStream = false,
            OAuth = oauth
        };

        try
        {
            await using var transport = new HttpClientTransport(transportOptions, httpClient);
            var clientOptions = new McpClientOptions { InitializationTimeout = requestTimeout };
            await using var client = await McpClient.CreateAsync(transport, clientOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
            var result = await client.CallToolAsync(tool, new Dictionary<string, object?>(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (result.IsError == true)
                throw new InvalidDataException($"The {provider} {tool} tool returned an error.");
            return result;
        }
        catch (Exception) when (interactiveAuthorizationBlocked)
        {
            throw new OpenRouterAuthorizationRequiredException();
        }
    }

    /// <summary>
    /// Diagnostic: lists the server's tools with the saved login, without calling any of them and without ever opening
    /// a browser (a missing or expired login fails with <see cref="OpenRouterAuthorizationRequiredException"/>).
    /// </summary>
    public static async Task<IList<McpClientTool>> ListToolsAsync(Uri endpoint, string provider, ITokenCache tokenCache,
        CancellationToken cancellationToken)
    {
        if (await tokenCache.GetTokensAsync(cancellationToken).ConfigureAwait(false) is null)
            throw new OpenRouterAuthorizationRequiredException();
        var blocked = false;
        var oauth = new ClientOAuthOptions
        {
            RedirectUri = new Uri("http://127.0.0.1/oauth/callback"),
            TokenCache = tokenCache,
            DynamicClientRegistration = new DynamicClientRegistrationOptions
            {
                ClientName = "QuotaDesk",
                ClientUri = new Uri("https://github.com/srbunnie/QuotaDesk")
            },
            AuthorizationCallbackHandler = (_, _) => { blocked = true; return Task.FromResult<AuthorizationResult?>(null); }
        };
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        try
        {
            await using var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                Name = provider,
                TransportMode = HttpTransportMode.StreamableHttp,
                EnableStandaloneGetStream = false,
                OAuth = oauth
            }, httpClient);
            await using var client = await McpClient.CreateAsync(transport,
                new McpClientOptions { InitializationTimeout = TimeSpan.FromSeconds(30) }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (blocked)
        {
            throw new OpenRouterAuthorizationRequiredException();
        }
    }
}

/// <summary>Short-lived 127.0.0.1 listener used only while the user completes a provider's OAuth in their browser.</summary>
internal sealed class OpenRouterLoopbackCallback : IDisposable
{
    private readonly string _provider;
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(15);
    private readonly TcpListener _listener;
    private bool _disposed;

    public Uri RedirectUri { get; }

    public OpenRouterLoopbackCallback(string provider = "OpenRouter")
    {
        _provider = provider;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        RedirectUri = new Uri($"http://127.0.0.1:{port}/oauth/callback");
    }

    public async Task<AuthorizationResult?> AuthorizeAsync(AuthorizationCallbackContext context, CancellationToken cancellationToken)
    {
        if (context.RedirectUri != RedirectUri)
            throw new InvalidOperationException("OAuth returned an unexpected loopback redirect URI.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AuthorizationTimeout);
        // ShellExecute runs whatever it is handed: only ever open an https page from the server's OAuth metadata.
        if (context.AuthorizationUri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("OAuth returned a non-HTTPS authorization URI.");
        Process.Start(new ProcessStartInfo(context.AuthorizationUri.AbsoluteUri) { UseShellExecute = true });

        while (true)
        {
            using var client = await _listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
            // Browsers open spare preconnect sockets that never send a request: give each connection a few seconds
            // and move on, so the real callback waiting in the backlog is not starved; a reset connection is skipped too.
            using var connection = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            connection.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var (done, result) = await HandleConnectionAsync(client, connection.Token).ConfigureAwait(false);
                if (done) return result;
            }
            catch (Exception ex) when (!timeout.IsCancellationRequested &&
                ex is IOException or OperationCanceledException or SocketException or UriFormatException) { }
        }
    }

    private async Task<(bool Done, AuthorizationResult? Result)> HandleConnectionAsync(TcpClient client, CancellationToken token)
    {
        if (client.Client.RemoteEndPoint is not IPEndPoint remote || !IPAddress.IsLoopback(remote.Address)) return (false, null);
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(token).ConfigureAwait(false);
        if (requestLine is null || requestLine.Length > 4096)
        {
            await RespondAsync(stream, L.T("Requisição inválida. Volte ao QuotaDesk.", "Invalid request. Go back to QuotaDesk."), token).ConfigureAwait(false);
            return (false, null);
        }
        string? header;
        var headerCount = 0;
        var malformedHeaders = false;
        while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync(token).ConfigureAwait(false)))
        {
            if (++headerCount > 32 || header.Length > 8192)
            {
                malformedHeaders = true;
                break;
            }
        }
        if (malformedHeaders)
        {
            await RespondAsync(stream, L.T("Cabeçalhos inválidos. Volte ao QuotaDesk.", "Invalid headers. Go back to QuotaDesk."), token).ConfigureAwait(false);
            return (false, null);
        }

        var parts = requestLine?.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 3 } || parts[0] != "GET")
        {
            await RespondAsync(stream, L.T("Acesso recusado. Volte ao QuotaDesk.", "Access refused. Go back to QuotaDesk."), token).ConfigureAwait(false);
            return (false, null);
        }

        var callbackUri = new Uri(new Uri($"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}"), parts[1]);
        if (!string.Equals(callbackUri.AbsolutePath, RedirectUri.AbsolutePath, StringComparison.Ordinal))
        {
            await RespondAsync(stream, L.T("Rota de retorno inválida. Volte ao QuotaDesk.", "Invalid return route. Go back to QuotaDesk."), token).ConfigureAwait(false);
            return (false, null);
        }

        var query = ParseQuery(callbackUri.Query);
        var denied = query.ContainsKey("error");
        await RespondAsync(stream, denied
            ? L.T("A autorização não foi concluída. Você pode fechar esta página e voltar ao QuotaDesk.",
                "Authorization was not completed. You can close this page and go back to QuotaDesk.")
            : L.T($"{_provider} autorizado. Você pode fechar esta página e voltar ao QuotaDesk.",
                $"{_provider} authorized. You can close this page and go back to QuotaDesk."), token).ConfigureAwait(false);

        if (denied) return (true, null);
        return (true, new AuthorizationResult
        {
            Code = query.GetValueOrDefault("code"),
            State = query.GetValueOrDefault("state"),
            Iss = query.GetValueOrDefault("iss")
        });

    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0].Replace('+', ' '));
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : "";
            result[key] = value;
        }
        return result;
    }

    private static async Task RespondAsync(NetworkStream stream, string message, CancellationToken cancellationToken)
    {
        var body = Encoding.UTF8.GetBytes($"<!doctype html><meta charset=\"utf-8\"><title>QuotaDesk</title><p>{message}</p>");
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _listener.Stop();
    }
}
