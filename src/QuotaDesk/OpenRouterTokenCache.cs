using ModelContextProtocol.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIUsageMonitor;

/// <summary>Stores the MCP OAuth token container encrypted for the signed-in Windows user.</summary>
internal sealed class OpenRouterTokenCache : ITokenCache
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly byte[] _entropy;
    private readonly string _path;

    /// <summary>Each provider keeps its own file and entropy, so one provider's tokens never unlock another's.</summary>
    public OpenRouterTokenCache(string fileName = "openrouter-mcp-token.dat", string entropy = "QuotaDesk.OpenRouter.MCP.v1")
    {
        _entropy = Encoding.UTF8.GetBytes(entropy);
        _path = Path.Combine(AppPaths.DataDirectory, fileName);
    }

    public async ValueTask StoreTokensAsync(TokenContainer tokens, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? plain = null;
        byte[]? encrypted = null;
        string? temporary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            plain = JsonSerializer.SerializeToUtf8Bytes(tokens, JsonOptions);
            encrypted = ProtectedData.Protect(plain, _entropy, DataProtectionScope.CurrentUser);
            temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temporary, encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _path, overwrite: true);
            temporary = null;
        }
        finally
        {
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            if (temporary is not null)
            {
                try { File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            _gate.Release();
        }
    }

    public async ValueTask<TokenContainer?> GetTokensAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? encrypted = null;
        byte[]? plain = null;
        try
        {
            if (!File.Exists(_path)) return null;
            encrypted = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
            plain = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<TokenContainer>(plain, JsonOptions);
        }
        catch (CryptographicException)
        {
            DeleteUnreadableCache();
            return null;
        }
        catch (JsonException)
        {
            DeleteUnreadableCache();
            return null;
        }
        finally
        {
            if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
            _gate.Release();
        }
    }

    /// <summary>Forgets the saved login, so the next connection signs in again (used to switch accounts).</summary>
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { DeleteUnreadableCache(); }
        finally { _gate.Release(); }
    }

    private void DeleteUnreadableCache()
    {
        try { File.Delete(_path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
