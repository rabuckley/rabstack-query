// Run through eng/check-upstream-tests.sh to fetch the pinned upstream sources first.
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

const string LedgerPath = "eng/upstream-tests.json";
const string SummaryPath = "eng/upstream-tests.summary.csv";
const string UpstreamPath = ".upstream";
const string UpstreamUrl = "https://github.com/TanStack/query.git";
string[] upstreamSourceRoots = ["packages/query-core/src", "packages/react-query/src"];

var update = args.Contains("--update");
var list = args.Contains("--list");
var sinceIndex = Array.IndexOf(args, "--since");

var errors = new List<string>();
var upstream = ExtractAll(
    upstreamSourceRoots
        .SelectMany(root => Directory.EnumerateFiles(Path.Combine(UpstreamPath, root), "*.test.ts*", SearchOption.AllDirectories))
        .Select(path => (Path: path, Text: File.ReadAllText(path))),
    errors);

if (sinceIndex >= 0)
{
    if (sinceIndex + 1 >= args.Length)
    {
        Console.Error.WriteLine("--since needs an upstream commit.");
        return 2;
    }

    PrintChangesSince(args[sinceIndex + 1], upstream);
    return 0;
}

var knownKeys = upstream.Select(test => test.Key).ToHashSet(StringComparer.Ordinal);
var ported = ReadPortsMarkers(knownKeys, errors);
var decisions = ReadLedger(upstream, ported, errors);

var summary = RenderSummary(upstream, ported, decisions);

if (update)
{
    File.WriteAllText(SummaryPath, summary);
}
// .gitattributes checks out *.csv with CRLF, so only the content can be compared.
else if (!File.Exists(SummaryPath) || File.ReadAllText(SummaryPath).ReplaceLineEndings("\n") != summary)
{
    errors.Add($"{SummaryPath} is out of date. Run eng/check-upstream-tests.sh --update and review the diff.");
}

if (list)
{
    foreach (var test in upstream.Where(test => !ported.Contains(test.Key) && !decisions.ContainsKey(test.Key)))
    {
        Console.WriteLine(test.Key);
    }
}

foreach (var error in errors)
{
    Console.Error.WriteLine(error);
}

var todo = decisions.Values.Count(status => status is "todo");
var omitted = decisions.Values.Count(status => status is "omitted");
Console.Error.WriteLine(
    $"{upstream.Count} upstream tests: {ported.Count} ported, {omitted} omitted, {todo} todo, " +
    $"{upstream.Count - ported.Count - omitted - todo} unreviewed.");

return errors.Count is 0 ? 0 : 1;

static List<UpstreamTest> ExtractAll(IEnumerable<(string Path, string Text)> files, List<string> errors)
{
    var tests = new List<UpstreamTest>();
    var packageByFile = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (var (path, text) in files)
    {
        var file = Path.GetFileName(path);
        var directory = Path.GetDirectoryName(path) ?? "";
        if (packageByFile.TryGetValue(file, out var other) && other != directory)
        {
            errors.Add($"{file} exists in both {other} and {directory}; keys need to distinguish them.");
        }

        packageByFile[file] = directory;
        tests.AddRange(Extract(path, file, text, errors));
    }

    foreach (var duplicate in tests.GroupBy(test => test.Key, StringComparer.Ordinal).Where(group => group.Count() > 1))
    {
        errors.Add($"Duplicate upstream test key: {duplicate.Key}");
    }

    return tests.OrderBy(test => test.Key, StringComparer.Ordinal).ToList();
}

// Indentation supplies describe nesting without a TypeScript parser.
// The raw count guards against silently skipping unsupported test calls.
static List<UpstreamTest> Extract(string path, string file, string text, List<string> errors)
{
    var tests = new List<UpstreamTest>();
    var describes = new Stack<(int Indent, string Name)>();

    foreach (Match match in Patterns.Block.Matches(text))
    {
        var indent = match.Groups["indent"].Length;
        while (describes.Count > 0 && describes.Peek().Indent >= indent)
        {
            describes.Pop();
        }

        var isEach = match.Groups["modifiers"].Value.Contains(".each", StringComparison.Ordinal);
        var name = ReadName(text, match.Index + match.Length, isEach);
        if (name is null)
        {
            errors.Add($"{path}: can't read a literal name at '{match.Value.Trim()}' (offset {match.Index}).");
            continue;
        }

        if (match.Groups["kind"].Value is "describe")
        {
            describes.Push((indent, name));
            continue;
        }

        var segments = describes.Reverse().Select(describe => describe.Name).Prepend(file).Append(name);
        tests.Add(new UpstreamTest(file, string.Join(" > ", segments)));
    }

    var rawCount = text.Split('\n').Count(line => !Patterns.CommentLine.IsMatch(line) && Patterns.RawTest.IsMatch(line));
    if (rawCount != tests.Count)
    {
        errors.Add($"{path}: extracted {tests.Count} tests but found {rawCount} raw it/test calls. Update the extractor.");
    }

    return tests;
}

// Computed names cannot serve as stable ledger keys.
static string? ReadName(string text, int start, bool isEach)
{
    var position = start;
    if (isEach)
    {
        position = SkipBalancedArguments(text, position);
        if (position < 0 || position >= text.Length || text[position] is not '(')
        {
            return null;
        }

        position++;
    }

    while (position < text.Length && char.IsWhiteSpace(text[position]))
    {
        position++;
    }

    if (position >= text.Length || text[position] is not ('\'' or '"' or '`'))
    {
        return null;
    }

    var quote = text[position];
    var end = FindStringEnd(text, position);
    if (end < 0)
    {
        return null;
    }

    var name = text[(position + 1)..end];
    return (quote is '`' && name.Contains("${", StringComparison.Ordinal)) || name.Contains('\t') || name.Contains('\n')
        ? null
        : name;
}

static int SkipBalancedArguments(string text, int position)
{
    for (var depth = 1; position < text.Length; position++)
    {
        var character = text[position];
        if (character is '\'' or '"' or '`')
        {
            position = FindStringEnd(text, position);
            if (position < 0)
            {
                return -1;
            }
        }
        else if (character is '(' or '[' or '{')
        {
            depth++;
        }
        else if (character is ')' or ']' or '}' && --depth is 0)
        {
            return position + 1;
        }
    }

    return -1;
}

static int FindStringEnd(string text, int start)
{
    for (var position = start + 1; position < text.Length; position++)
    {
        if (text[position] is '\\')
        {
            position++;
        }
        else if (text[position] == text[start])
        {
            return position;
        }
    }

    return -1;
}

static HashSet<string> ReadPortsMarkers(HashSet<string> knownKeys, List<string> errors)
{
    var ported = new HashSet<string>(StringComparer.Ordinal);
    var sourceFiles = Directory.EnumerateFiles("test", "*.cs", SearchOption.AllDirectories)
        .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        .OrderBy(path => path, StringComparer.Ordinal);

    foreach (var path in sourceFiles)
    {
        var lines = File.ReadAllLines(path);
        for (var index = 0; index < lines.Length; index++)
        {
            var match = Patterns.PortsMarker.Match(lines[index]);
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups["key"].Value;
            if (!knownKeys.Contains(key))
            {
                errors.Add($"{path}:{index + 1}: 'Ports:' names a test that isn't upstream at the pin: {key}");
            }
            else if (!MarksTest(lines, index))
            {
                errors.Add($"{path}:{index + 1}: 'Ports:' must sit directly above a [Fact] or [Theory].");
            }
            else
            {
                ported.Add(key);
            }
        }
    }

    return ported;
}

// Keeping the claim on the test means deleting the test also deletes the claim.
static bool MarksTest(string[] lines, int markerIndex)
{
    for (var index = markerIndex + 1; index < lines.Length; index++)
    {
        var line = lines[index].TrimStart();
        if (!line.StartsWith("//", StringComparison.Ordinal))
        {
            return line.StartsWith("[Fact", StringComparison.Ordinal) || line.StartsWith("[Theory", StringComparison.Ordinal);
        }
    }

    return false;
}

static Dictionary<string, string> ReadLedger(List<UpstreamTest> upstream, HashSet<string> ported, List<string> errors)
{
    var decisions = new Dictionary<string, string>(StringComparer.Ordinal);
    if (!File.Exists(LedgerPath))
    {
        return decisions;
    }

    JsonDocument document;
    try
    {
        document = JsonDocument.Parse(File.ReadAllText(LedgerPath));
    }
    catch (JsonException exception)
    {
        errors.Add($"{LedgerPath}: {exception.Message}");
        return decisions;
    }

    using (document)
    {
        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            errors.Add($"{LedgerPath}: expected an array of entries.");
            return decisions;
        }

        var index = -1;
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            index++;
            ApplyEntry(entry, $"{LedgerPath}[{index}]", upstream, ported, decisions, errors);
        }
    }

    return decisions;
}

static void ApplyEntry(
    JsonElement entry,
    string location,
    List<UpstreamTest> upstream,
    HashSet<string> ported,
    Dictionary<string, string> decisions,
    List<string> errors)
{
    var status = ReadText(entry, "status");
    var pattern = ReadText(entry, "key");
    var reason = ReadText(entry, "reason");
    if (status is not ("omitted" or "todo") || pattern is null || reason is null)
    {
        errors.Add($"{location}: expected status (omitted or todo), key and reason.");
        return;
    }

    var prefix = pattern.EndsWith(" > *", StringComparison.Ordinal) ? pattern[..^1] : null;
    var matches = upstream
        .Where(test => prefix is null ? test.Key == pattern : test.Key.StartsWith(prefix, StringComparison.Ordinal))
        .ToList();

    if (matches.Count is 0)
    {
        errors.Add($"{location}: no upstream test at the pin matches: {pattern}");
    }

    foreach (var test in matches)
    {
        if (ported.Contains(test.Key))
        {
            errors.Add($"{location}: already has a 'Ports:' marker: {test.Key}");
        }

        if (!decisions.TryAdd(test.Key, status))
        {
            errors.Add($"{location}: already has a ledger entry: {test.Key}");
        }
    }
}

static string? ReadText(JsonElement entry, string property)
{
    return entry.ValueKind is JsonValueKind.Object
        && entry.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;
}

static string RenderSummary(List<UpstreamTest> upstream, HashSet<string> ported, Dictionary<string, string> decisions)
{
    var summary = new StringBuilder("file,total,ported,omitted,todo,unreviewed\n");

    foreach (var file in upstream.GroupBy(test => test.File, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
    {
        var portedCount = file.Count(test => ported.Contains(test.Key));
        var omittedCount = file.Count(test => decisions.GetValueOrDefault(test.Key) is "omitted");
        var todoCount = file.Count(test => decisions.GetValueOrDefault(test.Key) is "todo");
        var unreviewed = file.Count(test => !ported.Contains(test.Key) && !decisions.ContainsKey(test.Key));
        summary.Append($"{file.Key},{file.Count()},{portedCount},{omittedCount},{todoCount},{unreviewed}\n");
    }

    return summary.ToString();
}

static void PrintChangesSince(string commit, List<UpstreamTest> current)
{
    RunGit("fetch", "-q", "--depth=1", "--filter=blob:none", UpstreamUrl, commit);
    var paths = RunGit("ls-tree", "-r", "--name-only", commit, "--", "packages/query-core/src", "packages/react-query/src")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries)
        .Where(path => path.Contains(".test.ts", StringComparison.Ordinal));

    var errors = new List<string>();
    var previous = ExtractAll(paths.Select(path => (path, RunGit("show", $"{commit}:{path}"))), errors);
    foreach (var error in errors)
    {
        Console.Error.WriteLine(error);
    }

    var previousKeys = previous.Select(test => test.Key).ToHashSet(StringComparer.Ordinal);
    var currentKeys = current.Select(test => test.Key).ToHashSet(StringComparer.Ordinal);
    foreach (var key in previousKeys.Except(currentKeys).Order(StringComparer.Ordinal))
    {
        Console.WriteLine($"- {key}");
    }

    foreach (var key in currentKeys.Except(previousKeys).Order(StringComparer.Ordinal))
    {
        Console.WriteLine($"+ {key}");
    }
}

static string RunGit(params string[] arguments)
{
    var startInfo = new ProcessStartInfo("git") { RedirectStandardOutput = true };
    startInfo.ArgumentList.Add("-C");
    startInfo.ArgumentList.Add(UpstreamPath);
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start git.");
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return process.ExitCode is 0
        ? output
        : throw new InvalidOperationException($"git {string.Join(' ', arguments)} exited with {process.ExitCode}.");
}

internal sealed record UpstreamTest(string File, string Key);

internal static class Patterns
{
    public static readonly Regex Block = new(
        @"^(?<indent>[ ]*)(?<kind>describe|it|test)(?<modifiers>(?:\.\w+)*)(?:<[^>\n]*>)?\(",
        RegexOptions.Multiline);

    public static readonly Regex RawTest = new(@"(?<![\w.$])(?:it|test)(?:\.\w+)*(?:<[^>]*>)?[(`]");

    public static readonly Regex CommentLine = new(@"^\s*(?://|\*|/\*)");

    public static readonly Regex PortsMarker = new(@"^\s*// Ports: (?<key>.+?)\s*$");
}
