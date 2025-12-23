namespace RabstackQuery.Example.Shared.Services;

public sealed class RateLimitException(string message) : Exception(message);
