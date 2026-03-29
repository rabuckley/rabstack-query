using System.Text.Json;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace RabstackQuery.DevTools.Blazor.Components;

/// <summary>
/// Recursive tree viewer that renders JSON data as an expandable/collapsible tree.
/// Pass <see cref="Data"/> (a serialized string) at the root level. The component
/// tries to parse it as JSON; on success it renders an interactive tree, otherwise
/// it falls back to a plain <c>&lt;pre&gt;</c> block.
/// </summary>
public partial class DataExplorer : ComponentBase
{
    /// <summary>Serialized data string. Used at the root level — parsed as JSON if possible.</summary>
    [Parameter] public string? Data { get; set; }

    /// <summary>Pre-parsed element for recursive children. Takes precedence over <see cref="Data"/>.</summary>
    [Parameter] public JsonElement? Element { get; set; }

    [Parameter] public string Label { get; set; } = "Data";
    [Parameter] public bool DefaultExpanded { get; set; } = true;
    [Parameter] public int Depth { get; set; }

    private JsonElement? _element;
    private bool _expanded;
    private bool _initialized;
    private bool _isExpandable;
    private string _typeInfo = "";
    private string _displayValue = "";
    private string _valueCssClass = "";
    private readonly List<(string Key, JsonElement Value)> _children = [];

    protected override void OnParametersSet()
    {
        _element = Element;

        // Root-level usage: parse the serialized string as JSON.
        if (_element is null && Data is { Length: > 0 })
        {
            try
            {
                using var doc = JsonDocument.Parse(Data);
                _element = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                _element = null;
            }
        }

        _children.Clear();
        _isExpandable = false;
        _typeInfo = "";
        _displayValue = "";
        _valueCssClass = "";

        if (_element is { } element)
        {
            Analyze(element);
        }

        // Only set initial expand state once; user toggling persists across re-renders.
        if (!_initialized)
        {
            _expanded = DefaultExpanded && _isExpandable;
            _initialized = true;
        }
    }

    private void Analyze(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                _isExpandable = true;
                var count = 0;
                foreach (var property in element.EnumerateObject())
                {
                    _children.Add((property.Name, property.Value));
                    count++;
                }
                _typeInfo = $"{{ {count} {(count == 1 ? "item" : "items")} }}";
                break;
            }
            case JsonValueKind.Array:
            {
                _isExpandable = true;
                var length = element.GetArrayLength();
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    _children.Add((index.ToString(), item));
                    index++;
                }
                _typeInfo = $"[ {length} {(length == 1 ? "item" : "items")} ]";
                break;
            }
            case JsonValueKind.String:
                _displayValue = $"\"{element.GetString()}\"";
                _valueCssClass = "explorer-value--string";
                break;
            case JsonValueKind.Number:
                _displayValue = element.GetRawText();
                _valueCssClass = "explorer-value--number";
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                _displayValue = element.ValueKind is JsonValueKind.True ? "true" : "false";
                _valueCssClass = "explorer-value--boolean";
                break;
            case JsonValueKind.Null:
                _displayValue = "null";
                _valueCssClass = "explorer-value--null";
                break;
            default:
                _displayValue = element.GetRawText();
                break;
        }
    }

    private void Toggle() => _expanded = !_expanded;

    private void OnKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
        {
            Toggle();
        }
    }
}
