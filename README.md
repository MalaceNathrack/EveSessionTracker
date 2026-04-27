# EVE Ratting Session Tracker

Automatically tracks your EVE Online ratting income via ESI (EVE Swagger Interface).
Authenticates with EVE SSO, polls your wallet journal, and groups transactions into
sessions with detailed breakdowns.

---

## Features

### 🔐 ESI Integration
- **EVE SSO authentication** — secure OAuth login for each character
- **Automatic wallet sync** — polls every 5 minutes (configurable)
- **Multi-character support** — track multiple toons simultaneously
- **Token management** — automatic refresh, secure local storage

### 📊 Session Analysis
- **Auto session detection** — gaps > 60 min = new session (configurable)
- **Manual split/merge** — refine session boundaries with ✂ Split and ⬌ Merge
- **Per-character breakdown** — bounties, ESS, corp tax, system tracking
- **Live updates** — see income as it happens, no exports needed

### 💰 Income Tracking
- **Bounty prizes** with tick counting
- **ESS Main Bank & Reserve** payouts
- **Corporation tax** (configurable per character)
- **Escalation/sale detection** (player donations ≥ 50M ISK)
- **Optional PI tax** tracking (off by default)

### 🎨 User Experience
- **Dark EVE theme** matching in-game aesthetics
- **Color-coded transactions** — Green: income, Red: tax, Gold: bonus
- **Export reports** — save all sessions or individual sessions to .txt
- **Real-time status** — see next poll time, transaction counts

---

## Setup & Build

### Requirements
- Windows 10/11
- .NET 8 SDK

### Build
```bash
dotnet build
dotnet run
```

Or open `EveSessionTracker.csproj` in Visual Studio / Rider and press F5.

---

## First-Time Setup

### 1. Create EVE Developer Application

1. Go to https://developers.eveonline.com
2. Click **"Create New Application"**
3. Fill in:
   - **Application Type:** Authentication & API Access
   - **Scopes:** `esi-wallet.read_character_wallet.v1`
   - **Callback URL:** `http://localhost:8080/callback`
4. Save and copy the **Client ID** from the app detail page

### 2. Configure the Tracker

1. Launch EVE Session Tracker
2. Go to the **CHARACTERS** tab
3. Click **⚙ Settings**
4. Paste your **Client ID**
5. (Optional) Leave **Client Secret** blank for PKCE-only authentication
6. Adjust polling interval if desired (default: 5 minutes)
7. Click **OK**

### 3. Add Characters

1. Click **+ Add Character**
2. Your browser opens with EVE SSO login
3. Log in with the character you want to track
4. Authorize the application
5. Return to the tracker — character is added automatically
6. Repeat for additional characters

### 4. Set Corporation Tax Rates

1. Select a character in the list
2. Click **Edit Tax Rate**
3. Enter your corp's tax rate (e.g., 10.0 for 10%)
4. Click **OK**

**Note:** Some null/low-sec corps don't generate ESI tax entries. The tracker
will calculate tax from bounties if no tax transactions are found.

---

## Usage

### Viewing Sessions

1. Switch to the **SESSIONS** tab
2. Sessions auto-populate as wallet data refreshes
3. Click a session to see detailed breakdown

### Manual Session Control

- **✂ Split Here** — add a manual split point within the selected session
- **⬌ Merge Next** — remove the split at the start of the selected session

Pinned sessions (with manual splits) are marked with `*` in the list.

### Exporting Reports

1. Click **💾 Save Report**
2. Choose:
   - **Yes** = Save all sessions
   - **No** = Save selected session only
3. Pick a location and filename
4. Report saved as formatted .txt

---

## Session Gap Threshold

Default: **60 minutes**. Adjust in Settings (⚙).

- **Higher** = fewer, longer sessions (good for long ratting blocks)
- **Lower** = more granular splits (e.g., 30 min catches breaks)

---

## Output Format Example

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

  COMBINED TOTALS
    Bounties:                    +802,848,521 ISK
    ESS:                         +475,364,328 ISK
    Corp Tax:                     -80,284,852 ISK
    Net:                       +1,197,927,997 ISK
```

---

## Tracked Transaction Types

| ESI Ref Type | Display As | Notes |
|---|---|---|
| `bounty_prizes` | Bounty Prizes | Direct ratting income per tick |
| `bounty_prize_corporation_tax` | Corp Tax | Your corp's cut (e.g., 10%) |
| `ess_escrow_transfer` | ESS Escrow Payment | Main bank + reserve payouts |
| `player_donation` | Player Donation | Escalation sales flagged if ≥ 50M ISK |
| `planetary_export_tax` | PI Export Tax | Optional (off by default) |

Other transaction types (jump bridges, market escrow, insurance, contracts, etc.)
are ignored by the session tracker.

---

## Data Storage

All settings and character tokens are stored locally in:
```
%APPDATA%\EveSessionTracker\
```

Files:
- `settings.json` — Client ID, gap threshold, PI tax toggle, poll interval
- `characters.json` — Character tokens, refresh tokens, tax rates

**Security Note:** Tokens are stored in plain JSON. Keep your PC secure.
If compromised, revoke the application at https://community.eveonline.com/support/third-party-applications/

---

## Troubleshooting

### "Could not start local listener on port 8080"
Another application is using port 8080. Close it or change the port in both
the EVE developer app callback URL and `EsiClient.cs`.

### "No corp tax entries found"
Some null/low-sec corps don't generate ESI tax transactions. The tracker
will calculate tax from bounties using your configured rate.

### "Token expired" or "Unauthorized"
The app automatically refreshes tokens. If this fails repeatedly, remove
the character and re-add it.

### Character not showing transactions
- Verify the character is in the CHARACTERS list
- Check that ✓ Refresh completed successfully
- View the CONSOLE tab (run with `--admin` flag) to see raw ESI responses

---

## Console / Debug Mode

Run with the `--admin` flag to enable a console tab:
```bash
dotnet run -- --admin
```

Shows:
- Raw ESI API responses
- Transaction type counts
- Corp tax calculation debug info
- Token refresh events

---

## License & Credits

Built for EVE Online using CCP's ESI API.
EVE Online and all related materials are © CCP hf.

Open source. Do whatever you want with it. o7
    ESS:                         +474,028,477 ISK
    Corp Tax:                     -80,285,253 ISK
    Other Income:                +200,000,000 ISK
    ──────────────────────────────────────────────────────
    Session Net:               +1,396,591,745 ISK
    Efficiency:                           93.6%
══════════════════════════════════════════════════════════════
```
