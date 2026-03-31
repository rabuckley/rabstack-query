namespace RabstackQuery;

public sealed class FocusManagerTests
{
    [Fact]
    public void IsFocused_ShouldReturnTrue_ByDefault()
    {
        // Arrange
        var focusManager = new FocusManager();

        // Act
        var isFocused = focusManager.IsFocused;

        // Assert
        Assert.True(isFocused);
    }

    [Fact]
    public void SetFocused_ShouldUpdateIsFocusedProperty_WhenCalledWithTrue()
    {
        // Arrange
        var focusManager = new FocusManager();
        var callCount = 0;
        EventHandler handler = (sender, args) => callCount++;
        focusManager.FocusChanged += handler;

        // Set to false first to ensure we can test setting to true
        focusManager.SetFocused(false);
        callCount = 0; // Reset count after initial setup

        // Act
        focusManager.SetFocused(true);

        // Assert
        Assert.True(focusManager.IsFocused);
    }

    [Fact]
    public void SetFocused_ShouldUpdateIsFocusedProperty_WhenCalledWithFalse()
    {
        // Arrange
        var focusManager = new FocusManager();
        var callCount = 0;
        EventHandler handler = (sender, args) => callCount++;
        focusManager.FocusChanged += handler;

        // Act
        focusManager.SetFocused(false);

        // Assert
        Assert.False(focusManager.IsFocused);
    }

    [Fact]
    public void FocusChanged_ShouldFireEvent_WhenSetFocusedChangesValue()
    {
        // Arrange
        var focusManager = new FocusManager();
        var callCount = 0;
        EventHandler handler = (sender, args) => callCount++;
        focusManager.FocusChanged += handler;

        // Set to false first to ensure we start from a known state
        focusManager.SetFocused(false);
        callCount = 0; // Reset count after initial setup

        // Act
        focusManager.SetFocused(true);

        // Assert
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void FocusChanged_ShouldNotFireEvent_WhenSetFocusedCalledWithSameValueTwice()
    {
        // Arrange
        var focusManager = new FocusManager();
        var callCount = 0;
        EventHandler handler = (sender, args) => callCount++;
        focusManager.FocusChanged += handler;

        // Act - Set to false twice
        focusManager.SetFocused(false);
        focusManager.SetFocused(false);

        // Assert - Should only fire once
        Assert.Equal(1, callCount);

        // Reset count
        callCount = 0;

        // Act - Set to true twice
        focusManager.SetFocused(true);
        focusManager.SetFocused(true);

        // Assert - Should only fire once
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void FocusChanged_ShouldFireEventMultipleTimes_WhenValueChanges()
    {
        // Arrange
        var focusManager = new FocusManager();
        var callCount = 0;
        var focusedValues = new List<bool>();

        EventHandler handler = (sender, args) =>
        {
            callCount++;
            focusedValues.Add(focusManager.IsFocused);
        };

        focusManager.FocusChanged += handler;

        // Act
        focusManager.SetFocused(false); // Event 1: false
        focusManager.SetFocused(false); // No event (same value)
        focusManager.SetFocused(true); // Event 2: true
        focusManager.SetFocused(true); // No event (same value)
        focusManager.SetFocused(false); // Event 3: false

        // Assert
        Assert.Equal(3, callCount);
        Assert.Equal([false, true, false], focusedValues);
    }

    [Fact]
    public void FocusChanged_ShouldNotFireEvent_WhenNoSubscribers()
    {
        // Arrange
        var focusManager = new FocusManager();

        // Act - Should not throw even with no subscribers
        focusManager.SetFocused(false);
        focusManager.SetFocused(true);

        // Assert - No exception thrown
        Assert.True(focusManager.IsFocused);
    }

    [Fact]
    public void FocusChanged_ShouldAllowMultipleSubscribers()
    {
        // Arrange
        var focusManager = new FocusManager();
        var callCount1 = 0;
        var callCount2 = 0;
        EventHandler handler1 = (sender, args) => callCount1++;
        EventHandler handler2 = (sender, args) => callCount2++;
        focusManager.FocusChanged += handler1;
        focusManager.FocusChanged += handler2;

        // Act
        focusManager.SetFocused(false);

        // Assert
        Assert.Equal(1, callCount1);
        Assert.Equal(1, callCount2);
    }

    [Fact]
    public void FocusChanged_ShouldNotFireEvent_AfterUnsubscribe()
    {
        // Arrange
        var focusManager = new FocusManager();
        var callCount = 0;
        EventHandler handler = (sender, args) => callCount++;

        focusManager.FocusChanged += handler;
        focusManager.SetFocused(false);
        Assert.Equal(1, callCount);

        // Act - Unsubscribe
        focusManager.FocusChanged -= handler;
        focusManager.SetFocused(true);

        // Assert - Count should not increase
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Instance_ShouldReturnSameInstance()
    {
        // Act
        var instance1 = FocusManager.Instance;
        var instance2 = FocusManager.Instance;

        // Assert
        Assert.Same(instance1, instance2);
    }
}
