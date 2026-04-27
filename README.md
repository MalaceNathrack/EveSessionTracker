# EVE Ratting Session Tracker

Reads EVE Online wallet journal exports and automatically groups transactions
into ratting sessions, producing the same breakdown format we've been using.

---

## Build & Run

Requires .NET 8 SDK (Windows).

```
dotnet build
dotnet run
```

Or open `EveSessionTracker.csproj` in Visual Studio / Rider and hit F5.

---

## Exporting Your Wallet in EVE

1. Open your wallet in-game
2. Click the **Journal** tab
3. Click the **export** icon (top-right corner of the journal window)
4. EVE saves a `.txt` file to:
   `C:\Users\<you>\Documents\EVE\logs\Wallet\`

The file is tab-separated: `Date | Type | Amount | Balance | Description`

Load one file per character, or multiple at once — the app merges them and
detects character names from the description text automatically.

---

## Features

| Feature | Detail |
|---|---|
| Auto session detection | Gaps > 60 min (configurable) = new session |
| Manual splits | Select a session → "✂ Split Here" to add a boundary |
| Merge sessions | Remove a manual split with "⬌ Merge with Next" |
| Per-character breakdown | Bounties, ESS, Corp Tax, Other Income |
| Escalation detection | Player donations ≥ 50M flagged as escalation sales |
| PI tax (optional) | Toggle in Settings — off by default |
| Color-coded detail | Green = income, Red = tax, Gold = bonus income |
| Save reports | All sessions or selected session → .txt |
| Dark theme | Matches EVE's aesthetic |

---

## Tracked Transaction Types

| Type | What it is |
|---|---|
| Bounty Prizes | Direct ratting income per tick |
| Bounty Prize Corporation Tax | Corp's cut (10% typically) |
| ESS Escrow Payment | Main bank + reserve payouts |
| Player Donation | Inter-player transfers (escalation sales flagged if ≥ 50M) |
| Planetary Export Tax | PI export fees (optional, off by default) |

Jump Bridge fees, Market Escrow, Daily Goal Rewards, and Insurance
are loaded but excluded from session income calculations.

---

## Session Gap Threshold

Default is **60 minutes**. Adjust in Settings (⚙).

- Higher value = fewer, longer sessions
- Lower value = more granular splits (e.g. 30 min catches lunch breaks)

A good workflow: load all your exports for a week, let auto-detection run,
then use "✂ Split Here" to manually correct any sessions that got merged
across a login/logout boundary.

---

## Output Format

```
══════════════════════════════════════════════════════════════
  SESSION  2026.04.26 19:38 — 02:16 UTC
  Duration 6h 38m
  Systems  00TY-J  ·  PPG-XC  ·  XG-D1L
══════════════════════════════════════════════════════════════

  CRISTA YARNEAR  [Escalation/Sale: 200,000,000 ISK]
    Bounties:                    +201,276,477 ISK  (18 ticks)
    ESS:                         +118,841,082 ISK  (6 payouts)
    Corp Tax:                     -20,127,648 ISK
    Other Income:                +200,000,000 ISK
    ──────────────────────────────────────────────────────
    Net:                         +500,089,911 ISK

  ...

  COMBINED TOTALS
    Bounties:                    +802,848,521 ISK
    ESS:                         +474,028,477 ISK
    Corp Tax:                     -80,285,253 ISK
    Other Income:                +200,000,000 ISK
    ──────────────────────────────────────────────────────
    Session Net:               +1,396,591,745 ISK
    Efficiency:                           93.6%
══════════════════════════════════════════════════════════════
```
