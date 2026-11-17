namespace Zomboid.Lua;

/// <summary>A Lua value that is one of several types, e.g. <c>InventoryItem | IsoObject</c>. Switch over it by type.</summary>
public union OneOf<T1, T2>(T1, T2);

/// <inheritdoc cref="OneOf{T1, T2}"/>
public union OneOf<T1, T2, T3>(T1, T2, T3);

/// <inheritdoc cref="OneOf{T1, T2}"/>
public union OneOf<T1, T2, T3, T4>(T1, T2, T3, T4);

/// <inheritdoc cref="OneOf{T1, T2}"/>
public union OneOf<T1, T2, T3, T4, T5>(T1, T2, T3, T4, T5);
