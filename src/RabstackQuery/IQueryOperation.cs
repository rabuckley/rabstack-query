namespace RabstackQuery;

/// <summary>
/// Double-dispatch interface for executing typed operations on a <see cref="Query{TData}"/>
/// through its non-generic <see cref="Query"/> base. Callers implement this interface with
/// generic <see cref="Execute{TData}"/> to recover the concrete <c>TData</c> that is erased
/// on the base class.
/// </summary>
internal interface IQueryOperation<TResult>
{
    TResult Execute<TData>(Query<TData> query);
}
