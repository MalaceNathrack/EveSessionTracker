using System.Text;

namespace EveSessionTracker;

// ──────────────────────────────────────────────────────────────────────────────
// Produces formatted text reports matching the session breakdown style.
// ──────────────────────────────────────────────────────────────────────────────
public static class SessionFormatter
{
    private const int LineWidth  = 62;
    private const int LabelWidth = 28;
    private const int ValueWidth = 22;

    private static readonly string HeavyDiv = new('═', LineWidth);
    private static readonly string ThinDiv  = new('─', LineWidth - 4);

    // ── Single session ────────────────────────────────────────────────────────

    public static string FormatSession(RattingSession s, bool includePITax)
    {
        var sb = new StringBuilder();

        sb.AppendLine(HeavyDiv);
        sb.AppendLine($"  SESSION  {s.Start:yyyy.MM.dd HH:mm} — {s.End:HH:mm} UTC");
        sb.AppendLine($"  Duration {(int)s.Duration.TotalHours}h {s.Duration.Minutes:D2}m");

        if (s.Systems.Count > 0)
            sb.AppendLine($"  Systems  {string.Join("  ·  ", s.Systems.Order())}");

        if (s.IsPinned)
            sb.AppendLine("  [Manual split]");

        sb.AppendLine(HeavyDiv);
        sb.AppendLine();

        // Per-character blocks
        foreach (var c in s.CharStats)
        {
            sb.AppendLine($"  {c.CharacterName.ToUpper()}");
            sb.AppendLine(Row("Bounties", c.BountyGross, $"({c.BountyTicks} ticks)"));
            
            if (c.ESSIncome != 0)
                sb.AppendLine(Row("ESS Payout", c.ESSIncome, $"({c.ESSPayouts} payout{(c.ESSPayouts == 1 ? "" : "s")})"));
            
            sb.AppendLine(Row("Corp Tax", c.CorpTax));

            sb.AppendLine($"    {ThinDiv}");
            sb.AppendLine(Row("Net", c.Net));
            sb.AppendLine();
        }

        // Combined totals
        if (s.CharStats.Count > 1)
        {
            sb.AppendLine("  COMBINED TOTALS");
            sb.AppendLine(Row("Bounties", s.TotalBounty));
            
            if (s.TotalESS != 0)
                sb.AppendLine(Row("ESS Payout", s.TotalESS));
            
            sb.AppendLine(Row("Corp Tax", s.TotalCorpTax));

            sb.AppendLine($"    {ThinDiv}");
            sb.AppendLine(Row("Session Net", s.TotalNet));

            sb.AppendLine();
        }

        sb.AppendLine(HeavyDiv);
        return sb.ToString();
    }

    // ── Full multi-session report ─────────────────────────────────────────────

    public static string FormatAllSessions(List<RattingSession> sessions, bool includePITax)
    {
        var sb = new StringBuilder();

        sb.AppendLine("EVE Online — Ratting Session Report");
        sb.AppendLine($"Generated : {DateTime.UtcNow:yyyy.MM.dd HH:mm} UTC");
        sb.AppendLine($"Sessions  : {sessions.Count}");
        sb.AppendLine();

        foreach (var session in sessions)
            sb.Append(FormatSession(session, includePITax));

        // Grand total across all sessions
        if (sessions.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine(HeavyDiv);
            sb.AppendLine("  GRAND TOTAL — ALL SESSIONS");
            sb.AppendLine(HeavyDiv);
            sb.AppendLine(Row("Total Bounties", sessions.Sum(s => s.TotalBounty)));
            
            long grandESS = sessions.Sum(s => s.TotalESS);
            if (grandESS != 0)
                sb.AppendLine(Row("Total ESS Payout", grandESS));
            
            sb.AppendLine(Row("Total Corp Tax", sessions.Sum(s => s.TotalCorpTax)));

            sb.AppendLine($"    {ThinDiv}");
            sb.AppendLine(Row("Grand Net", sessions.Sum(s => s.TotalNet)));
            sb.AppendLine(HeavyDiv);
        }

        return sb.ToString();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string FormatISK(long amount)
        => amount >= 0 ? $"+{amount:N0} ISK" : $"{amount:N0} ISK";

    private static string Row(string label, long amount, string? note = null)
    {
        string val = FormatISK(amount);
        string labelFormatted = (label + ":").PadRight(LabelWidth);
        if (note != null)
            return $"    {labelFormatted} {val,ValueWidth}  {note}";
        return $"    {labelFormatted} {val,ValueWidth}";
    }
}
