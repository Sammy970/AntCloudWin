using System.Globalization;
using System.Text.Json.Nodes;

namespace AntCloud.Core.Api;

sealed class UserProfile
{
    public string FirstName { get; }
    public string Email { get; }
    public string CurrentPlan { get; }
    public DateTimeOffset? RenewDate { get; }
    public int TotalTimeMonth { get; }
    public int TimeUsedMonth { get; }

    public UserProfile(JsonObject json)
    {
        FirstName = json.Str("firstName") ?? "User";
        Email = json.Str("email") ?? "";
        CurrentPlan = json.Str("currentPlan") ?? "basic";
        RenewDate = DateTimeOffset.TryParse(json.Str("renewDate"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        TotalTimeMonth = json.Int("totalTimeMonth") ?? 0;
        TimeUsedMonth = json.Int("timeUsedMonth") ?? 0;
    }

    /// "basic" means no paid plan; AntCloud's own client refuses to start a stream for it.
    public bool HasPaidPlan => !CurrentPlan.Equals("basic", StringComparison.OrdinalIgnoreCase);
    /// Night plans are identified by a plan code ending in "Nyt".
    public bool IsNightPlan => CurrentPlan.EndsWith("nyt", StringComparison.OrdinalIgnoreCase);

    /// Readable plan name: "premiumNyt" → "Premium Night".
    public string PlanName
    {
        get
        {
            var plan = IsNightPlan ? CurrentPlan[..^3] : CurrentPlan;
            var name = plan.Length == 0 ? "" : char.ToUpperInvariant(plan[0]) + plan[1..];
            return IsNightPlan ? $"{name} Night" : name;
        }
    }

    public string Initial => FirstName.Length > 0 ? FirstName[..1].ToUpperInvariant() : "?";
    public int? MinutesLeft => TotalTimeMonth > 0 ? Math.Max(0, TotalTimeMonth - TimeUsedMonth) : null;
    public int? DaysLeft => RenewDate is { } d ? (int)Math.Floor((d - DateTimeOffset.Now).TotalDays) : null;

    public string RenewText => DaysLeft switch
    {
        null => "",
        <= 0 => "Renews today",
        1 => "Renews in 1 day",
        var n => $"Renews in {n} days",
    };

    public static string Duration(int minutes) => minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";
}

/// Loose readers for AntCloud's JSON, whose numbers sometimes arrive as strings.
static class JsonExt
{
    public static string? Str(this JsonObject? obj, string key) =>
        obj?[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static int? Int(this JsonObject? obj, string key)
    {
        if (obj?[key] is not JsonValue v) return null;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var d)) return (int)d;
        return v.TryGetValue<string>(out var s) && int.TryParse(s, out var p) ? p : null;
    }

    public static bool? Bool(this JsonObject? obj, string key) =>
        obj?[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;
}
