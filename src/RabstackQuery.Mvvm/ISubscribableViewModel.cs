namespace RabstackQuery.Mvvm;

internal interface ISubscribableViewModel : IDisposable
{
    bool Subscribed { get; set; }
}
