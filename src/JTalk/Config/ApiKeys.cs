namespace JTalk.Config;

/// <summary>Shared key lookup: literal config values first, then named env vars, first non-empty wins.</summary>
public static class ApiKeys
{
    public static string? Resolve(params string?[] candidates)
    {
        // Callers interleave literals and env-var values via Env(); this just picks the first non-empty.
        return candidates.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    public static string? Env(string? name) =>
        string.IsNullOrWhiteSpace(name) ? null : Environment.GetEnvironmentVariable(name);
}
