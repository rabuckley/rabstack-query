using RabstackQuery.DevTools;

namespace RabstackQuery.DevTools.Maui;

/// <summary>
/// Main devtools modal page. Shows a tabbed list of queries and mutations
/// with search filtering, sort controls, and status color-coding.
/// </summary>
internal sealed class DevToolsPage : ContentPage
{
    private readonly CacheObserver _observer;
    private readonly QueryClient _queryClient;

    private readonly CollectionView _queryCollectionView;
    private readonly CollectionView _mutationCollectionView;
    private readonly SearchBar _searchBar;
    private readonly Label _hideDisabledLabel;
    private readonly Switch _hideDisabledSwitch;
    private readonly Picker _sortPicker;
    private readonly Button _queriesTab;
    private readonly Button _mutationsTab;
    private readonly BoxView _queriesTabIndicator;
    private readonly BoxView _mutationsTabIndicator;
    private readonly Label _queryCountLabel;
    private readonly Label _mutationCountLabel;
    private readonly View _queryLegend;
    private readonly View _mutationLegend;

    private int _selectedTab;

    public DevToolsPage(CacheObserver observer, QueryClient queryClient)
    {
        _observer = observer;
        _queryClient = queryClient;

        Title = "RabStack Query DevTools";

        var closeItem = new ToolbarItem { Text = "Done" };
        closeItem.Clicked += async (_, _) => await Navigation.PopModalAsync();
        ToolbarItems.Add(closeItem);

        // ── Tab bar ───────────────────────────────────────────────────

        _queryCountLabel = new Label
        {
            FontSize = DevToolsColors.FontCaption,
            VerticalOptions = LayoutOptions.Center,
        };
        _queryCountLabel.SetSecondaryTextColor();

        _mutationCountLabel = new Label
        {
            FontSize = DevToolsColors.FontCaption,
            VerticalOptions = LayoutOptions.Center,
        };
        _mutationCountLabel.SetSecondaryTextColor();

        _queriesTab = new Button
        {
            Text = "Queries",
            Padding = new Thickness(16, 8),
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
        };
        _queriesTab.Clicked += (_, _) => SelectTab(0);

        _mutationsTab = new Button
        {
            Text = "Mutations",
            Padding = new Thickness(16, 8),
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
        };
        _mutationsTab.Clicked += (_, _) => SelectTab(1);

        _queriesTabIndicator = new BoxView
        {
            HeightRequest = 3,
            Color = DevToolsColors.TabIndicator,
        };

        _mutationsTabIndicator = new BoxView
        {
            HeightRequest = 3,
            Color = DevToolsColors.TabIndicator,
        };

        var tabBar = new HorizontalStackLayout
        {
            Spacing = 8,
            Padding = new Thickness(16, 8),
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 4,
                    Children = { _queriesTab, _queriesTabIndicator },
                },
                _queryCountLabel,
                new VerticalStackLayout
                {
                    Spacing = 4,
                    Children = { _mutationsTab, _mutationsTabIndicator },
                },
                _mutationCountLabel,
            },
        };

        // ── Filter bar ───────────────────────────────────────────────
        // Grid layout so the SearchBar gets Star width and doesn't
        // overlap the "Hide disabled" label + switch.

        _searchBar = new SearchBar { Placeholder = "Filter queries..." };
        _searchBar.TextChanged += (_, _) => ApplyFiltersAndSort();

        _hideDisabledLabel = new Label
        {
            Text = "Hide disabled",
            VerticalOptions = LayoutOptions.Center,
            FontSize = DevToolsColors.FontCaption,
        };
        _hideDisabledLabel.SetSecondaryTextColor();

        _hideDisabledSwitch = new Switch { IsToggled = DevToolsState.HideDisabled };
        _hideDisabledSwitch.Toggled += (_, _) =>
        {
            DevToolsState.HideDisabled = _hideDisabledSwitch.IsToggled;
            ApplyFiltersAndSort();
        };

        var filterBar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            ColumnSpacing = 8,
            Padding = new Thickness(16, 0),
        };

        filterBar.Add(_searchBar, 0, 0);
        filterBar.Add(_hideDisabledLabel, 1, 0);
        filterBar.Add(_hideDisabledSwitch, 2, 0);

        // ── Sort picker ──────────────────────────────────────────────

        _sortPicker = new Picker
        {
            Title = "Sort by",
            ItemsSource = new[] { "Hash", "Last Updated", "Status", "Observers" },
            SelectedIndex = DevToolsState.SortOptionIndex,
            HorizontalOptions = LayoutOptions.Start,
            Margin = new Thickness(16, 0),
        };

        _sortPicker.SelectedIndexChanged += (_, _) =>
        {
            DevToolsState.SortOptionIndex = _sortPicker.SelectedIndex;
            ApplyFiltersAndSort();
        };

        // ── Query list ───────────────────────────────────────────────

        var queryEmptyLabel = new Label
        {
            Text = "No queries in the cache",
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            FontSize = DevToolsColors.FontBody,
        };
        queryEmptyLabel.SetSecondaryTextColor();

        _queryCollectionView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = CreateQueryTemplate(),
            EmptyView = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Children = { queryEmptyLabel },
            },
        };

        _queryCollectionView.SelectionChanged += OnQuerySelected;

        // ── Mutation list ────────────────────────────────────────────

        var mutationEmptyLabel = new Label
        {
            Text = "No mutations in the cache",
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            FontSize = DevToolsColors.FontBody,
        };
        mutationEmptyLabel.SetSecondaryTextColor();

        _mutationCollectionView = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = CreateMutationTemplate(),
            IsVisible = false,
            EmptyView = new VerticalStackLayout
            {
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Children = { mutationEmptyLabel },
            },
        };

        _mutationCollectionView.SelectionChanged += OnMutationSelected;

        // ── Status legends (one per tab) ────────────────────────────

        _queryLegend = CreateStatusLegend(DevToolsColors.QueryLegendItems);
        _mutationLegend = CreateStatusLegend(DevToolsColors.MutationLegendItems);
        _mutationLegend.IsVisible = false;

        // ── Layout ───────────────────────────────────────────────────

        var root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Tab bar
                new RowDefinition { Height = GridLength.Auto }, // Filter bar
                new RowDefinition { Height = GridLength.Auto }, // Sort picker
                new RowDefinition { Height = GridLength.Auto }, // Legend
                new RowDefinition { Height = GridLength.Star }, // Lists
            },
        };

        root.Add(tabBar, 0, 0);
        root.Add(filterBar, 0, 1);
        root.Add(_sortPicker, 0, 2);
        root.Add(_queryLegend, 0, 3);
        root.Add(_mutationLegend, 0, 3);
        root.Add(_queryCollectionView, 0, 4);
        root.Add(_mutationCollectionView, 0, 4);

        Content = root;

        // ── Initial state ────────────────────────────────────────────

        _selectedTab = DevToolsState.SelectedTab;
        _observer.SnapshotsChanged += OnSnapshotsChanged;

        SelectTab(_selectedTab);
        ApplyFiltersAndSort();
    }

    private void SelectTab(int tab)
    {
        _selectedTab = tab;
        DevToolsState.SelectedTab = tab;

        var isQueries = tab == 0;
        _queryCollectionView.IsVisible = isQueries;
        _mutationCollectionView.IsVisible = !isQueries;

        // Tab indicator: underline on the selected tab only.
        _queriesTabIndicator.IsVisible = isQueries;
        _mutationsTabIndicator.IsVisible = !isQueries;

        // Tab text color: strong for selected, subdued for unselected.
        _queriesTab.SetThemeColor(Button.TextColorProperty,
            isQueries ? DevToolsColors.TabSelectedLight : DevToolsColors.TabUnselectedLight,
            isQueries ? DevToolsColors.TabSelectedDark : DevToolsColors.TabUnselectedDark);
        _mutationsTab.SetThemeColor(Button.TextColorProperty,
            !isQueries ? DevToolsColors.TabSelectedLight : DevToolsColors.TabUnselectedLight,
            !isQueries ? DevToolsColors.TabSelectedDark : DevToolsColors.TabUnselectedDark);

        _queriesTab.FontAttributes = isQueries ? FontAttributes.Bold : FontAttributes.None;
        _mutationsTab.FontAttributes = !isQueries ? FontAttributes.Bold : FontAttributes.None;

        _searchBar.Placeholder = isQueries ? "Filter queries..." : "Filter mutations...";
        _sortPicker.IsVisible = isQueries;
        _hideDisabledSwitch.IsVisible = isQueries;
        _hideDisabledLabel.IsVisible = isQueries;

        // Show the correct legend for each tab's status vocabulary.
        _queryLegend.IsVisible = isQueries;
        _mutationLegend.IsVisible = !isQueries;

        ApplyFiltersAndSort();
    }

    private void OnSnapshotsChanged()
    {
        ApplyFiltersAndSort();
    }

    private void ApplyFiltersAndSort()
    {
        var searchText = _searchBar.Text ?? "";
        var hideDisabled = _hideDisabledSwitch.IsToggled;
        var sortOption = (SortOption)Math.Max(0, _sortPicker.SelectedIndex);

        if (_selectedTab == 0)
        {
            // Query filtering
            IEnumerable<QueryListItem> filtered = _observer.Queries;

            if (hideDisabled)
                filtered = filtered.Where(q => !q.IsDisabled);

            if (!string.IsNullOrEmpty(searchText))
                filtered = filtered.Where(q =>
                    q.QueryHash.Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    || q.QueryKeyDisplay.Contains(searchText, StringComparison.OrdinalIgnoreCase));

            // Sort
            var sorted = sortOption switch
            {
                SortOption.Hash => filtered.OrderBy(q => q.QueryHash),
                SortOption.LastUpdated => filtered.OrderByDescending(q => q.DataUpdatedAt),
                SortOption.Status => filtered.OrderBy(q => q.DisplayStatus),
                SortOption.Observers => filtered.OrderByDescending(q => q.ObserverCount),
                _ => filtered.OrderBy(q => q.QueryHash),
            };

            var list = sorted.ToList();
            _queryCollectionView.ItemsSource = list;
            _queryCountLabel.Text = $"({list.Count})";
        }
        else
        {
            // Mutation filtering
            IEnumerable<MutationListItem> filtered = _observer.Mutations;

            if (!string.IsNullOrEmpty(searchText))
                filtered = filtered.Where(m =>
                    m.MutationId.ToString().Contains(searchText, StringComparison.OrdinalIgnoreCase)
                    || (m.MutationKeyDisplay?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false));

            var list = filtered.ToList();
            _mutationCollectionView.ItemsSource = list;
            _mutationCountLabel.Text = $"({list.Count})";
        }
    }

    private async void OnQuerySelected(object? sender, SelectionChangedEventArgs args)
    {
        if (args.CurrentSelection.FirstOrDefault() is QueryListItem item)
        {
            _queryCollectionView.SelectedItem = null;
            await Navigation.PushAsync(new QueryDetailPage(item, _observer, _queryClient));
        }
    }

    private async void OnMutationSelected(object? sender, SelectionChangedEventArgs args)
    {
        if (args.CurrentSelection.FirstOrDefault() is MutationListItem item)
        {
            _mutationCollectionView.SelectedItem = null;
            await Navigation.PushAsync(new MutationDetailPage(item));
        }
    }

    // ── DataTemplates ────────────────────────────────────────────────

    private static DataTemplate CreateQueryTemplate()
    {
        return new DataTemplate(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = 4 },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                Padding = new Thickness(8, 10),
                ColumnSpacing = 10,
            };

            // Status color bar
            var colorBar = new BoxView { VerticalOptions = LayoutOptions.Fill };
            colorBar.SetBinding(BoxView.ColorProperty, static (QueryListItem item) => item.StatusColorHex,
                converter: HexToColorConverter.Instance);
            grid.Add(colorBar, 0, 0);

            // Main content: key display + status label
            var mainStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };

            var hashLabel = new Label
            {
                FontFamily = DevToolsColors.MonospaceFont,
                FontSize = DevToolsColors.FontBody,
                FontAttributes = FontAttributes.Bold,
                LineBreakMode = LineBreakMode.TailTruncation,
            };
            hashLabel.SetBinding(Label.TextProperty, static (QueryListItem item) => item.QueryKeyDisplay);
            mainStack.Add(hashLabel);

            var statusRow = new HorizontalStackLayout { Spacing = 6 };

            var statusLabel = new Label
            {
                FontSize = DevToolsColors.FontCaption,
            };
            statusLabel.SetBinding(Label.TextProperty, static (QueryListItem item) => item.StatusLabel);
            statusLabel.SetBinding(Label.TextColorProperty, static (QueryListItem item) => item.StatusColorHex,
                converter: HexToColorConverter.Instance);
            statusRow.Add(statusLabel);

            var hashSmall = new Label
            {
                FontSize = DevToolsColors.FontCaption,
                LineBreakMode = LineBreakMode.TailTruncation,
            };
            hashSmall.SetSecondaryTextColor();
            hashSmall.SetBinding(Label.TextProperty, static (QueryListItem item) => item.QueryHash);
            statusRow.Add(hashSmall);

            mainStack.Add(statusRow);
            grid.Add(mainStack, 1, 0);

            // Observer count
            var observerLabel = new Label
            {
                FontSize = DevToolsColors.FontCaption,
                VerticalOptions = LayoutOptions.Center,
            };
            observerLabel.SetSecondaryTextColor();
            observerLabel.SetBinding(
                Label.TextProperty,
                static (QueryListItem item) => item.ObserverCount,
                stringFormat: "{0} obs");
            grid.Add(observerLabel, 2, 0);

            return grid;
        });
    }

    private static DataTemplate CreateMutationTemplate()
    {
        return new DataTemplate(() =>
        {
            var grid = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition { Width = 4 },
                    new ColumnDefinition { Width = GridLength.Star },
                    new ColumnDefinition { Width = GridLength.Auto },
                },
                Padding = new Thickness(8, 10),
                ColumnSpacing = 10,
            };

            // Status color bar
            var colorBar = new BoxView { VerticalOptions = LayoutOptions.Fill };
            colorBar.SetBinding(BoxView.ColorProperty, static (MutationListItem item) => item.StatusColorHex,
                converter: HexToColorConverter.Instance);
            grid.Add(colorBar, 0, 0);

            // Main content
            var mainStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };

            var idLabel = new Label
            {
                FontFamily = DevToolsColors.MonospaceFont,
                FontSize = DevToolsColors.FontBody,
                FontAttributes = FontAttributes.Bold,
            };
            idLabel.SetBinding(
                Label.TextProperty,
                static (MutationListItem item) => item.MutationId,
                stringFormat: "Mutation #{0}");
            mainStack.Add(idLabel);

            var statusLabel = new Label
            {
                FontSize = DevToolsColors.FontCaption,
            };
            statusLabel.SetBinding(Label.TextProperty, static (MutationListItem item) => item.StatusLabel);
            statusLabel.SetBinding(Label.TextColorProperty, static (MutationListItem item) => item.StatusColorHex,
                converter: HexToColorConverter.Instance);
            mainStack.Add(statusLabel);

            var keyLabel = new Label
            {
                FontSize = DevToolsColors.FontCaption,
                LineBreakMode = LineBreakMode.TailTruncation,
            };
            keyLabel.SetSecondaryTextColor();
            keyLabel.SetBinding(Label.TextProperty, static (MutationListItem item) => item.MutationKeyDisplay);
            mainStack.Add(keyLabel);

            grid.Add(mainStack, 1, 0);

            // Observers indicator
            var observerLabel = new Label
            {
                FontSize = DevToolsColors.FontCaption,
                VerticalOptions = LayoutOptions.Center,
            };
            observerLabel.SetSecondaryTextColor();
            observerLabel.SetBinding(
                VisualElement.IsVisibleProperty,
                static (MutationListItem item) => item.HasObservers);
            observerLabel.Text = "observed";
            grid.Add(observerLabel, 2, 0);

            return grid;
        });
    }

    private static View CreateStatusLegend(IReadOnlyList<(string Label, Color Color)> items)
    {
        var layout = new HorizontalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 4),
        };

        foreach (var (label, color) in items)
        {
            var legendLabel = new Label
            {
                Text = label,
                FontSize = DevToolsColors.FontCaption,
                VerticalOptions = LayoutOptions.Center,
            };
            legendLabel.SetSecondaryTextColor();

            layout.Add(new HorizontalStackLayout
            {
                Spacing = 4,
                Children =
                {
                    new BoxView
                    {
                        Color = color,
                        WidthRequest = 10,
                        HeightRequest = 10,
                        VerticalOptions = LayoutOptions.Center,
                    },
                    legendLabel,
                },
            });
        }

        return layout;
    }
}
