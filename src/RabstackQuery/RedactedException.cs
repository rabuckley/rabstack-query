namespace RabstackQuery;

/// <summary>
/// Sentinel exception used during dehydration to replace error details that
/// a <see cref="DehydrateOptions.ShouldRedactErrors"/> predicate has marked
/// for redaction. The original exception type and message are discarded.
/// </summary>
public sealed class RedactedException : Exception
{
    public RedactedException() : base("redacted") { }
}
