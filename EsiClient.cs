using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EveSessionTracker;

public class EsiClient
{
    // ── ESI / SSO constants ───────────────────────────────────────────────────
    private const string AuthUrl    = "https://login.eveonline.com/v2/oauth/authorize";
    private const string TokenUrl   = "https://login.eveonline.com/v2/oauth/token";
    private const string EsiBase    = "https://esi.evetech.net/latest";
    private const string Scope      = "esi-wallet.read_character_wallet.v1";
    private const string RedirectUri = "http://localhost:8080/callback";

    private static readonly HttpClient Http = new()
    {
        DefaultRequestHeaders = { { "User-Agent", "EveSessionTracker/1.0" } }
    };

    private static readonly Dictionary<long, string> SystemNameCache = [];

    private readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens a browser for EVE SSO login, waits for the OAuth callback,
    /// and returns a populated CharacterToken. Throws on failure or cancellation.
    /// </summary>
    public async Task<CharacterToken> AuthorizeAsync(string clientId, string clientSecret = "",
        IProgress<string>? progress = null)
    {
        // 1. PKCE values
        var verifier   = GenerateCodeVerifier();
        var challenge  = GenerateCodeChallenge(verifier);
        var state      = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

        // 2. Build auth URL
        var authUri = $"{AuthUrl}?" +
            $"response_type=code" +
            $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
            $"&client_id={clientId}" +
            $"&scope={Uri.EscapeDataString(Scope)}" +
            $"&state={state}" +
            $"&code_challenge={challenge}" +
            $"&code_challenge_method=S256";

        // 3. Start local HTTP listener BEFORE opening browser
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:8080/");
        try { listener.Start(); }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Could not start local listener on port 8080. " +
                $"Another application may be using it.\n\n{ex.Message}");
        }

        // 4. Open browser
        progress?.Report("Opening EVE SSO login in browser...");
        Process.Start(new ProcessStartInfo(authUri) { UseShellExecute = true });

        // 5. Wait for callback (60 second timeout)
        progress?.Report("Waiting for login callback...");
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        HttpListenerContext context;
        try
        {
            context = await listener.GetContextAsync().WaitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            listener.Stop();
            throw new TimeoutException("Login timed out after 90 seconds.");
        }

        // 6. Parse code and state from query string — no System.Web dependency
        var rawUrl  = context.Request.Url?.ToString() ?? "";
        var qIdx    = rawUrl.IndexOf('?');
        var qPairs  = qIdx >= 0 ? rawUrl[(qIdx + 1)..].Split('&') : [];
        var qs      = qPairs
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(
                p => Uri.UnescapeDataString(p[0]),
                p => Uri.UnescapeDataString(p[1]));

        var code     = qs.TryGetValue("code",  out var c) ? c : null;
        var retState = qs.TryGetValue("state", out var s) ? s : null;

        // Send close-tab response
        var html = "<html><body style='font-family:sans-serif;background:#1a1d22;color:#c0c8d2'>" +
                   "<h2>Authentication successful.</h2>" +
                   "<p>You can close this window and return to the app.</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType     = "text/html";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
        context.Response.Close();
        listener.Stop();

        if (string.IsNullOrEmpty(code))
            throw new InvalidOperationException("No authorization code received from EVE SSO.");

        if (retState != state)
            throw new InvalidOperationException("OAuth state mismatch — possible CSRF.");

        // 7. Exchange code for tokens
        progress?.Report("Exchanging authorization code for tokens...");
        var token = await ExchangeCodeAsync(clientId, clientSecret, code, verifier);

        // 8. Read character info from JWT
        progress?.Report("Identifying character...");
        var (charId, charName) = ParseCharacterFromJwt(token.AccessToken);

        // Default tax rate to 10% - user will be prompted to set actual rate
        return new CharacterToken
        {
            CharacterId    = charId,
            CharacterName  = charName,
            CorporationId  = 0,
            CorpTaxRate    = 10.0,
            AccessToken    = token.AccessToken,
            RefreshToken   = token.RefreshToken,
            TokenExpiry    = DateTime.UtcNow.AddSeconds(token.ExpiresIn - 30)
        };
    }



    /// <summary>
    /// Fetches the most recent wallet journal entries for a character.
    /// Automatically refreshes the access token if needed.
    /// </summary>
    public async Task<List<Transaction>> GetWalletJournalAsync(
        CharacterToken token,
        string clientId, string clientSecret = "",
        int maxPages = 3,
        Action<CharacterToken>? onTokenRefreshed = null)
    {
        token = await EnsureValidTokenAsync(token, clientId, clientSecret, onTokenRefreshed);

        var transactions = new List<Transaction>();

        for (int page = 1; page <= maxPages; page++)
        {
            var url = $"{EsiBase}/characters/{token.CharacterId}/wallet/journal/" +
                      $"?datasource=tranquility&page={page}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token.AccessToken);

            var response = await Http.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Token may have just expired mid-fetch — try one refresh
                token = await RefreshTokenAsync(token, clientId, clientSecret);
                onTokenRefreshed?.Invoke(token);

                using var retry = new HttpRequestMessage(HttpMethod.Get, url);
                retry.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", token.AccessToken);
                response = await Http.SendAsync(retry);
            }

            response.EnsureSuccessStatusCode();

            var json    = await response.Content.ReadAsStringAsync();
            var entries = JsonSerializer.Deserialize<List<EsiJournalEntry>>(json, _jsonOpts)
                          ?? [];

            Console.WriteLine($"\n→ Page {page} received {entries.Count} entries");

            // Resolve system names for entries that reference a system
            var systemIds = entries
                .Where(e => e.ContextIdType == "system_id" && e.ContextId.HasValue)
                .Select(e => e.ContextId!.Value)
                .Distinct()
                .Where(id => !SystemNameCache.ContainsKey(id))
                .ToList();

            foreach (var sid in systemIds)
                SystemNameCache[sid] = await FetchSystemNameAsync(sid);

            // Convert ESI entries to Transactions
            foreach (var entry in entries)
            {
                string sysName = "";
                if (entry.ContextIdType == "system_id" && entry.ContextId.HasValue)
                    SystemNameCache.TryGetValue(entry.ContextId.Value, out sysName!);

                // Debug: log bounty/ESS entries
                if (entry.RefType.StartsWith("bounty") || entry.RefType == "ess_escrow_transfer")
                    Console.WriteLine($"  {entry.RefType} | {entry.Amount:N0} ISK | {entry.Date:MM/dd HH:mm}");

                // Only include bounties, corp tax, and ESS - skip everything else
                if (entry.RefType == "bounty_prizes" || 
                    entry.RefType == "bounty_prize_corporation_tax" || 
                    entry.RefType == "ess_escrow_transfer")
                {
                    transactions.Add(EsiEntryToTransaction(entry, token.CharacterName, sysName ?? ""));
                }
                else
                {
                    // Log skipped transactions for debugging
                    Console.WriteLine($"⚠ Skipping: {entry.RefType} | Amount: {entry.Amount}");
                }
            }

            // Check if there are more pages
            if (!response.Headers.TryGetValues("X-Pages", out var xPages))
                break;
            if (!int.TryParse(xPages.FirstOrDefault(), out int totalPages))
                break;
            if (page >= totalPages)
                break;
        }

        return transactions;
    }

    // ── Token management ──────────────────────────────────────────────────────

    public async Task<CharacterToken> EnsureValidTokenAsync(
        CharacterToken token, string clientId, string clientSecret = "",
        Action<CharacterToken>? onRefreshed = null)
    {
        if (DateTime.UtcNow < token.TokenExpiry)
            return token;

        token = await RefreshTokenAsync(token, clientId, clientSecret);
        onRefreshed?.Invoke(token);
        return token;
    }

    public async Task<CharacterToken> RefreshTokenAsync(CharacterToken token, string clientId, string clientSecret = "")
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("grant_type",    "refresh_token"),
            new("refresh_token", token.RefreshToken),
            new("client_id",     clientId)
        };
        // Include client_secret if this is a confidential client app
        if (!string.IsNullOrEmpty(clientSecret))
            fields.Add(new("client_secret", clientSecret));
        var body = new FormUrlEncodedContent(fields);

        var response = await Http.PostAsync(TokenUrl, body);
        response.EnsureSuccessStatusCode();

        var json  = await response.Content.ReadAsStringAsync();
        var fresh = JsonSerializer.Deserialize<EsiTokenResponse>(json, _jsonOpts)
                    ?? throw new InvalidOperationException("Empty token response.");

        token.AccessToken  = fresh.AccessToken;
        token.RefreshToken = fresh.RefreshToken;  // ESI rotates refresh tokens
        token.TokenExpiry  = DateTime.UtcNow.AddSeconds(fresh.ExpiresIn - 30);
        return token;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<EsiTokenResponse> ExchangeCodeAsync(
        string clientId, string clientSecret, string code, string codeVerifier)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("grant_type",    "authorization_code"),
            new("code",          code),
            new("client_id",     clientId),
            new("code_verifier", codeVerifier),
            new("redirect_uri",  RedirectUri)
        };
        if (!string.IsNullOrEmpty(clientSecret))
            fields.Add(new("client_secret", clientSecret));
        var body = new FormUrlEncodedContent(fields);

        var response = await Http.PostAsync(TokenUrl, body);
        var json     = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Token exchange failed ({response.StatusCode}):\n{json}");

        return JsonSerializer.Deserialize<EsiTokenResponse>(json, _jsonOpts)
               ?? throw new InvalidOperationException("Empty token response.");
    }

    private async Task<string> FetchSystemNameAsync(long systemId)
    {
        try
        {
            var resp = await Http.GetAsync($"{EsiBase}/universe/systems/{systemId}/");
            if (!resp.IsSuccessStatusCode) return systemId.ToString();
            var json = await resp.Content.ReadAsStringAsync();
            var info = JsonSerializer.Deserialize<EsiSystemInfo>(json, _jsonOpts);
            return info?.Name ?? systemId.ToString();
        }
        catch { return systemId.ToString(); }
    }

    private static Transaction EsiEntryToTransaction(
        EsiJournalEntry e, string charName, string sysName)
    {
        var type = e.RefType switch
        {
            "bounty_prizes"                => TransactionType.BountyPrize,
            "bounty_prize_corporation_tax" => TransactionType.BountyPrizeTax,
            "ess_escrow_transfer"          => TransactionType.ESSEscrow,
            _                              => TransactionType.Other
        };

        return new Transaction
        {
            Date          = e.Date.ToUniversalTime(),
            Type          = type,
            Amount        = (long)e.Amount,
            Balance       = (long)e.Balance,
            Description   = e.Description,
            CharacterName = charName,
            System        = sysName
        };
    }

    // ── PKCE helpers ──────────────────────────────────────────────────────────

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string GenerateCodeChallenge(string verifier)
    {
        var bytes   = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    // ── JWT character extraction ──────────────────────────────────────────────
    // EVE JWT sub claim format: "CHARACTER:EVE:{characterId}"

    private static (long Id, string Name) ParseCharacterFromJwt(string jwt)
    {
        try
        {
            var parts   = jwt.Split('.');
            if (parts.Length < 2)
                throw new FormatException("Invalid JWT format.");

            // Restore base64 padding
            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            int pad = payload.Length % 4;
            if (pad != 0) payload = payload.PadRight(payload.Length + (4 - pad), '=');

            var json    = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            var doc     = JsonDocument.Parse(json);
            var root    = doc.RootElement;

            // sub = "CHARACTER:EVE:12345678"
            var sub     = root.GetProperty("sub").GetString() ?? "";
            var parts2  = sub.Split(':');
            long charId = parts2.Length >= 3 ? long.Parse(parts2[2]) : 0;

            // name claim
            var charName = root.TryGetProperty("name", out var nameProp)
                ? nameProp.GetString() ?? "Unknown"
                : "Unknown";

            return (charId, charName);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not parse character from EVE token. {ex.Message}");
        }
    }
}
