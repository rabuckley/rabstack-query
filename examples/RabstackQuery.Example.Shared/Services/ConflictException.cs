namespace RabstackQuery.Example.Shared.Services;

public sealed class ConflictException(string message) : Exception(message);
