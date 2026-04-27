using System.Text.Json;

namespace EveSessionTracker;

public static class TokenStore
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EveSessionTracker");

    private static readonly string SettingsPath  = Path.Combine(DataDir, "settings.json");
    private static readonly string CharactersPath = Path.Combine(DataDir, "characters.json");

    private static readonly JsonSerializerOptions Opts =
        new() { WriteIndented = true };

    // ── Settings ──────────────────────────────────────────────────────────────

    public static AppSettings LoadSettings()
    {
        EnsureDir();
        if (!File.Exists(SettingsPath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public static void SaveSettings(AppSettings settings)
    {
        EnsureDir();
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, Opts));
    }

    // ── Characters ────────────────────────────────────────────────────────────

    public static List<CharacterToken> LoadCharacters()
    {
        EnsureDir();
        if (!File.Exists(CharactersPath)) return [];
        try
        {
            var json = File.ReadAllText(CharactersPath);
            var tokens = JsonSerializer.Deserialize<List<CharacterToken>>(json) ?? [];
            
            // Default any 0% tax rates to 10% (for old tokens without this field)
            bool needsSave = false;
            foreach (var token in tokens)
            {
                if (token.CorpTaxRate == 0.0)
                {
                    token.CorpTaxRate = 10.0;
                    needsSave = true;
                }
            }
            
            if (needsSave)
                SaveCharacters(tokens);
            
            return tokens;
        }
        catch { return []; }
    }

    public static void SaveCharacters(List<CharacterToken> characters)
    {
        EnsureDir();
        File.WriteAllText(CharactersPath, JsonSerializer.Serialize(characters, Opts));
    }

    public static void UpsertCharacter(CharacterToken token)
    {
        var list = LoadCharacters();
        var existing = list.FindIndex(c => c.CharacterId == token.CharacterId);
        if (existing >= 0) list[existing] = token;
        else               list.Add(token);
        SaveCharacters(list);
    }

    public static void RemoveCharacter(long characterId)
    {
        var list = LoadCharacters();
        list.RemoveAll(c => c.CharacterId == characterId);
        SaveCharacters(list);
    }

    private static void EnsureDir()
    {
        if (!Directory.Exists(DataDir))
            Directory.CreateDirectory(DataDir);
    }

    // ── Path for troubleshooting ──────────────────────────────────────────────
    public static string DataDirectory => DataDir;
}
