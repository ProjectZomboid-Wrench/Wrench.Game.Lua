using Wrench;
using Zomboid.Java;
using Zomboid.Java.Inventory;
using Zomboid.Java.Iso;

namespace Zomboid.Lua.ContextMenus;

/// <summary>The context menu for items in an inventory or container pane.</summary>
public sealed class InventoryMenu : ContextMenu
{
    internal InventoryMenu(ISContextMenu native, MenuDispatch dispatch, int player, IReadOnlyList<GameObject> items)
        : base(native, dispatch, player, ISInventoryPaneContextMenu.AddToolTip)
    {
        ItemObjects = items;
        Items = items.Select(JavaConvert.To<InventoryItem>).ToList();
    }

    /// <summary>The clicked items; a stack counts once, by its first item.</summary>
    public IReadOnlyList<InventoryItem> Items { get; }

    internal IReadOnlyList<GameObject> ItemObjects { get; }

    // Stacks arrive as { items = { first, ... } }, single items as themselves.
    internal static List<GameObject> Representatives(GameObject entries) =>
        entries.Sequence().Select(entry =>
        {
            GameObject value = entry;
            return value.IsTable ? (GameObject)value["items"][1] : value;
        }).ToList();
}

/// <summary>The context menu for the world: whatever is on the clicked square.</summary>
public sealed class WorldMenu : ContextMenu
{
    internal WorldMenu(ISContextMenu native, MenuDispatch dispatch, int player, GameObject objects)
        : base(native, dispatch, player, ISWorldObjectContextMenu.AddToolTip) =>
        Objects = objects.Sequence().Select(o => JavaConvert.To<IsoObject>((object?)o)).ToList();

    /// <summary>The objects under the cursor.</summary>
    public IReadOnlyList<IsoObject> Objects { get; }
}
