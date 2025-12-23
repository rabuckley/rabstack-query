using RabstackQuery.DevTools;

namespace RabstackQuery.DevTools.Maui;

/// <summary>
/// Extension methods to attach the RabStack Query DevTools overlay to a MAUI window.
/// </summary>
public static class DevToolsExtensions
{
    /// <summary>
    /// Adds a floating debug overlay to the window that provides real-time
    /// visibility into the <see cref="QueryClient"/>'s cache state.
    /// <para>
    /// A small "RQ" floating action button appears in the bottom-right corner
    /// with a badge showing the query count. Tapping it opens a modal page
    /// with the full query/mutation list, search, sort, and detail views.
    /// </para>
    /// <example>
    /// <code>
    /// // In App.CreateWindow():
    /// #if DEBUG
    /// window.UseRabstackQueryDevTools(queryClient, new DevToolsOptions
    /// {
    ///     DataFormatter = data =&gt; JsonSerializer.Serialize(data, AppJsonContext.Default.Options)
    /// });
    /// #endif
    /// </code>
    /// </example>
    /// </summary>
    public static void UseRabstackQueryDevTools(
        this Window window,
        QueryClient queryClient,
        DevToolsOptions? options = null)
    {
        options ??= new DevToolsOptions();

        var observer = new CacheObserver(queryClient, options);
        var fab = new DevToolsFab { QueryCount = observer.QueryCount };

        var overlay = new WindowOverlay(window)
        {
            EnableDrawableTouchHandling = true,
        };
        overlay.AddWindowElement(fab);

        // Update FAB badge and redraw when cache state changes.
        observer.SnapshotsChanged += () =>
        {
            fab.QueryCount = observer.QueryCount;
            fab.ErrorCount = observer.Queries.Count(q => q.DisplayStatus is QueryDisplayStatus.Error);
            overlay.Invalidate();
        };

        // Tap on the FAB opens the devtools modal.
        overlay.Tapped += (_, args) =>
        {
            if (!fab.Contains(args.Point)) return;

            fab.IsVisible = false;
            overlay.Invalidate();

            var page = new DevToolsPage(observer, queryClient);
            var nav = new NavigationPage(page);

            // Re-show FAB when the modal is dismissed (close button or swipe-down).
            EventHandler<ModalPoppedEventArgs>? handler = null;
            handler = (_, poppedArgs) =>
            {
                if (poppedArgs.Modal != nav) return;
                Application.Current!.ModalPopped -= handler;
                fab.IsVisible = true;
                overlay.Invalidate();
            };
            Application.Current!.ModalPopped += handler;

            _ = window.Page!.Navigation.PushModalAsync(nav);
        };

        // Defer AddOverlay until the window is ready. WinUI can raise Created
        // before the window is fully activated, which prevents the overlay from
        // drawing reliably. Other platforms can attach as soon as the
        // MauiContext exists.
        EventHandler? createdHandler = null;
        EventHandler? activatedHandler = null;
        var windowActivated = false;

        void TryAttachOverlay()
        {
            if (window.Handler?.MauiContext is null)
            {
                return;
            }

            if (OperatingSystem.IsWindows() && !windowActivated)
            {
                return;
            }

            window.Created -= createdHandler;
            window.Activated -= activatedHandler;
            window.AddOverlay(overlay);
            overlay.Invalidate();
        }

        createdHandler = (_, _) => TryAttachOverlay();
        activatedHandler = (_, _) =>
        {
            windowActivated = true;
            TryAttachOverlay();
        };

        window.Created += createdHandler;
        window.Activated += activatedHandler;

        // Dispose the observer when the window is destroyed.
        window.Destroying += (_, _) => observer.Dispose();
    }
}
