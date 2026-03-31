namespace RabstackQuery;

/// <summary>
/// Custom exception for testing custom TError types.
/// </summary>
public sealed class CustomException : Exception
{
    public CustomException(string message) : base(message) { }
}
