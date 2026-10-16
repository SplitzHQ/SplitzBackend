using FluentStorage;
using Microsoft.Extensions.Options;
using SplitzBackend.Services;
using Xunit;

namespace SplitzBackend.Tests;

/// <summary>
/// Checks image upload and cleanup through the FluentStorage store API.
/// </summary>
public class ObjectStorageTests
{
    /// <summary>
    /// Uploads must replace existing content at the same object key.
    /// </summary>
    [Fact]
    public async Task UploadReplacesExistingObject()
    {
        using var store = StorageFactory.InMemory();
        var storage = new S3ObjectStorage(store, Options.Create(new StorageOptions()));
        using var original = new MemoryStream([1, 2, 3]);
        using var replacement = new MemoryStream([4, 5]);

        await storage.UploadAsync("users/avatar.webp", "image/webp", original, CancellationToken.None);
        await storage.UploadAsync("users/avatar.webp", "image/webp", replacement, CancellationToken.None);

        Assert.Equal(new byte[] { 4, 5 }, await store.GetBytes("users/avatar.webp"));
    }

    /// <summary>
    /// Cleanup removes keys in each application-owned image prefix.
    /// </summary>
    [Theory]
    [InlineData("users/avatar.webp")]
    [InlineData("groups/avatar.webp")]
    [InlineData("transactions/receipt.webp")]
    [InlineData("drafts/receipt.webp")]
    public async Task CleanupDeletesOwnedObject(string objectKey)
    {
        using var store = StorageFactory.InMemory();
        var storage = new S3ObjectStorage(store, Options.Create(new StorageOptions()));
        using var content = new MemoryStream([1, 2, 3]);
        await storage.UploadAsync(objectKey, "image/webp", content, CancellationToken.None);

        await storage.DeleteIfOwnedAsync(objectKey, CancellationToken.None);
        await storage.DeleteIfOwnedAsync(objectKey, CancellationToken.None);

        Assert.False(await store.ObjectExists(objectKey));
    }

    /// <summary>
    /// Cleanup must leave external URLs and keys outside the owned prefixes unchanged.
    /// </summary>
    [Theory]
    [InlineData("https://example.com/users/avatar.webp")]
    [InlineData("other/avatar.webp")]
    public async Task CleanupIgnoresUnownedObject(string objectKey)
    {
        using var store = StorageFactory.InMemory();
        var storage = new S3ObjectStorage(store, Options.Create(new StorageOptions()));
        using var content = new MemoryStream([1, 2, 3]);
        await store.SetObject(objectKey, content);

        await storage.DeleteIfOwnedAsync(objectKey, CancellationToken.None);

        Assert.True(await store.ObjectExists(objectKey));
    }
}