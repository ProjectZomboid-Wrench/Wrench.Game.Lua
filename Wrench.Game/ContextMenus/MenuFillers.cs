namespace Zomboid.ContextMenus;

/// <summary>Adds to or changes the inventory context menu each time it opens. Found and created when the mod loads.</summary>
public abstract class InventoryMenuFiller
{
    public abstract void Fill(InventoryMenu menu);
}

/// <summary>Adds to or changes the world context menu each time it opens. Found and created when the mod loads.</summary>
public abstract class WorldMenuFiller
{
    public abstract void Fill(WorldMenu menu);
}
