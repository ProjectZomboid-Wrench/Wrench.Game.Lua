using System.Globalization;
using Wrench;
using Zomboid.Characters;
using Zomboid.Characters.CharacterTimedActions;

namespace Zomboid.TimedActions;

/// <summary>
/// A timed action written in C#: an <c>ISBaseTimedAction</c> subclass that PZ queues, runs and cancels like its own.
/// Override what the action needs; whatever isn't overridden runs vanilla's. Put the action's effects in
/// <see cref="Complete"/>, as B42's actions do.
/// </summary>
/// <remarks>
/// Multiplayer isn't supported yet: the server rebuilds actions from their Lua class, which only exists where Wrench
/// and the mod run.
/// </remarks>
[LuaClass("ISBaseTimedAction")]
public abstract class TimedAction : LuaObject
{
    protected TimedAction(IsoGameCharacter character) : base(character)
    {
    }

    public IsoGameCharacter Character => Get<IsoGameCharacter>("character");

    /// <summary>The duration in ticks, adjusted by vanilla for moodles and wounds when the action starts; -1 runs until stopped.</summary>
    public int MaxTime
    {
        get => Get<int>("maxTime");
        set => Set("maxTime", value);
    }

    public bool StopOnWalk
    {
        get => Get<bool>("stopOnWalk");
        set => Set("stopOnWalk", value);
    }

    public bool StopOnRun
    {
        get => Get<bool>("stopOnRun");
        set => Set("stopOnRun", value);
    }

    public bool StopOnAim
    {
        get => Get<bool>("stopOnAim");
        set => Set("stopOnAim", value);
    }

    public float CaloriesModifier
    {
        get => Get<float>("caloriesModifier");
        set => Set("caloriesModifier", value);
    }

    public bool UseProgressBar
    {
        get => Get<bool?>("useProgressBar") ?? true;
        set => Set("useProgressBar", value);
    }

    /// <summary>The Java side of the running action; null until it starts.</summary>
    public LuaTimedActionNew? JavaAction => Get<LuaTimedActionNew?>("action");

    /// <summary>Progress from 0 to 1 once started.</summary>
    public float JobDelta => Convert.ToSingle((object?)CallLua("getJobDelta"), CultureInfo.InvariantCulture);

    /// <summary>Adds the action to its character's queue; false if PZ turned it away (asleep, dragging a corpse).</summary>
    public bool Queue()
    {
        var queue = LuaHost.Required.GetGlobal("ISTimedActionQueue") as GameObject
                    ?? throw new LuaException("ISTimedActionQueue isn't loaded");
        var add = LuaHost.Required.Index(queue, "add");
        var queued = add is not null && First(LuaHost.Required.Call(Class.Mod, add, [Table])) is not null;
        if (!queued)
            Release();
        return queued;
    }

    [LuaMethod("isValidStart")]
    public virtual bool IsValidStart() => IsTruthy((object?)CallBase("isValidStart"));

    /// <summary>Checked every tick; the action stops once it returns false.</summary>
    [LuaMethod("isValid")]
    public abstract bool IsValid();

    /// <summary>True to hold off starting, e.g. while the character turns to face something.</summary>
    [LuaMethod("waitToStart")]
    public virtual bool WaitToStart() => IsTruthy((object?)CallBase("waitToStart"));

    [LuaMethod("update")]
    public virtual void Update() => CallBase("update");

    [LuaMethod("start")]
    public virtual void Start() => CallBase("start");

    /// <summary>The action was interrupted; vanilla's clears the rest of the queue, so call the base.</summary>
    public virtual void Stop() => CallBase("stop");

    /// <summary>The action finished; vanilla's starts the next one in the queue, so call the base.</summary>
    public virtual void Perform() => CallBase("perform");

    /// <summary>The action's effects, after <see cref="Perform"/>. Returns whether it succeeded.</summary>
    public virtual bool Complete() => true;

    /// <summary>The action was dropped from the queue before it started.</summary>
    public virtual void ForceCancel() => CallBase("forceCancel");

    [LuaMethod("getDuration")]
    public virtual float GetDuration()
    {
        object? duration = CallBase("getDuration");
        return duration is null ? MaxTime : Convert.ToSingle(duration, CultureInfo.InvariantCulture);
    }

    public void SetActionAnim(string animation) => CallLua("setActionAnim", animation);

    public void SetAnimVariable(string key, object value) => CallLua("setAnimVariable", key, value);

    /// <summary>The models in the character's hands: items, item types (strings) or null for empty.</summary>
    public void SetOverrideHandModels(object? primary, object? secondary) =>
        CallLua("setOverrideHandModels", primary, secondary);

    [LuaMethod("stop")]
    private void OnStop()
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
    private void OnPerform()
    {
        Perform();
        if (Globals.IsClient())
            Release();
    }

    [LuaMethod("complete")]
    private bool OnComplete()
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
    private void OnForceCancel()
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
