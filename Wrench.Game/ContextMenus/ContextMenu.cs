using Wrench;
using Zomboid.Characters;

namespace Zomboid.ContextMenus;

/// <summary>
/// A context menu being built (PZ's <c>ISContextMenu</c>). Valid while it is filled; option handlers run later, on
/// click, and get fresh arguments through <see cref="OptionClick"/>.
/// </summary>
public class ContextMenu
{
    private readonly string _tooltipFactory;

    internal ContextMenu(GameObject table, MenuDispatch dispatch, int player, string tooltipFactory)
    {
        Table = table;
        Dispatch = dispatch;
        PlayerNumber = player;
        _tooltipFactory = tooltipFactory;
    }

    /// <summary>The underlying <c>ISContextMenu</c>, for anything this API doesn't cover.</summary>
    public GameObject Table { get; }

    public int PlayerNumber { get; }

    public IsoPlayer Player => Globals.GetSpecificPlayer(PlayerNumber);

    internal MenuDispatch Dispatch { get; }

    public IReadOnlyList<MenuOption> Options =>
        ((GameObject)Table["options"]).Sequence().Select(option => new MenuOption(this, (GameObject)option)).ToList();

    /// <summary>
    /// Adds an option. <paramref name="args"/> become the option's parameters and reach the handler as
    /// <see cref="OptionClick.Args"/>; pass game objects the handler needs that way rather than capturing them, since
    /// captured ones have expired by the time of the click.
    /// </summary>
    public MenuOption AddOption(string text, Action<OptionClick> onSelect, params object?[] args)
    {
        var id = Dispatch.Add(PlayerNumber, onSelect);
        return Wrap(Call("addOption", [text, id, Dispatch.Function, .. args]));
    }

    /// <summary>Adds an option that opens a submenu, and returns the submenu.</summary>
    public ContextMenu AddSubMenu(string text)
    {
        var option = Call("addOption", [text, null, null]);
        var submenu = (GameObject)Call("getNew", [Table])!;
        Call("addSubMenu", [option, submenu]);
        return new ContextMenu(submenu, Dispatch, PlayerNumber, _tooltipFactory);
    }

    /// <summary>The first option showing <paramref name="text"/>; menus hold translated text, see <see cref="Globals.GetText"/>.</summary>
    public MenuOption? Find(string text) => Call("getOptionFromName", [text]) is GameObject option ? Wrap(option) : null;

    /// <summary>Removes the first option showing <paramref name="text"/>; false if there was none.</summary>
    public bool Remove(string text)
    {
        if (Find(text) is null)
            return false;
        Call("removeOptionByName", [text]);
        return true;
    }

    internal GameObject NewTooltip()
    {
        var segments = _tooltipFactory.Split('.');
        var owner = (GameObject)LuaHost.Required.GetGlobal(segments[0])!;
        return (GameObject)LuaApiFirst(LuaHost.Required.Call(Dispatch.Mod, LuaHost.Required.Index(owner, segments[1])!, []))!;
    }

    internal ContextMenu? SubMenu(object? number)
    {
        if (number is null)
            return null;
        var root = (GameObject)LuaApiFirst(LuaHost.Required.Call(Dispatch.Mod, LuaHost.Required.GetGlobal("getPlayerContextMenu")!,
            [(double)PlayerNumber]))!;
        return LuaHost.Required.Index((GameObject)root["instanceMap"], number) is GameObject submenu
            ? new ContextMenu(submenu, Dispatch, PlayerNumber, _tooltipFactory)
            : null;
    }

    private MenuOption Wrap(object? option) =>
        new(this, option as GameObject ?? throw new LuaException("ISContextMenu didn't return an option"));

    private object? Call(string method, object?[] args) => LuaApiFirst(LuaHost.Required.CallMethod(Dispatch.Mod, Table, method, args));

    private static object? LuaApiFirst(object?[] results) => results.Length > 0 ? results[0] : null;
}
