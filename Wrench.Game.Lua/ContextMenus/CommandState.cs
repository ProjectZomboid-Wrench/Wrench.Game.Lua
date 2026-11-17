using Zomboid.Java;

namespace Zomboid.Lua.ContextMenus;

/// <summary>Whether a command's option is shown, and if shown whether it can be clicked.</summary>
public readonly record struct CommandState
{
    private CommandState(bool isVisible, bool isEnabled, string? reason)
    {
        IsVisible = isVisible;
        IsEnabled = isEnabled;
        Reason = reason;
    }

    public static CommandState Enabled => new(true, true, null);

    public static CommandState Hidden => new(false, false, null);

    public bool IsVisible { get; }

    public bool IsEnabled { get; }

    /// <summary>The tooltip on a greyed-out option, as a translation key or plain text.</summary>
    public string? Reason { get; }

    /// <summary>Shown greyed out, with <paramref name="reason"/> (a translation key or plain text) as its tooltip.</summary>
    public static CommandState Disabled(string reason) => new(true, false, reason);

    public static implicit operator CommandState(bool visible) => visible ? Enabled : Hidden;
}
