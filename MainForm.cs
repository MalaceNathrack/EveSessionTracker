using System.Drawing;
using System.Windows.Forms;

namespace EveSessionTracker;

public class MainForm : Form
{
    // ── App state ─────────────────────────────────────────────────────────────
    private AppSettings              _settings    = new();
    private List<CharacterToken>     _characters  = [];
    private List<Transaction>        _transactions = [];
    private List<RattingSession>     _sessions    = [];
    private readonly SortedSet<DateTime> _manualSplits = [];
    private readonly EsiClient       _esi         = new();
    private System.Windows.Forms.Timer? _pollTimer;
    private bool _polling = false;

    // ── Controls ──────────────────────────────────────────────────────────────
    private TabControl   _tabs          = null!;
    private TabPage      _tabChars      = null!;
    private TabPage      _tabSessions   = null!;
    private TabPage?     _tabConsole;

    // Characters tab
    private ListView     _charList      = null!;
    private Button       _btnAddChar    = null!;
    private Button       _btnRemoveChar = null!;
    private Button       _btnEditTax    = null!;
    private Button       _btnRefreshNow = null!;
    private Label        _lblNextPoll   = null!;
    private ProgressBar  _pbRefresh     = null!;

    // Sessions tab
    private ToolStrip    _toolbar       = null!;
    private ListBox      _sessionList   = null!;
    private Panel        _detailScroll  = null!;
    private SplitContainer _sessionSplitter = null!;

    // Console tab
    private TextBox?     _consoleOutput;

    // Status
    private StatusStrip          _statusBar  = null!;
    private ToolStripStatusLabel _statusLbl  = null!;
    private ToolStripStatusLabel _statusRight = null!;

    // ── EVE dark palette ──────────────────────────────────────────────────────
    private static readonly Color C_Bg       = Color.FromArgb(22,  25,  30);
    private static readonly Color C_Panel    = Color.FromArgb(30,  35,  42);
    private static readonly Color C_List     = Color.FromArgb(26,  30,  36);
    private static readonly Color C_Fg       = Color.FromArgb(200, 205, 210);
    private static readonly Color C_Blue     = Color.FromArgb(100, 185, 255);
    private static readonly Color C_Green    = Color.FromArgb(100, 215, 100);
    private static readonly Color C_Red      = Color.FromArgb(220, 100, 100);
    private static readonly Color C_Gold     = Color.FromArgb(210, 170,  55);
    private static readonly Color C_Muted    = Color.FromArgb(130, 140, 155);
    private static readonly Color C_Orange   = Color.FromArgb(220, 150,  60);

    private readonly bool _adminMode;

    // ── Constructor ───────────────────────────────────────────────────────────
    public MainForm(bool adminMode = false)
    {
        _adminMode = adminMode;
        
        Text        = "EVE Ratting Session Tracker";
        Size        = new Size(1200, 820);
        MinimumSize = new Size(860, 560);
        BackColor   = C_Bg;
        ForeColor   = C_Fg;
        Font        = new Font("Segoe UI", 9f);

        BuildUI();
        Load += OnLoad;
    }

    private void OnLoad(object? sender, EventArgs e)
    {
        _settings   = TokenStore.LoadSettings();
        _characters = TokenStore.LoadCharacters();
        
        RefreshCharacterList();

        // Force session list width
        if (_sessionSplitter != null)
            _sessionSplitter.SplitterDistance = 450;

        if (_settings.ClientId.Length == 0)
        {
            SetStatus("No Client ID configured. Go to the Characters tab and click Settings.", "");
            return;
        }

        if (_characters.Count > 0)
            BeginInvoke(async () => await PollAllCharactersAsync());

        StartPollTimer();
    }

    // ── UI Construction ───────────────────────────────────────────────────────
    private void BuildUI()
    {
        // Status bar
        _statusBar   = new StatusStrip { BackColor = C_Panel };
        _statusLbl   = new ToolStripStatusLabel("Loading...")
            { ForeColor = C_Muted, Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusRight = new ToolStripStatusLabel("")
            { ForeColor = C_Muted };
        _statusBar.Items.Add(_statusLbl);
        _statusBar.Items.Add(_statusRight);

        // Tab control
        _tabs = new TabControl
        {
            Dock      = DockStyle.Fill,
            DrawMode  = TabDrawMode.OwnerDrawFixed,
            ItemSize  = new Size(130, 28),
            SizeMode  = TabSizeMode.Fixed,
            Font      = new Font("Segoe UI Semibold", 9f),
            BackColor = C_Bg
        };
        _tabs.DrawItem += DrawTabItem;

        _tabChars    = new TabPage("  CHARACTERS")   { BackColor = C_Bg, ForeColor = C_Fg };
        _tabSessions = new TabPage("  SESSIONS")     { BackColor = C_Bg, ForeColor = C_Fg };

        BuildCharactersTab();
        BuildSessionsTab();
        
        if (_adminMode)
        {
            _tabConsole = new TabPage("  CONSOLE") { BackColor = C_Bg, ForeColor = C_Fg };
            BuildConsoleTab();
        }

        _tabs.TabPages.Add(_tabChars);
        _tabs.TabPages.Add(_tabSessions);
        
        if (_adminMode && _tabConsole != null)
            _tabs.TabPages.Add(_tabConsole);

        Controls.Add(_tabs);
        Controls.Add(_statusBar);

        // Redirect Console output to console tab (only in admin mode)
        if (_adminMode && _consoleOutput != null)
        {
            Console.SetOut(new ConsoleWriter(_consoleOutput, this));
            Console.WriteLine("=== EVE Session Tracker Console ===");
            Console.WriteLine($"Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Console.WriteLine();
        }
    }

    // ── Characters Tab ────────────────────────────────────────────────────────
    private void BuildCharactersTab()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = C_Bg, Padding = new Padding(12) };

        // Header
        var hdr = new Label
        {
            Text      = "Authorized Characters",
            Font      = new Font("Segoe UI Semibold", 11f),
            ForeColor = C_Blue,
            AutoSize  = true,
            Top = 12, Left = 12
        };

        var subHdr = new Label
        {
            Text      = "Each character requires a separate EVE SSO login. Wallet journal is fetched automatically.",
            ForeColor = C_Muted,
            AutoSize  = true,
            Top = 38, Left = 12
        };

        // Character ListView
        _charList = new ListView
        {
            View           = View.Details,
            FullRowSelect  = true,
            GridLines      = false,
            HeaderStyle    = ColumnHeaderStyle.Nonclickable,
            BackColor      = C_List,
            ForeColor      = C_Fg,
            BorderStyle    = BorderStyle.None,
            Font           = new Font("Consolas", 9.5f),
            Top = 68, Left = 12, Width = 760, Height = 320
        };
        _charList.Columns.Add("Character",     180);
        _charList.Columns.Add("Character ID",  120);
        _charList.Columns.Add("Tax Rate",      80);
        _charList.Columns.Add("Token Expires", 160);
        _charList.Columns.Add("Status",        200);

        // Buttons
        _btnAddChar    = DarkButton("+ Add Character",    OnAddCharacter,    12,  408);
        _btnRemoveChar = DarkButton("- Remove Selected",  OnRemoveCharacter, 145, 408);
        _btnEditTax    = DarkButton("Edit Tax Rate",     OnEditTaxRate,     290, 408);
        _btnRefreshNow = DarkButton("↻ Refresh Now",      async (s,e) => await PollAllCharactersAsync(), 420, 408);
        var btnSettings = DarkButton("⚙ Settings",        OnSettings,        550, 408);

        _pbRefresh = new ProgressBar
        {
            Style   = ProgressBarStyle.Marquee,
            Visible = false,
            Left = 12, Top = 446, Width = 760, Height = 8,
            BackColor = C_Bg, ForeColor = C_Blue
        };

        _lblNextPoll = new Label
        {
            Text      = "",
            ForeColor = C_Muted,
            AutoSize  = true,
            Left = 12, Top = 462
        };

        // Info box
        var infoBox = new RichTextBox
        {
            BackColor   = Color.FromArgb(18, 22, 28),
            ForeColor   = C_Muted,
            BorderStyle = BorderStyle.None,
            ReadOnly    = true,
            Font        = new Font("Segoe UI", 8.5f),
            Left = 12, Top = 490, Width = 760, Height = 120,
            ScrollBars  = RichTextBoxScrollBars.None
        };
        infoBox.Text =
            "SETUP INSTRUCTIONS\r\n" +
            "1. Go to https://developers.eveonline.com and create a new application.\r\n" +
            "2. Application type: Authentication & API Access\r\n" +
            "3. Scopes: esi-wallet.read_character_wallet.v1\r\n" +
            "4. Callback URL: http://localhost:8080/callback\r\n" +
            "5. Copy the Client ID from the app detail page into Settings (⚙) below.\r\n" +
            "6. Click + Add Character and log in for each toon you want to track.";

        panel.Controls.AddRange([hdr, subHdr, _charList, _btnAddChar, _btnRemoveChar,
                                  _btnEditTax, _btnRefreshNow, btnSettings, _pbRefresh, _lblNextPoll, infoBox]);
        _tabChars.Controls.Add(panel);
    }

    // ── Sessions Tab ──────────────────────────────────────────────────────────
    private void BuildSessionsTab()
    {
        // Toolbar
        _toolbar = new ToolStrip
        {
            BackColor = C_Panel,
            ForeColor = C_Fg,
            Renderer  = new ToolStripProfessionalRenderer(new DarkColorTable()),
            Dock      = DockStyle.Top
        };

        var btnSave  = TBtn("💾 Save Report",  OnSave);
        var btnSplit = TBtn("✂ Split Here",    OnSplit);
        var btnMerge = TBtn("⬌ Merge Next",   OnMerge);

        _toolbar.Items.Add(btnSave);
        _toolbar.Items.Add(new ToolStripSeparator());
        _toolbar.Items.Add(btnSplit);
        _toolbar.Items.Add(btnMerge);

        // Left: session list
        var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = C_Panel };
        var listHdr = new Label
        {
            Text      = "  SESSIONS",
            Dock      = DockStyle.Top,
            Height    = 24,
            Font      = new Font("Segoe UI Semibold", 8.5f),
            ForeColor = C_Blue,
            BackColor = C_Bg
        };

        _sessionList = new ListBox
        {
            Dock           = DockStyle.Fill,
            Font           = new Font("Consolas", 9f),
            BackColor      = C_List,
            ForeColor      = C_Fg,
            BorderStyle    = BorderStyle.None,
            IntegralHeight = false
        };
        _sessionList.SelectedIndexChanged += OnSessionSelected;

        leftPanel.Controls.Add(_sessionList);
        leftPanel.Controls.Add(listHdr);

        // Right: scrollable detail panel
        _detailScroll = new Panel
        {
            Dock          = DockStyle.Fill,
            BackColor     = C_Bg,
            AutoScroll    = true,
            BorderStyle   = BorderStyle.None
        };

        var splitter = new SplitContainer
        {
            Dock             = DockStyle.Fill,
            SplitterDistance = 450,
            Panel1MinSize    = 450,
            FixedPanel       = FixedPanel.Panel1,
            BackColor        = C_Bg,
            BorderStyle      = BorderStyle.None
        };
        splitter.Panel1.Controls.Add(leftPanel);
        splitter.Panel2.Controls.Add(_detailScroll);

        _sessionSplitter = splitter;

        var container = new Panel { Dock = DockStyle.Fill };
        container.Controls.Add(splitter);
        container.Controls.Add(_toolbar);

        _tabSessions.Controls.Add(container);
    }

    // ── Console Tab ───────────────────────────────────────────────────────────
    private void BuildConsoleTab()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = C_Bg, Padding = new Padding(12) };

        var hdr = new Label
        {
            Text      = "Console Output",
            Font      = new Font("Segoe UI Semibold", 11f),
            ForeColor = C_Blue,
            AutoSize  = true,
            Top = 12, Left = 12
        };

        var subHdr = new Label
        {
            Text      = "Debug output and ESI transaction logs",
            ForeColor = C_Muted,
            AutoSize  = true,
            Top = 38, Left = 12
        };

        _consoleOutput = new TextBox
        {
            Multiline    = true,
            ReadOnly     = true,
            ScrollBars   = ScrollBars.Vertical,
            BackColor    = C_List,
            ForeColor    = C_Fg,
            BorderStyle  = BorderStyle.None,
            Font         = new Font("Consolas", 9f),
            WordWrap     = false,
            Top = 68, Left = 12, Width = 760, Height = 520
        };

        var btnClear = DarkButton("Clear Log", (s, e) => _consoleOutput.Clear(), 12, 598);

        panel.Controls.Add(hdr);
        panel.Controls.Add(subHdr);
        panel.Controls.Add(_consoleOutput);
        panel.Controls.Add(btnClear);

        _tabConsole.Controls.Add(panel);
    }

    // ── Character management ──────────────────────────────────────────────────
    private async void OnAddCharacter(object? sender, EventArgs e)
    {
        if (_settings.ClientId.Length == 0)
        {
            MessageBox.Show(
                "Please enter your EVE Developer Application Client ID in Settings first.",
                "Client ID Required", MessageBoxButtons.OK, MessageBoxIcon.Information);
            OnSettings(null, EventArgs.Empty);
            return;
        }

        _btnAddChar.Enabled    = false;
        _btnRemoveChar.Enabled = false;
        _pbRefresh.Visible     = true;

        var prog = new Progress<string>(msg => SetStatus(msg, ""));

        try
        {
            var token = await _esi.AuthorizeAsync(_settings.ClientId, _settings.ClientSecret, prog);

            // Check duplicate
            if (_characters.Any(c => c.CharacterId == token.CharacterId))
            {
                SetStatus($"{token.CharacterName} is already added.", "");
                return;
            }

            // Prompt user to set tax rate
            using var dlg = new TaxRateDialog(token.CharacterName, token.CorpTaxRate);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                token.CorpTaxRate = dlg.TaxRatePercent;
            }

            _characters.Add(token);
            TokenStore.UpsertCharacter(token);
            RefreshCharacterList();

            SetStatus($"Added {token.CharacterName}. Fetching wallet journal...", "");

            await FetchCharacterJournalAsync(token);
            SetStatus($"{token.CharacterName} added and synced.", $"{_sessions.Count} sessions");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Authentication failed:\n\n{ex.Message}",
                "Auth Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetStatus("Authentication failed.", "");
        }
        finally
        {
            _btnAddChar.Enabled    = true;
            _btnRemoveChar.Enabled = true;
            _pbRefresh.Visible     = false;
        }
    }

    private void OnRemoveCharacter(object? sender, EventArgs e)
    {
        if (_charList.SelectedItems.Count == 0) return;

        var item    = _charList.SelectedItems[0];
        var charId  = (long)item.Tag!;
        var name    = item.Text;

        if (MessageBox.Show($"Remove {name}?\n\nThis removes their wallet data from this session.",
                "Confirm Remove", MessageBoxButtons.YesNo) != DialogResult.Yes) return;

        _characters.RemoveAll(c => c.CharacterId == charId);
        _transactions.RemoveAll(t => t.CharacterName == name);
        TokenStore.RemoveCharacter(charId);

        RefreshCharacterList();
        RebuildSessions();
        SetStatus($"Removed {name}.", $"{_sessions.Count} sessions");
    }

    private void OnEditTaxRate(object? sender, EventArgs e)
    {
        if (_charList.SelectedItems.Count == 0)
        {
            MessageBox.Show("Please select a character first.", "No Selection", 
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var item = _charList.SelectedItems[0];
        var charId = (long)item.Tag!;
        var token = _characters.FirstOrDefault(c => c.CharacterId == charId);
        
        if (token == null) return;

        using var dlg = new TaxRateDialog(token.CharacterName, token.CorpTaxRate);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            token.CorpTaxRate = dlg.TaxRatePercent;
            TokenStore.UpsertCharacter(token);
            RefreshCharacterList();
            RebuildSessions(); // Recalculate with new tax rate
            SetStatus($"Updated tax rate for {token.CharacterName} to {token.CorpTaxRate:F1}%.", 
                $"{_sessions.Count} sessions");
        }
    }

    private void RefreshCharacterList()
    {
        _charList.Items.Clear();

        foreach (var c in _characters)
        {
            var expired = DateTime.UtcNow > c.TokenExpiry;
            var item    = new ListViewItem(c.CharacterName)
            {
                Tag       = c.CharacterId,
                ForeColor = expired ? C_Orange : C_Green
            };
            item.SubItems.Add(c.CharacterId.ToString());
            item.SubItems.Add($"{c.CorpTaxRate:F1}%");
            item.SubItems.Add(c.TokenExpiry.ToLocalTime().ToString("MM/dd HH:mm") + " local");
            item.SubItems.Add(expired ? "Token expired — will refresh on next poll" : "Active");

            _charList.Items.Add(item);
        }
    }

    // ── ESI polling ───────────────────────────────────────────────────────────
    private void StartPollTimer()
    {
        _pollTimer?.Dispose();
        int intervalMs = Math.Max(1, _settings.PollIntervalMinutes) * 60 * 1000;

        _pollTimer = new System.Windows.Forms.Timer { Interval = intervalMs };
        _pollTimer.Tick += async (_, _) =>
        {
            if (!_polling)
                await PollAllCharactersAsync();
        };
        _pollTimer.Start();
        UpdateNextPollLabel();
    }

    private void UpdateNextPollLabel()
    {
        if (_pollTimer is null) return;
        _lblNextPoll.Text = $"Auto-refresh every {_settings.PollIntervalMinutes} min.";
    }

    private async Task PollAllCharactersAsync()
    {
        if (_characters.Count == 0) return;
        if (_polling) return;
        _polling = true;

        _pbRefresh.Visible     = true;
        _btnRefreshNow.Enabled = false;

        SetStatus($"Refreshing {_characters.Count} character(s)...", "");

        try
        {
            var tasks = _characters.Select(c => FetchCharacterJournalAsync(c)).ToList();
            await Task.WhenAll(tasks);
        }
        finally
        {
            _polling               = false;
            _pbRefresh.Visible     = false;
            _btnRefreshNow.Enabled = true;
            RefreshCharacterList();
            SetStatus("Wallet journals up to date.",
                $"{_characters.Count} character(s)  ·  {_sessions.Count} session(s)");
        }
    }

    private async Task FetchCharacterJournalAsync(CharacterToken token)
    {
        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] Fetching wallet for {token.CharacterName}...");
        
        try
        {
            var newTxs = await _esi.GetWalletJournalAsync(
                token,
                _settings.ClientId, _settings.ClientSecret,
                maxPages: 3,
                onTokenRefreshed: refreshed =>
                {
                    // Update stored token on refresh
                    var idx = _characters.FindIndex(c => c.CharacterId == refreshed.CharacterId);
                    if (idx >= 0) _characters[idx] = refreshed;
                    TokenStore.UpsertCharacter(refreshed);
                });

            // Debug: Count transaction types
            var typeCounts = newTxs.GroupBy(t => t.Type).ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine($"Received {newTxs.Count} total transactions:");
            foreach (var kvp in typeCounts)
                Console.WriteLine($"  {kvp.Key}: {kvp.Value}");
            
            if (typeCounts.ContainsKey(TransactionType.BountyPrizeTax))
                Console.WriteLine($"✓ Corp tax entries found from ESI");
            else if (typeCounts.ContainsKey(TransactionType.BountyPrize))
                Console.WriteLine($"⚠ No corp tax entries - will calculate from rate ({token.CorpTaxRate:F1}%)");
            Console.WriteLine();

            // Merge — remove old transactions for this character then add fresh
            _transactions.RemoveAll(t => t.CharacterName == token.CharacterName);
            _transactions.AddRange(newTxs);

            RebuildSessions();
        }
        catch (Exception ex)
        {
            SetStatus($"Error fetching {token.CharacterName}: {ex.Message}", "");
        }
    }

    private void RebuildSessions()
    {
        var charTaxRates = _characters.ToDictionary(c => c.CharacterName, c => c.CorpTaxRate);

        _sessions = SessionDetector.DetectSessions(
            _transactions,
            TimeSpan.FromMinutes(_settings.GapMinutes),
            charTaxRates,
            _manualSplits);

        // Update session list on UI thread
        Action update = () =>
        {
            int prev = _sessionList.SelectedIndex;

            _sessionList.BeginUpdate();
            _sessionList.Items.Clear();
            foreach (var s in _sessions)
                _sessionList.Items.Add(s.ListLabel);
            _sessionList.EndUpdate();

            int newIdx = prev >= 0 && prev < _sessionList.Items.Count ? prev
                : _sessionList.Items.Count > 0 ? 0 : -1;
            if (newIdx >= 0) _sessionList.SelectedIndex = newIdx;

            _statusRight.Text = $"{_sessions.Count} session(s)";
        };

        if (InvokeRequired) Invoke(update);
        else update();
    }

    // ── Session list ──────────────────────────────────────────────────────────
    private void OnSessionSelected(object? sender, EventArgs e)
    {
        var session = SelectedSession();
        if (session is null) return;
        RenderSessionDetail(session);
    }

    private void OnSplit(object? sender, EventArgs e)
    {
        var session = SelectedSession();
        if (session is null) return;

        using var dlg = new SplitDialog(session.Start, session.End);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        _manualSplits.Add(dlg.SplitPoint);
        RebuildSessions();
        SetStatus($"Split added at {dlg.SplitPoint:yyyy.MM.dd HH:mm} UTC.", "");
    }

    private void OnMerge(object? sender, EventArgs e)
    {
        var session = SelectedSession();
        if (session is null) return;
        _manualSplits.Remove(session.Start);
        RebuildSessions();
        SetStatus("Split removed.", "");
    }

    private void OnSave(object? sender, EventArgs e)
    {
        if (_sessions.Count == 0) return;

        var choice = MessageBox.Show(
            "Yes = Save all sessions\nNo = Save selected session only",
            "Save Report", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return;

        using var dlg = new SaveFileDialog
        {
            Filter   = "Text file (*.txt)|*.txt",
            FileName = $"EVE_Sessions_{DateTime.UtcNow:yyyyMMdd_HHmm}.txt"
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        try
        {
            string report = choice == DialogResult.Yes
                ? SessionFormatter.FormatAllSessions([.. _sessions], _settings.IncludePITax)
                : SessionFormatter.FormatSession(SelectedSession()!, _settings.IncludePITax);
            File.WriteAllText(dlg.FileName, report);
            SetStatus($"Saved \u2192 {dlg.FileName}", "");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Save failed:\n{ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Settings ──────────────────────────────────────────────────────────────
    private void OnSettings(object? sender, EventArgs e)
    {
        using var dlg = new SettingsForm(_settings);
        if (dlg.ShowDialog() != DialogResult.OK) return;

        _settings = dlg.Result;
        TokenStore.SaveSettings(_settings);
        StartPollTimer();
        SetStatus("Settings saved.", $"Data stored in: {TokenStore.DataDirectory}");
    }

    // ── Detail rendering — panel-based, no monospace dependency ─────────────
    private void RenderSessionDetail(RattingSession session)
    {
        _detailScroll.SuspendLayout();
        _detailScroll.Controls.Clear();

        var inner = new Panel
        {
            Left      = 0,
            Top       = 0,
            Width     = Math.Max(_detailScroll.ClientSize.Width - 5, 600),
            BackColor = C_Bg,
            Padding   = new Padding(20, 16, 20, 20)
        };

        int y = 16;

        // ── Session header ────────────────────────────────────────────────────
        var hdrTime = new Label
        {
            Text      = $"SESSION  {session.Start:yyyy.MM.dd HH:mm} — {session.End:HH:mm} UTC",
            Left      = 20,
            Top       = y,
            AutoSize  = true,
            ForeColor = C_Blue,
            Font      = new Font("Segoe UI", 10.5f, FontStyle.Bold)
        };
        inner.Controls.Add(hdrTime);
        y += 26;

        var hdrDuration = new Label
        {
            Text      = $"Duration {(int)session.Duration.TotalHours}h {session.Duration.Minutes:D2}m",
            Left      = 20,
            Top       = y,
            AutoSize  = true,
            ForeColor = C_Muted,
            Font      = new Font("Segoe UI", 9f)
        };
        inner.Controls.Add(hdrDuration);
        y += 22;

        if (session.Systems.Count > 0)
        {
            var hdrSystems = new Label
            {
                Text      = "Systems  " + string.Join("  ·  ", session.Systems.Order()),
                Left      = 20,
                Top       = y,
                AutoSize  = true,
                ForeColor = C_Muted,
                Font      = new Font("Segoe UI", 9f)
            };
            inner.Controls.Add(hdrSystems);
            y += 22;
        }

        y += 8;

        // ── Character cards in grid layout ─────────────────────────────────────
        int cardsPerRow = 2;
        int cardWidth = (inner.Width - 40 - 16) / cardsPerRow;  // 16px gap between cards
        int cardX = 20;
        int cardY = y;
        int maxCardHeight = 0;
        int cardIndex = 0;

        foreach (var c in session.CharStats)
        {
            var card = CreateCharacterCard(c, cardWidth, _settings.IncludePITax);
            card.Left = cardX;
            card.Top  = cardY;
            inner.Controls.Add(card);

            maxCardHeight = Math.Max(maxCardHeight, card.Height);
            cardIndex++;

            if (cardIndex % cardsPerRow == 0)
            {
                // Move to next row
                cardX = 20;
                cardY += maxCardHeight + 16;
                maxCardHeight = 0;
            }
            else
            {
                // Move to next column
                cardX += cardWidth + 16;
            }
        }

        // Update y position based on where we ended
        if (cardIndex % cardsPerRow != 0)
            y = cardY + maxCardHeight + 16;
        else
            y = cardY;

        y += 8;

        // ── Combined totals card ───────────────────────────────────────────────
        if (session.CharStats.Count > 1)
        {
            y += 4;
            int fullWidth = inner.Width - 40;
            var totalsCard = CreateCombinedTotalsCard(session, fullWidth, _settings.IncludePITax);
            totalsCard.Left = 20;
            totalsCard.Top  = y;
            inner.Controls.Add(totalsCard);
            y += totalsCard.Height + 16;
        }

        inner.Height = y + 20;
        _detailScroll.Controls.Add(inner);
        _detailScroll.ResumeLayout();
    }

    private Panel CreateCharacterCard(CharacterSessionStats c, int width, bool includePITax)
    {
        var card = new Panel
        {
            Width     = width,
            BackColor = C_Panel,
            Padding   = new Padding(18, 14, 18, 14)
        };

        int y = 14;

        // Character name
        var lblName = new Label
        {
            Text      = c.CharacterName,
            Left      = 18,
            Top       = y,
            AutoSize  = true,
            ForeColor = C_Fg,
            Font      = new Font("Segoe UI", 11f, FontStyle.Bold)
        };
        card.Controls.Add(lblName);
        y += 28;

        // Stats
        y = AddCardRow(card, "Bounties (" + c.BountyTicks + " ticks)", c.BountyGross, C_Green, y);
        
        if (c.ESSIncome != 0)
            y = AddCardRow(card, "ESS (" + c.ESSPayouts + " payout" + (c.ESSPayouts == 1 ? "" : "s") + ")", c.ESSIncome, C_Green, y);
        
        y = AddCardRow(card, "Corp tax", c.CorpTax, C_Red, y);

        // Net
        y += 2;
        var lblNet = new Label
        {
            Text      = "Net",
            Left      = 18,
            Top       = y,
            AutoSize  = true,
            ForeColor = C_Fg,
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        card.Controls.Add(lblNet);

        var lblNetVal = new Label
        {
            Text      = SessionFormatter.FormatISK(c.Net),
            Left      = 18,
            Top       = y,
            Width     = card.Width - 36,
            TextAlign = ContentAlignment.TopRight,
            ForeColor = c.Net >= 0 ? C_Green : C_Red,
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        card.Controls.Add(lblNetVal);
        y += 28;

        card.Height = y;
        return card;
    }

    private Panel CreateCombinedTotalsCard(RattingSession session, int width, bool includePITax)
    {
        var card = new Panel
        {
            Width     = width,
            BackColor = Color.FromArgb(25, 30, 36),
            Padding   = new Padding(18, 14, 18, 14)
        };

        int y = 14;

        // Title
        var lblTitle = new Label
        {
            Text      = "COMBINED TOTALS",
            Left      = 18,
            Top       = y,
            AutoSize  = true,
            ForeColor = C_Blue,
            Font      = new Font("Segoe UI", 11f, FontStyle.Bold)
        };
        card.Controls.Add(lblTitle);
        y += 32;

        // Stats
        y = AddCardRow(card, "Bounties", session.TotalBounty, C_Green, y);
        
        if (session.TotalESS != 0)
            y = AddCardRow(card, "ESS Payout", session.TotalESS, C_Green, y);
        
        y = AddCardRow(card, "Corp tax", session.TotalCorpTax, C_Red, y);

        // Net total
        y += 6;
        var lblNet = new Label
        {
            Text      = "Session Net",
            Left      = 18,
            Top       = y,
            AutoSize  = true,
            ForeColor = C_Fg,
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        card.Controls.Add(lblNet);

        var lblNetVal = new Label
        {
            Text      = SessionFormatter.FormatISK(session.TotalNet),
            Left      = 18,
            Top       = y,
            Width     = width - 36,
            TextAlign = ContentAlignment.TopRight,
            ForeColor = session.TotalNet >= 0 ? C_Green : C_Red,
            Font      = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        card.Controls.Add(lblNetVal);
        y += 28;

        card.Height = y;
        return card;
    }

    private int AddCardRow(Panel card, string label, long amount, Color valueColor, int y)
    {
        var lblLabel = new Label
        {
            Text      = label,
            Left      = 18,
            Top       = y,
            AutoSize  = true,
            ForeColor = C_Muted,
            Font      = new Font("Segoe UI", 9f)
        };
        card.Controls.Add(lblLabel);

        var lblValue = new Label
        {
            Text      = SessionFormatter.FormatISK(amount),
            Left      = 18,
            Top       = y,
            Width     = card.Width - 36,
            TextAlign = ContentAlignment.TopRight,
            ForeColor = valueColor,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        card.Controls.Add(lblValue);

        return y + 22;
    }

    private int AddCardRowRaw(Panel card, string label, string value, Color valueColor, int width, int y)
    {
        var lblLabel = new Label
        {
            Text      = label,
            Left      = 18,
            Top       = y,
            AutoSize  = true,
            ForeColor = C_Muted,
            Font      = new Font("Segoe UI", 9f)
        };
        card.Controls.Add(lblLabel);

        var lblValue = new Label
        {
            Text      = value,
            Left      = 18,
            Top       = y,
            Width     = card.Width - 36,
            TextAlign = ContentAlignment.TopRight,
            ForeColor = valueColor,
            Font      = new Font("Segoe UI", 9f, FontStyle.Bold)
        };
        card.Controls.Add(lblValue);

        return y + 22;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private RattingSession? SelectedSession()
    {
        int i = _sessionList.SelectedIndex;
        return i >= 0 && i < _sessions.Count ? _sessions[i] : null;
    }

    private void SetStatus(string left, string right)
    {
        Action a = () => { _statusLbl.Text = left; if (right.Length > 0) _statusRight.Text = right; };
        if (InvokeRequired) Invoke(a);
        else a();
    }

    private Button DarkButton(string text, EventHandler handler, int left, int top) =>
        new Button()
        {
            Text      = text,
            BackColor = C_Panel,
            ForeColor = C_Fg,
            FlatStyle = FlatStyle.Flat,
            Left = left, Top = top, Width = 120, Height = 28,
            Font = new Font("Segoe UI", 8.5f),
            Cursor = Cursors.Hand
        }.Also(b => { b.FlatAppearance.BorderColor = Color.FromArgb(55, 65, 80); b.Click += handler; });

    private ToolStripButton TBtn(string text, EventHandler h) =>
        new(text, null, h)
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            ForeColor    = C_Fg,
            Font         = new Font("Segoe UI", 9f)
        };

    private void DrawTabItem(object? sender, DrawItemEventArgs e)
    {
        var tab    = _tabs.TabPages[e.Index];
        var bounds = _tabs.GetTabRect(e.Index);
        bool sel   = e.Index == _tabs.SelectedIndex;

        e.Graphics.FillRectangle(new SolidBrush(sel ? C_Bg : C_Panel), bounds);
        e.Graphics.DrawString(tab.Text, new Font("Segoe UI Semibold", 8.5f),
            new SolidBrush(sel ? C_Blue : C_Muted), bounds,
            new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _pollTimer?.Dispose();
        base.OnFormClosed(e);
    }
}

// ── Console output redirection ────────────────────────────────────────────────
internal class ConsoleWriter : System.IO.TextWriter
{
    private readonly TextBox _textBox;
    private readonly Form _form;

    public ConsoleWriter(TextBox textBox, Form form)
    {
        _textBox = textBox;
        _form = form;
    }

    public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

    public override void Write(char value)
    {
        WriteToTextBox(value.ToString());
    }

    public override void Write(string? value)
    {
        if (value != null) WriteToTextBox(value);
    }

    public override void WriteLine(string? value)
    {
        WriteToTextBox((value ?? "") + Environment.NewLine);
    }

    private void WriteToTextBox(string text)
    {
        if (_form.InvokeRequired)
        {
            _form.Invoke(() => AppendText(text));
        }
        else
        {
            AppendText(text);
        }
    }

    private void AppendText(string text)
    {
        _textBox.AppendText(text);
        _textBox.SelectionStart = _textBox.TextLength;
        _textBox.ScrollToCaret();
    }
}

// ── Extension helper ──────────────────────────────────────────────────────────
internal static class ControlExtensions
{
    public static T Also<T>(this T obj, Action<T> action) { action(obj); return obj; }
}

// ── Split dialog ──────────────────────────────────────────────────────────────
public class SplitDialog : Form
{
    public DateTime SplitPoint { get; private set; }
    private readonly DateTimePicker _picker;

    public SplitDialog(DateTime sessionStart, DateTime sessionEnd)
    {
        Text = "Add Manual Session Split";
        Size = new Size(360, 155);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        Controls.Add(new Label
            { Text = $"Split point (UTC)  [{sessionStart:HH:mm} \u2013 {sessionEnd:HH:mm}]:",
              Left = 10, Top = 12, AutoSize = true });

        var lo  = sessionStart < DateTimePicker.MinimumDateTime ? DateTimePicker.MinimumDateTime : sessionStart;
        var hi  = sessionEnd   > DateTimePicker.MaximumDateTime ? DateTimePicker.MaximumDateTime : sessionEnd;
        var mid = lo.AddSeconds((hi - lo).TotalSeconds / 2);

        _picker = new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy.MM.dd   HH:mm",
            ShowUpDown = true, MinDate = lo, MaxDate = hi, Value = mid,
            Left = 10, Top = 38, Width = 220
        };

        var ok     = new Button { Text = "OK",     DialogResult = DialogResult.OK,     Left = 240, Top = 36, Width = 90 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 240, Top = 64, Width = 90 };
        ok.Click += (_, _) => SplitPoint = _picker.Value;

        Controls.AddRange([_picker, ok, cancel]);
        AcceptButton = ok; CancelButton = cancel;
    }
}

// ── Settings dialog ───────────────────────────────────────────────────────────
public class SettingsForm : Form
{
    public AppSettings Result { get; private set; } = new();
    private readonly TextBox       _clientId;
    private readonly TextBox       _clientSecret;
    private readonly NumericUpDown _gapSpinner;
    private readonly NumericUpDown _pollSpinner;
    private readonly CheckBox      _piCheck;

    public SettingsForm(AppSettings current)
    {
        Text = "Settings";
        Size = new Size(480, 298);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        int y = 14;
        AddRow("EVE App Client ID:", ref y,
            _clientId = new TextBox { Width = 300, Text = current.ClientId });

        AddRow("EVE App Client Secret:", ref y,
            _clientSecret = new TextBox { Width = 300, Text = current.ClientSecret,
                UseSystemPasswordChar = true });

        AddRow("Session gap threshold (minutes):", ref y,
            _gapSpinner = new NumericUpDown
                { Minimum = 10, Maximum = 480, Value = current.GapMinutes, Width = 70 });

        AddRow("Auto-refresh interval (minutes):", ref y,
            _pollSpinner = new NumericUpDown
                { Minimum = 1, Maximum = 60, Value = current.PollIntervalMinutes, Width = 70 });

        AddRow("Include PI export tax in session reports:", ref y,
            _piCheck = new CheckBox { Checked = current.IncludePITax });

        y += 8;
        var ok     = new Button { Text = "OK",     DialogResult = DialogResult.OK,     Left = 280, Top = y, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 372, Top = y, Width = 80 };
        ok.Click += (_, _) => Result = new AppSettings
        {
            ClientId            = _clientId.Text.Trim(),
            ClientSecret        = _clientSecret.Text.Trim(),
            GapMinutes          = (int)_gapSpinner.Value,
            PollIntervalMinutes = (int)_pollSpinner.Value,
            IncludePITax        = _piCheck.Checked
        };

        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
    }

    private void AddRow(string label, ref int y, Control control)
    {
        Controls.Add(new Label { Text = label, Left = 14, Top = y + 4, AutoSize = true });
        control.Left = 310; control.Top = y;
        Controls.Add(control);
        y += 36;
    }
}

// ── Dark toolbar theme ────────────────────────────────────────────────────────
internal class DarkColorTable : ProfessionalColorTable
{
    private static readonly Color D = Color.FromArgb(30, 35, 42);
    private static readonly Color H = Color.FromArgb(50, 60, 75);
    private static readonly Color S = Color.FromArgb(55, 65, 80);

    public override Color ToolStripGradientBegin         => D;
    public override Color ToolStripGradientMiddle        => D;
    public override Color ToolStripGradientEnd           => D;
    public override Color ButtonSelectedGradientBegin    => H;
    public override Color ButtonSelectedGradientMiddle   => H;
    public override Color ButtonSelectedGradientEnd      => H;
    public override Color ButtonPressedGradientBegin     => H;
    public override Color ButtonPressedGradientMiddle    => H;
    public override Color ButtonPressedGradientEnd       => H;
    public override Color ButtonCheckedGradientBegin     => H;
    public override Color ButtonCheckedGradientMiddle    => H;
    public override Color ButtonCheckedGradientEnd       => H;
    public override Color ButtonSelectedBorder           => H;
    public override Color ButtonPressedBorder            => H;
    public override Color ButtonCheckedHighlightBorder   => H;
    public override Color SeparatorDark                  => S;
    public override Color SeparatorLight                 => S;
}
