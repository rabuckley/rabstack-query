namespace RabstackQuery;

public sealed class DefaultQueryKeyHasherTests
{
    [Fact]
    public void Hash_ShouldReturnSameHash_ForSameInput()
    {
        // Arrange
        var hasher = new DefaultQueryKeyHasher();
        QueryKey key = ["todos"];

        // Act
        var hash1 = hasher.HashQueryKey(key);
        var hash2 = hasher.HashQueryKey(key);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_ShouldReturnDifferentHash_ForDifferentInput()
    {
        // Arrange
        var hasher = new DefaultQueryKeyHasher();
        QueryKey key1 = ["todos"];
        QueryKey key2 = ["posts"];

        // Act
        var hash1 = hasher.HashQueryKey(key1);
        var hash2 = hasher.HashQueryKey(key2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_WithEquivalentAnonymousObjects_ShouldReturnSameHash()
    {
        // Arrange
        var hasher = new DefaultQueryKeyHasher();

        QueryKey key1 =
        [
            "todos", new
            {
                a = 1, b = 2
            }
        ];

        QueryKey key2 =
        [
            "todos", new
            {
                b = 2, a = 1
            }
        ];

        // Act
        var hash1 = hasher.HashQueryKey(key1);
        var hash2 = hasher.HashQueryKey(key2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_WithEqivalentDictionaries_ShouldReturnSameHash()
    {
        // Arrange
        var hasher = new DefaultQueryKeyHasher();

        QueryKey key1 =
        [
            "todos", new Dictionary<string, object>
            {
                {
                    "a", 1
                },
                {
                    "b", 2
                }
            }
        ];

        QueryKey key2 =
        [
            "todos", new Dictionary<string, object>
            {
                {
                    "b", 2
                },
                {
                    "a", 1
                }
            }
        ];

        // Act
        var hash1 = hasher.HashQueryKey(key1);
        var hash2 = hasher.HashQueryKey(key2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_WithEquivalentNestedObjects_ShouldReturnSameHash()
    {
        // Arrange
        var hasher = new DefaultQueryKeyHasher();

        QueryKey key1 =
        [
            "todos", new
            {
                a = 1,
                b = new[]
                {
                    3, 2, 1
                }
            }
        ];

        QueryKey key2 =
        [
            "todos", new
            {
                b = new[]
                {
                    3, 2, 1
                },
                a = 1
            }
        ];

        // Act
        var hash1 = hasher.HashQueryKey(key1);
        var hash2 = hasher.HashQueryKey(key2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_ShouldThrowArgumentNullException_ForNullInput()
    {
        // Arrange
        var hasher = new DefaultQueryKeyHasher();
        QueryKey? key = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => hasher.HashQueryKey(key!));
    }

    [Fact]
    public void Hash_WithReorderedKey_ShouldReturnDifferentHash()
    {
        // Arrange
        var hasher = new DefaultQueryKeyHasher();
        QueryKey key1 = ["todos", 1];
        QueryKey key2 = [.. key1.Reverse()];

        // Act
        var hash1 = hasher.HashQueryKey(key1);
        var hash2 = hasher.HashQueryKey(key2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_WithNullValues_ShouldHandleCorrectly()
    {
        // Arrange
        var hasher = new DefaultQueryKeyHasher();
        QueryKey key1 = ["todos", null];
        QueryKey key2 = ["todos", null];

        // Act
        var hash1 = hasher.HashQueryKey(key1);
        var hash2 = hasher.HashQueryKey(key2);

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Hash_WithDifferentArrayOrder_ShouldReturnDifferentHash()
    {
        // Arrange
        var hasher = new DefaultQueryKeyHasher();
        int[] arr = [1, 2, 3];
        int[] reversed = [.. arr.Reverse()];
        QueryKey key1 = ["todos", arr];
        QueryKey key2 = ["todos", reversed];

        // Act
        var hash1 = hasher.HashQueryKey(key1);
        var hash2 = hasher.HashQueryKey(key2);

        // Assert
        Assert.NotEqual(hash1, hash2);
    }
}
