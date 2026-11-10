namespace Syncrowatch.Core.Transport;

public static class GitEnv
{
    public static IReadOnlyDictionary<string, string> PromptDisabled => new Dictionary<string, string>
    {
        ["GIT_TERMINAL_PROMPT"] = "0",
        ["GIT_CONFIG_NOSYSTEM"] = "1"
    };
}
