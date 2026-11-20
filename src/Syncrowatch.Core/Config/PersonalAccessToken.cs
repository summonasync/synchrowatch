namespace Syncrowatch.Core.Config;

public sealed record PersonalAccessToken
{
    public PersonalAccessToken(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value;
    }

    public string Value { get; }

    public override string ToString() => "personal-access-token";
}
