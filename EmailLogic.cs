using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace EmailChecker;

/// <summary>All stateless business-logic methods — no UI references.</summary>
internal static class EmailLogic
{
    // ── Constants ────────────────────────────────────────────────────────────

    public const string InvalidHeader = "=== INVALID EMAILS ===";

    private static readonly Regex EmailRe = new(
        @"[a-zA-Z0-9][a-zA-Z0-9._%+\-]*@[a-zA-Z0-9][a-zA-Z0-9.\-]*\.[a-zA-Z]{2,}",
        RegexOptions.Compiled);

    private static readonly HashSet<string> MultiPartTlds = new(StringComparer.OrdinalIgnoreCase)
    {
        "co.kr","com.kr","ne.kr","or.kr","pe.kr","go.kr","re.kr","ac.kr",
        "co.jp","ne.jp","or.jp","go.jp","ac.jp",
        "co.uk","org.uk","me.uk","net.uk","ltd.uk",
        "com.au","net.au","org.au","gov.au","edu.au",
        "com.br","net.br","org.br","gov.br",
        "com.tw","net.tw","org.tw",
        "co.nz","net.nz","org.nz",
        "com.cn","net.cn","org.cn",
        "co.in","net.in","org.in",
        "com.sg","net.sg","org.sg",
    };

    private static readonly HashSet<string> InvalidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "notexist", "not_exist", "not exist", "noexist", "no_exist", "no exist",
        "error", "invalid", "disabled", "disable", "inactive", "bounce",
    };

    // ── Email extraction ──────────────────────────────────────────────────────

    /// <summary>Return the first valid-looking email from a line, or null.</summary>
    public static string? ExtractEmailFromLine(string line)
    {
        line = line.Trim();
        if (string.IsNullOrEmpty(line)) return null;
        var m = EmailRe.Match(line);
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    /// <summary>Read a file and return all extracted emails.</summary>
    public static List<string> ExtractEmailsFromFile(string path)
    {
        var emails = new List<string>();
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                var em = ExtractEmailFromLine(line);
                if (em != null) emails.Add(em);
            }
        }
        catch { /* silently skip unreadable files */ }
        return emails;
    }

    // ── List operations ───────────────────────────────────────────────────────

    /// <summary>Remove case-insensitive duplicates, keeping first occurrence.
    /// Returns (uniqueList, removedCount).</summary>
    public static (List<string> Unique, int Removed) Deduplicate(List<string> emails)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<string>();
        foreach (var e in emails)
        {
            if (seen.Add(e)) unique.Add(e);
        }
        return (unique, emails.Count - unique.Count);
    }

    // ── Domain helpers ────────────────────────────────────────────────────────

    /// <summary>Normalise a domain to its brand name.
    /// naver.com / naver.kr / naver.co.kr / naver.com.br → "naver"</summary>
    public static string GetBrand(string domain)
    {
        domain = domain.ToLowerInvariant().Trim();
        var parts = domain.Split('.');
        if (parts.Length >= 3)
        {
            var candidate = $"{parts[^2]}.{parts[^1]}";
            if (MultiPartTlds.Contains(candidate)) return parts[^3];
        }
        return parts.Length >= 2 ? parts[^2] : domain;
    }

    // ── Grouping ──────────────────────────────────────────────────────────────

    /// <summary>Group emails by normalised brand, sorted alphabetically.</summary>
    public static SortedDictionary<string, List<string>> GroupByDomain(List<string> emails)
    {
        var groups = new SortedDictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in emails)
        {
            var at = e.IndexOf('@');
            if (at < 0) continue;
            var brand = GetBrand(e[(at + 1)..]);
            if (!groups.TryGetValue(brand, out var list))
                groups[brand] = list = new List<string>();
            list.Add(e);
        }
        return groups;
    }

    /// <summary>Render groups into section-header format for the text area.</summary>
    public static string FormatGrouped(SortedDictionary<string, List<string>> groups)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (brand, emails) in groups)
        {
            sb.AppendLine($"=== {brand.ToUpperInvariant()} ===");
            foreach (var e in emails) sb.AppendLine(e);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Parse section-header text back into a dictionary.</summary>
    public static Dictionary<string, List<string>> ParseGroupedText(string text)
    {
        var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.StartsWith("=== ") && line.EndsWith(" ==="))
            {
                current = line[4..^4].Trim();
                groups[current] = new List<string>();
            }
            else if (!string.IsNullOrEmpty(line) && current != null)
            {
                groups[current].Add(line);
            }
        }
        return groups;
    }

    /// <summary>Strip all section headers, returning a flat list of emails.</summary>
    public static List<string> StripGroupHeaders(string text)
    {
        var result = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (!string.IsNullOrEmpty(line) && !(line.StartsWith("=== ") && line.EndsWith(" ===")))
                result.Add(line);
        }
        return result;
    }

    // ── Invalid section helpers ───────────────────────────────────────────────

    /// <summary>Split textarea content into (mainText, invalidEmails).
    /// The invalid section starts at the INVALID_HEADER line.</summary>
    public static (string Main, List<string> Invalid) SplitInvalidSection(string text)
    {
        var lines = text.Split('\n');
        int splitIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == InvalidHeader) { splitIdx = i; break; }
        }
        if (splitIdx < 0)
            return (text.Trim(), new List<string>());

        var main = string.Join('\n', lines[..splitIdx]).Trim();
        var invalid = new List<string>();
        foreach (var line in lines[(splitIdx + 1)..])
        {
            var l = line.Trim();
            if (!string.IsNullOrEmpty(l) && !(l.StartsWith("=== ") && l.EndsWith(" ===")))
                invalid.Add(l);
        }
        return (main, invalid);
    }

    /// <summary>Append the invalid section to mainText when there are invalid emails.</summary>
    public static string MergeWithInvalid(string mainText, List<string> invalidEmails)
    {
        if (invalidEmails.Count == 0) return mainText.Trim();
        return mainText.Trim() + "\n" + InvalidHeader + "\n" + string.Join('\n', invalidEmails);
    }

    // ── API ───────────────────────────────────────────────────────────────────

    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        // Accept any certificate — ensures the API call works on all Windows machines
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    })
    { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly int[] RetryDelaysMs = { 1500, 3000, 6000 };

    /// <summary>Check a single email via the YChecker API.
    /// 429 is retried up to 3 times with back-off then marked invalid.
    /// Network errors keep the email (fail-safe).</summary>
    public static async Task<(bool IsValid, string Status)> CheckEmailValidityAsync(
        string email, string apiKey)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var req = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"https://app.sonjj.com/v1/check_email/?email={Uri.EscapeDataString(email)}");
                req.Headers.Add("X-Api-Key", apiKey);

                var resp = await Http.SendAsync(req);

                if ((int)resp.StatusCode == 429)
                {
                    if (attempt < RetryDelaysMs.Length)
                    {
                        await Task.Delay(RetryDelaysMs[attempt]);
                        continue;
                    }
                    return (false, "rate_limited");
                }

                if (resp.IsSuccessStatusCode)
                {
                    var data = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
                    var status = data?.GetValueOrDefault("status", "") ?? "";
                    if (string.IsNullOrWhiteSpace(status))
                        return (true, "no_status");
                    return (!InvalidStatuses.Contains(status), status.ToLowerInvariant());
                }

                // Other non-success codes: keep email (fail-safe)
                return (true, $"http_{(int)resp.StatusCode}");
            }
            catch (Exception ex)
            {
                var msg = ex.Message;
                return (true, $"err:{(msg.Length > 60 ? msg[..60] : msg)}");
            }
        }
    }

    // ── File save ─────────────────────────────────────────────────────────────

    /// <summary>Write each group to &lt;brand&gt;_&lt;timestamp&gt;.txt in the directory.
    /// Returns the list of created file paths.</summary>
    public static List<string> SaveGroups(
        Dictionary<string, List<string>> groups, string directory)
    {
        var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var created = new List<string>();
        foreach (var (name, emails) in groups)
        {
            var safe = Regex.Replace(name.ToLowerInvariant(), @"[^\w\-]", "_");
            var path = Path.Combine(directory, $"{safe}_{ts}.txt");
            File.WriteAllLines(path, emails);
            created.Add(path);
        }
        return created;
    }

    // ── API key persistence ───────────────────────────────────────────────────

    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EmailChecker");
    private static readonly string ApiKeyFile =
        Path.Combine(ConfigDir, "apikey.txt");

    public static string LoadApiKey()
    {
        try { return File.Exists(ApiKeyFile) ? File.ReadAllText(ApiKeyFile).Trim() : string.Empty; }
        catch { return string.Empty; }
    }

    public static void SaveApiKey(string key)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(ApiKeyFile, key.Trim());
        }
        catch { }
    }
}
