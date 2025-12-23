namespace RabstackQuery;

public interface INotifyManager
{
    public void Batch(Action callback);
    public T Batch<T>(Func<T> callback);
}
