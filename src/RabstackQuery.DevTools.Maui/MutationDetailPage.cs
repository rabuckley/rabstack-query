using RabstackQuery.DevTools;

namespace RabstackQuery.DevTools.Maui;

/// <summary>
/// Detail view for a single mutation. Shows identity, status, data/variables,
/// and error information in themed card sections.
/// </summary>
internal sealed class MutationDetailPage : ContentPage
{
    public MutationDetailPage(MutationListItem item)
    {
        Title = "Mutation Detail";

        var scrollView = new ScrollView();
        var stack = new VerticalStackLayout { Padding = new Thickness(16), Spacing = 16 };

        // ── Identity ──────────────────────────────────────────────────

        var identityStack = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                CreateField("Mutation ID", $"#{item.MutationId}"),
                CreateField("Mutation Key", item.MutationKeyDisplay ?? "(none)"),
            },
        };
        stack.Add(CreateCard(identityStack));

        // ── Status ────────────────────────────────────────────────────

        var statusGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
            },
            ColumnSpacing = 16,
            RowSpacing = 8,
        };

        var statusColor = DevToolsColors.ForMutationStatus(item.Status);

        statusGrid.Add(CreateField("Status", item.StatusLabel, statusColor), 0, 0);
        statusGrid.Add(CreateField("Is Paused", item.IsPaused ? "Yes" : "No"), 1, 0);
        statusGrid.Add(CreateField("Has Observers", item.HasObservers ? "Yes" : "No"), 0, 1);

        stack.Add(CreateCard(statusGrid));

        // ── Data ───────────────────────────────────────────────────────

        var dataStack = new VerticalStackLayout { Spacing = 12 };

        dataStack.Add(CreateField("Data", FormatDataDisplay(item.DataDisplay)));
        dataStack.Add(CreateField("Variables", FormatDataDisplay(item.VariablesDisplay)));
        dataStack.Add(CreateField("Submitted At", item.SubmittedAt > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(item.SubmittedAt).ToString("O")
            : "(not submitted)"));
        dataStack.Add(CreateField("Failure Count", item.FailureCount.ToString()));

        stack.Add(CreateCard(dataStack));

        // ── Error (if present) ────────────────────────────────────────

        if (item.ErrorDisplay is not null)
        {
            stack.Add(CreateCard(
                CreateField("Error", item.ErrorDisplay, DevToolsColors.Error),
                borderColor: DevToolsColors.Error));
        }

        scrollView.Content = stack;
        Content = scrollView;
    }

    /// <summary>
    /// Wraps content in a themed card border matching the example app's convention.
    /// </summary>
    private static Border CreateCard(View content, Color? borderColor = null)
    {
        var card = new Border
        {
            Content = content,
            Padding = new Thickness(16),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            StrokeThickness = 1,
        };

        if (borderColor is not null)
        {
            card.Stroke = borderColor;
        }
        else
        {
            card.SetThemeColor(Border.StrokeProperty,
                DevToolsColors.BorderLight, DevToolsColors.BorderDark);
        }

        return card;
    }

    private static View CreateField(string label, string value, Color? valueColor = null)
    {
        var labelView = new Label
        {
            Text = label,
            FontSize = DevToolsColors.FontCaption,
        };
        labelView.SetSecondaryTextColor();

        var valueView = new Label
        {
            Text = value,
            FontSize = DevToolsColors.FontBody,
            FontAttributes = FontAttributes.Bold,
        };

        if (valueColor is not null)
        {
            valueView.TextColor = valueColor;
        }

        return new VerticalStackLayout { Spacing = 2, Children = { labelView, valueView } };
    }

    /// <summary>
    /// Appends a hint when ToString() produces an unhelpful type name.
    /// </summary>
    private static string FormatDataDisplay(string? display)
    {
        if (display is null) return "(null)";

        if (display.StartsWith("System.", StringComparison.Ordinal) || display.Contains('`'))
        {
            return display + "\n\n(configure DevToolsOptions.DataFormatter for formatted output)";
        }

        return display;
    }
}
