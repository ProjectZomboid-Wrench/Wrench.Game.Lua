using Zomboid.Java;
using Zomboid.Java.Characters;

namespace Zomboid.Lua.ContextMenus;

/// <summary>
/// A context menu being built, over PZ's <see cref="ISContextMenu"/>. Valid while it is filled; option handlers run
/// later, on click, and get fresh arguments through <see cref="OptionClick"/>.
/// </summary>
public class ContextMenu
{
    private const int MaxParams = 10;

    private readonly Func<ISToolTip> _newTooltip;

    internal ContextMenu(ISContextMenu native, MenuDispatch dispatch, int player, Func<ISToolTip> newTooltip)
    {
        Native = native;
        Dispatch = dispatch;
        PlayerNumber = player;
        _newTooltip = newTooltip;
    }

    /// <summary>The underlying <c>ISContextMenu</c>, for anything this API doesn't cover.</summary>
    public ISContextMenu Native { get; }

    public int PlayerNumber { get; }

    public IsoPlayer Player => Globals.GetSpecificPlayer(PlayerNumber);

    internal MenuDispatch Dispatch { get; }

    public IReadOnlyList<MenuOption> Options => Native.Options.Select(option => new MenuOption(this, option)).ToList();

    /// <summary>
    /// Adds an option. <paramref name="args"/> (at most 10) become the option's parameters and reach the handler as
    /// <see cref="OptionClick.Args"/>; pass game objects the handler needs that way rather than capturing them, since
    /// captured ones have expired by the time of the click.
    /// </summary>
    public MenuOption AddOption(string text, Action<OptionClick> onSelect, params object?[] args)
    {
        if (args.Length > MaxParams)
            throw new ArgumentException($"ISContextMenu options take at most {MaxParams} parameters", nameof(args));
        var id = Dispatch.Add(PlayerNumber, onSelect);
        var p = new object?[MaxParams];
        args.CopyTo(p, 0);
        return new MenuOption(this, Native.AddOption(text, id, Dispatch.Function, p[0], p[1], p[2], p[3], p[4], p[5], p[6], p[7], p[8], p[9]));
    }

    /// <summary>Adds an option that opens a submenu, and returns the submenu.</summary>
    public ContextMenu AddSubMenu(string text)
    {
        var option = Native.AddOption(text);
        var submenu = Native.GetNew(Native);
        Native.AddSubMenu(option, submenu);
        return new ContextMenu(submenu, Dispatch, PlayerNumber, _newTooltip);
    }

    /// <summary>The first option showing <paramref name="text"/>; menus hold translated text, see <see cref="Globals.GetText"/>.</summary>
    public MenuOption? Find(string text) => Native.GetOptionFromName(text) is { } option ? new MenuOption(this, option) : null;

    /// <summary>Removes the first option showing <paramref name="text"/>; false if there was none.</summary>
    public bool Remove(string text)
    {
        if (Native.GetOptionFromName(text) is null)
            return false;
        Native.RemoveOptionByName(text);
        return true;
    }

    internal ISToolTip NewTooltip() => _newTooltip();

    internal ContextMenu? SubMenu(int? number) =>
        number is { } n && Native.GetSubMenu(n) is { } submenu ? new ContextMenu(submenu, Dispatch, PlayerNumber, _newTooltip) : null;
}
