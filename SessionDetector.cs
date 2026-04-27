namespace EveSessionTracker;

public static class SessionDetector
{
    private static readonly HashSet<TransactionType> ActiveTypes =
    [
        TransactionType.BountyPrize,
        TransactionType.BountyPrizeTax,
        TransactionType.ESSEscrow
    ];

    public static List<RattingSession> DetectSessions(
        List<Transaction> allTransactions,
        TimeSpan gapThreshold,
        Dictionary<string, double> charTaxRates,
        IEnumerable<DateTime>? manualSplits = null)
    {
        var active = allTransactions
            .Where(t => ActiveTypes.Contains(t.Type))
            .OrderBy(t => t.Date)
            .ToList();

        if (active.Count == 0) return [];

        var splits = new SortedSet<DateTime>(manualSplits ?? []);

        for (int i = 1; i < active.Count; i++)
            if (active[i].Date - active[i - 1].Date > gapThreshold)
                splits.Add(active[i].Date);

        var windows = new List<(DateTime Start, DateTime End)>();
        DateTime ws = active.First().Date;
        foreach (var split in splits) { windows.Add((ws, split)); ws = split; }
        windows.Add((ws, active.Last().Date.AddMinutes(1)));

        var sessions = new List<RattingSession>();

        foreach (var (wStart, wEnd) in windows)
        {
            var txs = allTransactions
                .Where(t => t.Date >= wStart && t.Date < wEnd)
                .OrderBy(t => t.Date)
                .ToList();

            if (txs.Count == 0) continue;

            var session = new RattingSession
            {
                Start        = txs.First().Date,
                End          = txs.Last().Date,
                Transactions = txs,
                Systems      = [.. txs.Where(t => t.System.Length > 0)
                                      .Select(t => t.System).Distinct()],
                IsPinned     = manualSplits?.Contains(wStart) ?? false
            };

            session.CharStats = BuildCharStats(txs, charTaxRates);
            sessions.Add(session);
        }

        return [.. sessions.OrderByDescending(s => s.Start)];
    }

    private static List<CharacterSessionStats> BuildCharStats(List<Transaction> txs, Dictionary<string, double> charTaxRates)
    {
        string lastChar = "";
        var resolved = new List<(Transaction Tx, string Name)>(txs.Count);

        foreach (var tx in txs.OrderBy(t => t.Date))
        {
            string name = tx.CharacterName;
            if (tx.Type == TransactionType.BountyPrize) lastChar = tx.CharacterName;
            else if (tx.Type == TransactionType.BountyPrizeTax && name.Length == 0)
                name = lastChar;
            resolved.Add((tx, name));
        }

        return resolved
            .Where(r => r.Name.Length > 0)
            .GroupBy(r => r.Name)
            .Select(g =>
            {
                var s = new CharacterSessionStats { CharacterName = g.Key };
                foreach (var (tx, _) in g)
                {
                    switch (tx.Type)
                    {
                        case TransactionType.BountyPrize:
                            s.BountyGross += tx.Amount; 
                            s.BountyTicks++; 
                            break;
                        case TransactionType.BountyPrizeTax:
                            s.CorpTax += tx.Amount; 
                            break;
                        case TransactionType.ESSEscrow:
                            s.ESSIncome += tx.Amount;
                            s.ESSPayouts++;
                            break;
                    }
                }

                // Calculate corp tax if we didn't get it from ESI
                if (s.CorpTax == 0 && s.BountyGross > 0 && charTaxRates.TryGetValue(g.Key, out var taxRate) && taxRate > 0)
                {
                    s.CorpTax = -(long)(s.BountyGross * taxRate / 100.0);
                }

                return s;
            })
            .OrderBy(s => s.CharacterName)
            .ToList();
    }
}
