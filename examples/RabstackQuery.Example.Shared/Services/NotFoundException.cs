namespace RabstackQuery.Example.Shared.Services;

public sealed class NotFoundException(string message) : Exception(message);
