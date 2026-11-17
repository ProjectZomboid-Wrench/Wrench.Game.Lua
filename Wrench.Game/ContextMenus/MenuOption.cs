using Wrench;

namespace Zomboid.ContextMenus;

/// <summary>An option in a <see cref="ContextMenu"/>, whoever added it: vanilla, a Lua mod or a .NET mod.</summary>
public sealed class MenuOption
{
    private readonly ContextMenu _menu;

    internal MenuOption(ContextMenu menu, GameObject table)
    {
        _menu = menu;
        Table = table;
    }

    /// <summary>The underlying option table.</summary>
    public GameObject Table { get; }

    public string Text
    {
        get => (string)Table["name"];
        set => Table["name"] = value;
    }

    /// <summary>False shows the option greyed out (<c>notAvailable</c>); give it a <see cref="Tooltip"/> saying why.</summary>
    public bool Enabled
    {
        get => Table["notAvailable"] is not true;
        set => Table["notAvailable"] = !value;
    }

    public string? Tooltip
    {
        get => Table["toolTip"] is GameObject tooltip ? (string?)tooltip["description"] : null;
        set
        {
            if (value is null)
            {
                Table["toolTip"] = null;
                return;
            }
            var tooltip = _menu.NewTooltip();
            tooltip["description"] = value;
            Table["toolTip"] = tooltip;
        }
    }

    /// <summary>The submenu this option opens, if any.</summary>
    public ContextMenu? SubMenu => _menu.SubMenu(Table["subOption"]);

    public void Remove() => _menu.Remove(Text);

    /// <summary>
    /// Runs <paramref name="handler"/> instead of the option's handler when it is clicked, for this menu only.
    /// <see cref="OptionClick.CallOriginal"/> runs the original.
    /// </summary>
    public void Override(Action<OptionClick> handler)
    {
        var id = _menu.Dispatch.Add(_menu.PlayerNumber, handler, (object?)Table["onSelect"], (object?)Table["target"]);
        Table["onSelect"] = _menu.Dispatch.Function;
        Table["target"] = id;
    }
}
