namespace RabstackQuery;

/// <summary>
/// Base class for actions that describe query state transitions. Dispatched by the
/// query reducer to update <see cref="QueryState{TData}"/>.
/// </summary>
/// <remarks>
/// This is a closed hierarchy — all concrete subclasses are defined within RabstackQuery
/// and are sealed. Do not create custom subclasses.
/// </remarks>
public abstract class DispatchAction
{
    private protected DispatchAction() { }
}
