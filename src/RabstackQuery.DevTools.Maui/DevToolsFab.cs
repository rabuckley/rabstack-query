using Microsoft.Maui.Graphics;

namespace RabstackQuery.DevTools.Maui;

/// <summary>
/// Floating action button drawn as a <see cref="IWindowOverlayElement"/>.
/// Renders a semi-transparent circle with "RQ" text and a red badge showing
/// the current query count. Hit-testing is circular so taps outside the FAB
/// pass through to the underlying app content.
/// </summary>
internal sealed class DevToolsFab : IWindowOverlayElement
{
    private const float FabSize = 48f;
    private const float FabRightMargin = 16f;
    private const float FabBottomMargin = 48f;
    private const float BadgeRadius = 10f;

    private float _centerX;
    private float _centerY;

    /// <summary>
    /// When false, the FAB is hidden (not drawn and not hittable).
    /// Set to false while the devtools modal is open.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    public int QueryCount { get; set; }

    /// <summary>
    /// Number of queries currently in error state. When positive the badge
    /// turns red; otherwise it uses a neutral blue.
    /// </summary>
    public int ErrorCount { get; set; }

    public bool Contains(Point point)
    {
        if (!IsVisible) return false;

        var dx = point.X - _centerX;
        var dy = point.Y - _centerY;
        var radius = FabSize / 2.0;
        return dx * dx + dy * dy <= radius * radius;
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (!IsVisible) return;

        // Position: bottom-right corner with margins.
        _centerX = dirtyRect.Width - FabSize / 2 - FabRightMargin;
        _centerY = dirtyRect.Height - FabSize / 2 - FabBottomMargin;

        // Circle background — theme-aware since ICanvas can't use SetAppThemeColor.
        var isDark = Application.Current?.RequestedTheme is AppTheme.Dark;
        canvas.FillColor = isDark ? DevToolsColors.FabBackgroundDark : DevToolsColors.FabBackgroundLight;
        canvas.FillCircle(_centerX, _centerY, FabSize / 2);

        // "RQ" label
        canvas.FontColor = Colors.White;
        canvas.FontSize = 16;
        canvas.DrawString(
            "RQ",
            _centerX - FabSize / 2, _centerY - FabSize / 2,
            FabSize, FabSize,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        // Red badge with query count
        if (QueryCount > 0)
        {
            var badgeCenterX = _centerX + FabSize / 2 - BadgeRadius + 2;
            var badgeCenterY = _centerY - FabSize / 2 + BadgeRadius - 2;

            // Blue badge for neutral count; red only when queries are errored.
            canvas.FillColor = ErrorCount > 0 ? DevToolsColors.Error : DevToolsColors.Fetching;
            canvas.FillCircle(badgeCenterX, badgeCenterY, BadgeRadius);

            canvas.FontColor = Colors.White;
            canvas.FontSize = 10;
            var text = QueryCount > 99 ? "99+" : QueryCount.ToString();
            canvas.DrawString(
                text,
                badgeCenterX - BadgeRadius, badgeCenterY - BadgeRadius,
                BadgeRadius * 2, BadgeRadius * 2,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
