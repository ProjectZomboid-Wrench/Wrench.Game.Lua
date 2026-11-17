using Zomboid.Lua.Umbrella;

namespace Zomboid.Lua.ContextMenus;

/// <summary>An option in a <see cref="ContextMenu"/>, whoever added it: vanilla, a Lua mod or a .NET mod.</summary>
public sealed class MenuOption
{
    private readonly ContextMenu _menu;

    internal MenuOption(ContextMenu menu, ISContextMenuOption native)
    {
        _menu = menu;
        Native = native;
    }

    /// <summary>The underlying option table.</summary>
    public ISContextMenuOption Native { get; }

    public string Text
    {
        get => Native.Name;
        set => Native.Name = value;
    }

    /// <summary>False shows the option greyed out (<c>notAvailable</c>); give it a <see cref="Tooltip"/> saying why.</summary>
    public bool Enabled
    {
        get => Native.NotAvailable is not true;
        set => Native.NotAvailable = !value;
    }

    public string? Tooltip
    {
        get => Native.ToolTip?.Description;
        set
        {
            if (value is null)
            {
                Native.ToolTip = null;
                return;
            }
            var tooltip = _menu.NewTooltip();
            tooltip.Description = value;
            Native.ToolTip = tooltip;
        }
    }

    /// <summary>The submenu this option opens, if any.</summary>
    public ContextMenu? SubMenu => _menu.SubMenu(Native.SubOption);

    public void Remove() => _menu.Remove(Text);

    /// <summary>
    /// Runs <paramref name="handler"/> instead of the option's handler when it is clicked, for this menu only.
    /// <see cref="OptionClick.CallOriginal"/> runs the original.
    /// </summary>
    public void Override(Action<OptionClick> handler)
    {
        var id = _menu.Dispatch.Add(_menu.PlayerNumber, handler, Native.OnSelect, (object?)Native.Target);
        Native.OnSelect = _menu.Dispatch.Function;
        Native.Target = id;
    }
}
