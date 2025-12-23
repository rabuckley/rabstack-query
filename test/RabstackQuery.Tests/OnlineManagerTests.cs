namespace RabstackQuery;

/// <summary>
/// Tests for OnlineManager.
/// Ports tests from TanStack's onlineManager.test.tsx, adapted for the C# event-based API.
/// Browser-specific tests (navigator, window.addEventListener) are omitted; those
/// concepts don't exist in the C# implementation.
/// </summary>
public sealed class OnlineManagerTests
{
    [Fact]
    public void IsOnline_ShouldReturnTrue_ByDefault()
    {
        // Arrange & Act
        var manager = new OnlineManager();

        // Assert
        Assert.True(manager.IsOnline);
    }

    [Fact]
    public void SetOnline_ShouldUpdateIsOnline()
    {
        // Arrange
        var manager = new OnlineManager();

        // Act
        manager.SetOnline(false);

        // Assert
        Assert.False(manager.IsOnline);

        // Act
        manager.SetOnline(true);

        // Assert
        Assert.True(manager.IsOnline);
    }

    [Fact]
    public void SetOnline_ShouldCallListeners_WhenValueChanges()
    {
        // Arrange
        var manager = new OnlineManager();
        var eventRaised = false;

        EventHandler handler = (sender, args) =>
        {
            eventRaised = true;
        };

        manager.OnlineChanged += handler;

        // Act
        manager.SetOnline(false);

        // Assert
        Assert.True(eventRaised);
    }

    /// <summary>
    /// Mirrors TanStack: "should call listeners when setOnline is called"
    /// — setOnline(false) twice only notifies once, then setOnline(true) twice
    /// only notifies once.
    /// </summary>
    [Fact]
    public void SetOnline_ShouldNotFireEvent_WhenSettingSameValueTwice()
    {
        // Arrange
        var manager = new OnlineManager();
        var eventCount = 0;

        EventHandler handler = (sender, args) =>
        {
            eventCount++;
        };

        manager.OnlineChanged += handler;

        // Act - set to false (should fire event)
        manager.SetOnline(false);
        manager.SetOnline(false);

        // Assert - only one notification
        Assert.Equal(1, eventCount);

        // Act - set to true (should fire event)
        manager.SetOnline(true);
        manager.SetOnline(true);

        // Assert - total of two notifications
        Assert.Equal(2, eventCount);
    }

    [Fact]
    public void SetOnline_ShouldAllowMultipleSubscribers()
    {
        // Arrange
        var manager = new OnlineManager();
        var handler1Count = 0;
        var handler2Count = 0;

        EventHandler handler1 = (sender, args) => handler1Count++;
        EventHandler handler2 = (sender, args) => handler2Count++;

        manager.OnlineChanged += handler1;
        manager.OnlineChanged += handler2;

        // Act
        manager.SetOnline(false);

        // Assert — both handlers should fire
        Assert.Equal(1, handler1Count);
        Assert.Equal(1, handler2Count);
    }

    [Fact]
    public void SetOnline_ShouldNotNotifyAfterUnsubscribe()
    {
        // Arrange
        var manager = new OnlineManager();
        var callCount = 0;
        EventHandler handler = (sender, args) => callCount++;

        manager.OnlineChanged += handler;
        manager.SetOnline(false);
        Assert.Equal(1, callCount);

        // Act — unsubscribe
        manager.OnlineChanged -= handler;
        manager.SetOnline(true);

        // Assert — count should not have increased
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void SetOnline_ShouldNotThrow_WhenNoSubscribers()
    {
        // Arrange
        var manager = new OnlineManager();

        // Act — should not throw with no event handlers attached
        manager.SetOnline(false);
        manager.SetOnline(true);

        // Assert — state is correct
        Assert.True(manager.IsOnline);
    }

    /// <summary>
    /// Mirrors TanStack: verifies multiple transitions fire the correct number of events.
    /// </summary>
    [Fact]
    public void SetOnline_ShouldTrackMultipleTransitions()
    {
        // Arrange
        var manager = new OnlineManager();
        var stateHistory = new List<bool>();

        EventHandler handler = (sender, args) =>
        {
            stateHistory.Add(manager.IsOnline);
        };

        manager.OnlineChanged += handler;

        // Act
        manager.SetOnline(false); // -> offline
        manager.SetOnline(false); // no-op
        manager.SetOnline(true);  // -> online
        manager.SetOnline(true);  // no-op
        manager.SetOnline(false); // -> offline

        // Assert
        Assert.Equal([false, true, false], stateHistory);
    }

    [Fact]
    public void Instance_ShouldReturnSameInstance()
    {
        // Act
        var instance1 = OnlineManager.Instance;
        var instance2 = OnlineManager.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }
}
