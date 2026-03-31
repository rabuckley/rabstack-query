using RabstackQuery.DevTools;

using static RabstackQuery.DevTools.Maui.DevToolsTracing;

namespace RabstackQuery.DevTools.Maui;

/// <summary>
/// Detail view for a single query. Shows full state (key, hash, status,
/// data, error) and provides action buttons (refetch, invalidate, reset, remove).
/// </summary>
internal sealed class QueryDetailPage : ContentPage
{
    public QueryDetailPage(QueryListItem item, CacheObserver observer, QueryClient queryClient)
    {
        Title = "Query Detail";

        var scrollView = new ScrollView();
        var stack = new VerticalStackLayout { Padding = new Thickness(16), Spacing = 16 };

        // ── Identity ──────────────────────────────────────────────────

        stack.Add(CreateSection("Query Key", item.QueryKeyDisplay, isMonospace: true));
        stack.Add(CreateSection("Query Hash", item.QueryHash, isMonospace: true));

        // ── Status ────────────────────────────────────────────────────

        var statusGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
            },
            ColumnSpacing = 16,
            RowSpacing = 8,
        };

        var statusColor = DevToolsColors.ForQueryStatus(item.DisplayStatus);

        statusGrid.Add(CreateField("Status", item.StatusLabel, statusColor), 0, 0);
        statusGrid.Add(CreateField("Fetch Status", item.FetchStatus.ToString()), 1, 0);
        statusGrid.Add(CreateField("Observers", item.ObserverCount.ToString()), 0, 1);
        statusGrid.Add(CreateField("Last Updated", FormatRelativeTime(item.DataUpdatedAt)), 1, 1);
        statusGrid.Add(CreateField("Invalidated", item.IsInvalidated ? "Yes" : "No"), 0, 2);
        statusGrid.Add(CreateField("Failure Count", item.FetchFailureCount.ToString()), 1, 2);

        stack.Add(CreateCard(statusGrid));

        // ── Data ──────────────────────────────────────────────────────

        var dataText = item.DataDisplay;

        // When ToString() produces an unhelpful type name, hint that DataFormatter
        // should be configured for human-readable output.
        if (dataText.StartsWith("System.", StringComparison.Ordinal) || dataText.Contains('`'))
        {
            dataText += "\n\n(configure DevToolsOptions.DataFormatter for formatted output)";
        }

        stack.Add(CreateSection("Data", dataText, isMonospace: true, isScrollable: true));

        // ── Error (if present) ────────────────────────────────────────

        if (item.ErrorDisplay is not null)
        {
            stack.Add(CreateSection("Error", item.ErrorDisplay, isMonospace: true, isScrollable: true,
                borderColor: DevToolsColors.Error));
        }

        // ── Actions ───────────────────────────────────────────────────

        var actionsLayout = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
        };

        actionsLayout.Add(CreateActionButton("Refetch", async btn =>
        {
            btn.IsEnabled = false;
            using var activity = StartQueryAction(
                DevToolsActionType.Refetch, item.QueryHash, item.QueryKeyDisplay);
            try
            {
                var query = observer.FindQueryByHash(item.QueryHash);
                if (query is not null) await query.Fetch();
            }
            catch { /* Query errors are expected and tracked in query state */ }
            finally { btn.IsEnabled = true; }
        }));

        actionsLayout.Add(CreateActionButton("Invalidate", async _ =>
        {
            var query = observer.FindQueryByHash(item.QueryHash);
            if (query?.QueryKey is not { } key) return;

            using var activity = StartQueryAction(
                DevToolsActionType.Invalidate, item.QueryHash, item.QueryKeyDisplay);

            await queryClient.InvalidateQueriesAsync(new InvalidateQueryFilters { QueryKey = key });
        }));

        actionsLayout.Add(CreateActionButton("Reset", _ =>
        {
            var query = observer.FindQueryByHash(item.QueryHash);
            if (query?.QueryKey is not { } key) return;

            using var activity = StartQueryAction(
                DevToolsActionType.Reset, item.QueryHash, item.QueryKeyDisplay);

            queryClient.ResetQueries(new QueryFilters { QueryKey = key });
        }));

        actionsLayout.Add(CreateActionButton("Remove", _ =>
        {
            var query = observer.FindQueryByHash(item.QueryHash);
            if (query?.QueryKey is not { } key) return;

            using var activity = StartQueryAction(
                DevToolsActionType.Remove, item.QueryHash, item.QueryKeyDisplay);

            queryClient.RemoveQueries(new QueryFilters { QueryKey = key });
            Navigation.PopAsync();
        }));

        var isTriggeredLoading = item.IsDevToolsTriggered && item.Status is QueryStatus.Pending;
        var triggerLoadingBtn = CreateActionButton(
            isTriggeredLoading ? "Restore Loading" : "Trigger Loading",
            async btn =>
            {
                btn.IsEnabled = false;
                using var activity = StartQueryAction(
                    isTriggeredLoading ? DevToolsActionType.RestoreLoading : DevToolsActionType.TriggerLoading,
                    item.QueryHash, item.QueryKeyDisplay);
                try
                {
                    if (isTriggeredLoading)
                        await observer.Restore(item.QueryHash);
                    else
                        observer.TriggerLoading(item.QueryHash);
                }
                finally { btn.IsEnabled = true; }
            });
        actionsLayout.Add(triggerLoadingBtn);

        var isTriggeredError = item.IsDevToolsTriggered && item.Status is QueryStatus.Errored;
        var triggerErrorBtn = CreateActionButton(
            isTriggeredError ? "Restore Error" : "Trigger Error",
            async btn =>
            {
                btn.IsEnabled = false;
                using var activity = StartQueryAction(
                    isTriggeredError ? DevToolsActionType.RestoreError : DevToolsActionType.TriggerError,
                    item.QueryHash, item.QueryKeyDisplay);
                try
                {
                    if (isTriggeredError)
                        await observer.Restore(item.QueryHash);
                    else
                        observer.TriggerError(item.QueryHash, new Exception("Triggered from devtools"));
                }
                finally { btn.IsEnabled = true; }
            });
        actionsLayout.Add(triggerErrorBtn);

        stack.Add(actionsLayout);

        scrollView.Content = stack;
        Content = scrollView;
    }

    private static View CreateSection(string title, string content,
        bool isMonospace = false, bool isScrollable = false, Color? borderColor = null)
    {
        var titleLabel = new Label
        {
            Text = title,
            FontAttributes = FontAttributes.Bold,
            FontSize = DevToolsColors.FontSmall,
        };

        var contentLabel = new Label
        {
            Text = content,
            FontSize = DevToolsColors.FontSmall,
            LineBreakMode = LineBreakMode.WordWrap,
        };

        if (isMonospace)
        {
            contentLabel.FontFamily = DevToolsColors.MonospaceFont;
        }

        View body = contentLabel;
        if (isScrollable)
        {
            body = new ScrollView
            {
                Content = contentLabel,
                MaximumHeightRequest = 200,
                Orientation = ScrollOrientation.Vertical,
            };
        }

        var border = new Border
        {
            Content = body,
            Padding = new Thickness(12),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            StrokeThickness = 1,
        };

        if (borderColor is not null)
        {
            border.Stroke = borderColor;
        }
        else
        {
            border.SetThemeColor(Border.StrokeProperty,
                DevToolsColors.BorderLight, DevToolsColors.BorderDark);
        }

        return new VerticalStackLayout { Spacing = 4, Children = { titleLabel, border } };
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

    private static Button CreateActionButton(string text, Action<Button> onClicked)
    {
        var button = new Button
        {
            Text = text,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(16, 8),
            FontSize = DevToolsColors.FontBody,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 1,
            CornerRadius = 8,
        };

        button.SetThemeColor(Button.BorderColorProperty,
            DevToolsColors.BorderLight, DevToolsColors.BorderDark);
        button.SetThemeColor(Button.TextColorProperty,
            DevToolsColors.TabSelectedLight, DevToolsColors.TabSelectedDark);

        button.Clicked += (_, _) => onClicked(button);
        return button;
    }

    // Overload for async actions (Refetch)
    private static Button CreateActionButton(string text, Func<Button, Task> onClicked)
    {
        var button = new Button
        {
            Text = text,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(16, 8),
            FontSize = DevToolsColors.FontBody,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 1,
            CornerRadius = 8,
        };

        button.SetThemeColor(Button.BorderColorProperty,
            DevToolsColors.BorderLight, DevToolsColors.BorderDark);
        button.SetThemeColor(Button.TextColorProperty,
            DevToolsColors.TabSelectedLight, DevToolsColors.TabSelectedDark);

        button.Clicked += async (_, _) => await onClicked(button);
        return button;
    }

    private static string FormatRelativeTime(long unixMs)
    {
        if (unixMs == 0) return "never";

        var elapsed = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
        return elapsed.TotalSeconds switch
        {
            < 5 => "just now",
            < 60 => $"{(int)elapsed.TotalSeconds}s ago",
            < 3600 => $"{(int)elapsed.TotalMinutes}m ago",
            < 86400 => $"{(int)elapsed.TotalHours}h ago",
            _ => $"{(int)elapsed.TotalDays}d ago",
        };
    }
}
