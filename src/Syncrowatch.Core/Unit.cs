namespace Syncrowatch.Core;

public sealed record Unit
{
    public static readonly Unit Value = new();

    private Unit()
    {
    }
}
