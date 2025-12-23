# Session Notes

## Code Quality Issues

- XAML `FontSize="Caption"` uses `NamedSize` which is obsolete in .NET 10.
  The code-behind usage in `TodosPage.cs` has been replaced with a constant,
  but XAML string values like `FontSize="Caption"` still compile and work.
  Migrate when the replacement API is documented.
