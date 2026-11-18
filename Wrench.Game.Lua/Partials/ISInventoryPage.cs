using Zomboid.Java.Inventory;

namespace Zomboid.Lua;

public partial class ISInventoryPage
{
    /// <summary>The containers the page has buttons for: the player's bags, or for the loot window everything in reach.</summary>
    public IEnumerable<ItemContainer> Containers =>
        Backpacks.Select(button => button.Field<ItemContainer?>("inventory")).OfType<ItemContainer>();
}
