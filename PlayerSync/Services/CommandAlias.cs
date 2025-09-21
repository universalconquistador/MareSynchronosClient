namespace MareSynchronos.Utils;

public static class CommandAlias
{
    public const string Primary = "/sync";
    public const string Fallback = "/psync";

    private static string _active = Fallback;

    /// <summary>
    /// The command alias to display in all user-facing text.
    /// Will be "/sync" if we registered it successfully; otherwise "/psync".
    /// </summary>
    public static string Active
    {
        get => _active;
        internal set => _active = string.IsNullOrWhiteSpace(value) ? Fallback : value;
    }
}
