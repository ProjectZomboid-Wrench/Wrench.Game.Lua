using Zomboid.Java.Characters;

namespace Zomboid.Lua.TimedActions;

/// <summary>
/// A timed action written in C# from scratch: an <c>ISBaseTimedAction</c> subclass that PZ queues, runs and cancels
/// like its own. Override what the action needs (whatever isn't overridden runs vanilla's) and put its effects in
/// <see cref="ISBaseTimedAction.Complete"/>, as B42's actions do. To build on a vanilla action instead, subclass its
/// class (e.g. <c>ISInventoryTransferAction</c>) the same way.
/// </summary>
/// <remarks>
/// Multiplayer isn't supported yet: the server rebuilds actions from their Lua class, which only exists where Wrench
/// and the mod run.
/// </remarks>
public abstract class TimedAction : ISBaseTimedAction
{
    protected TimedAction(IsoGameCharacter character) : base(character)
    {
    }

    /// <summary>Checked every tick; the action stops once it returns false.</summary>
    public abstract override bool IsValid();

    /// <summary>The action's effects, after <see cref="ISBaseTimedAction.Perform"/>. Returns whether it succeeded.</summary>
    public override bool Complete() => true;
}
