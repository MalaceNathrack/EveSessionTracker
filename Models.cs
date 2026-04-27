using System.Text.Json.Serialization;

namespace EveSessionTracker;

// ── Transaction types ─────────────────────────────────────────────────────────
public enum TransactionType
{
    BountyPrize, BountyPrizeTax, ESSEscrow, PlayerDonation,
    PlanetaryExportTax, DailyGoal, JumpBridgeFee, MarketEscrow,
    Insurance, Contract, Other
}

// ── Core transaction (converted from ESI wallet journal entries) ─────────────
public class Transaction
{
    public DateTime        Date          { get; set; }
    public TransactionType Type          { get; set; }
    public long            Amount        { get; set; }
    public long            Balance       { get; set; }
    public string          Description   { get; set; } = "";
    public string          CharacterName { get; set; } = "";
    public string          System        { get; set; } = "";
}

// ── Per-character stats for one session ──────────────────────────────────────
public class CharacterSessionStats
{
    public string       CharacterName     { get; set; } = "";
    public long         BountyGross       { get; set; }
    public int          BountyTicks       { get; set; }
    public long         ESSIncome         { get; set; }
    public int          ESSPayouts        { get; set; }
    public long         CorpTax           { get; set; }
    public long         OtherIncome       { get; set; }
    public List<string> OtherIncomeLabels { get; set; } = [];
    public long         PITax             { get; set; }

    public long Net => BountyGross + ESSIncome + CorpTax;
}

// ── A detected ratting session ────────────────────────────────────────────────
public class RattingSession
{
    public DateTime                    Start        { get; set; }
    public DateTime                    End          { get; set; }
    public List<Transaction>           Transactions { get; set; } = [];
    public List<CharacterSessionStats> CharStats    { get; set; } = [];
    public HashSet<string>             Systems      { get; set; } = [];
    public bool                        IsPinned     { get; set; }

    public TimeSpan Duration     => End - Start;
    public long     TotalBounty  => CharStats.Sum(c => c.BountyGross);
    public long     TotalESS     => CharStats.Sum(c => c.ESSIncome);
    public long     TotalCorpTax => CharStats.Sum(c => c.CorpTax);
    public long     TotalOther   => CharStats.Sum(c => c.OtherIncome);
    public long     TotalPITax   => CharStats.Sum(c => c.PITax);
    public long     TotalNet     => CharStats.Sum(c => c.Net);

    public string ListLabel
    {
        get
        {
            string pin  = IsPinned ? "* " : "  ";
            string net  = (TotalNet / 1_000_000.0).ToString("+#,##0.0;-#,##0.0") + "M";
            string sys  = Systems.Count > 0 ? "  | " + string.Join(" ", Systems.Order().Take(3)) : "";
            return $"{pin}{Start:MM/dd HH:mm}  [{(int)Duration.TotalHours}h{Duration.Minutes:D2}m]  {net}{sys}";
        }
    }
}

// ── Character auth info (stored on disk) ─────────────────────────────────────
public class CharacterToken
{
    public long     CharacterId      { get; set; }
    public string   CharacterName    { get; set; } = "";
    public long     CorporationId    { get; set; }
    public double   CorpTaxRate      { get; set; }  // 0-100
    public string   AccessToken      { get; set; } = "";
    public string   RefreshToken     { get; set; } = "";
    public DateTime TokenExpiry      { get; set; }
}

// ── App settings (stored on disk) ─────────────────────────────────────────────
public class AppSettings
{
    public string ClientId            { get; set; } = "";
    public string ClientSecret        { get; set; } = "";   // leave blank if using PKCE-only app
    public int    GapMinutes          { get; set; } = 60;
    public bool   IncludePITax       { get; set; } = false;
    public int    PollIntervalMinutes { get; set; } = 5;
}

// ── ESI API response types ────────────────────────────────────────────────────
public class EsiJournalEntry
{
    [JsonPropertyName("id")]              public long     Id             { get; set; }
    [JsonPropertyName("date")]            public DateTime Date           { get; set; }
    [JsonPropertyName("ref_type")]        public string   RefType        { get; set; } = "";
    [JsonPropertyName("amount")]          public double   Amount         { get; set; }
    [JsonPropertyName("balance")]         public double   Balance        { get; set; }
    [JsonPropertyName("description")]     public string   Description    { get; set; } = "";
    [JsonPropertyName("context_id")]      public long?    ContextId      { get; set; }
    [JsonPropertyName("context_id_type")] public string?  ContextIdType  { get; set; }
    [JsonPropertyName("first_party_id")]  public long?    FirstPartyId   { get; set; }
    [JsonPropertyName("second_party_id")] public long?    SecondPartyId  { get; set; }
    [JsonPropertyName("reason")]          public string?  Reason         { get; set; }
}

public class EsiSystemInfo
{
    [JsonPropertyName("name")]      public string Name    { get; set; } = "";
    [JsonPropertyName("system_id")] public long   SystemId { get; set; }
}

public class EsiTokenResponse
{
    [JsonPropertyName("access_token")]  public string AccessToken  { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_in")]    public int    ExpiresIn    { get; set; }
    [JsonPropertyName("token_type")]    public string TokenType    { get; set; } = "";
}
