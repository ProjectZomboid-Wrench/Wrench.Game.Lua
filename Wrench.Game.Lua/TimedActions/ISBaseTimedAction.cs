using Wrench;

namespace Zomboid.Lua;

// Lifecycle for C# timed actions (subclasses of any vanilla action): an instance is let go once PZ is done with it.
public partial class ISBaseTimedAction
{
    /// <summary>Adds the action to its character's queue; false if PZ turned it away (asleep, dragging a corpse).</summary>
    public bool Queue()
    {
        var queue = LuaHost.Required.GetGlobal("ISTimedActionQueue") as GameObject
                    ?? throw new LuaException("ISTimedActionQueue isn't loaded");
        var add = LuaHost.Required.Index(queue, "add");
        var queued = add is not null && First(LuaHost.Required.Call(LuaHost.Required.CurrentMod, add, [Table])) is not null;
        if (!queued)
            Release();
        return queued;
    }

    [LuaMethod("stop")]
    private void StopThenRelease()
    {
        try
        {
            Stop();
        }
        finally
        {
            Release();
        }
    }

    // Clients never get complete(): the server runs it.
    [LuaMethod("perform")]
    private void PerformThenReleaseOnClient()
    {
        Perform();
        if (Java.Globals.IsClient())
            Release();
    }

    [LuaMethod("complete")]
    private bool CompleteThenRelease()
    {
        try
        {
            return Complete();
        }
        finally
        {
            Release();
        }
    }

    [LuaMethod("forceCancel")]
    private void ForceCancelThenRelease()
    {
        try
        {
            ForceCancel();
        }
        finally
        {
            Release();
        }
    }
}
