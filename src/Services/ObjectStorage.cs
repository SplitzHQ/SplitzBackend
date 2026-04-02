using System.Security.Cryptography;
using System.Text;
using FluentStorage.Blobs;
using Microsoft.Extensions.Options;

namespace SplitzBackend.Services;

public interface IObjectStorage
{
    Task UploadAsync(string objectKey, string contentType, Stream content, CancellationToken cancellationToken);
    Task DeleteIfOwnedAsync(string? storedUrlOrKey, CancellationToken cancellationToken);
    string BuildPublicUrl(string objectKey, TimeSpan roundingInterval, string cacheControl);
    bool TryParseObjectKey(string? storedUrlOrKey, out string objectKey);
}

public sealed class S3ObjectStorage(IBlobStorage blobStorage, IOptions<StorageOptions> options) : IObjectStorage
{
    private readonly StorageOptions _options = options.Value;

    public async Task UploadAsync(string objectKey, string contentType, Stream content,
        CancellationToken cancellationToken)
    {
        await blobStorage.WriteAsync(objectKey, content, cancellationToken: cancellationToken);
    }

    public async Task DeleteIfOwnedAsync(string? storedUrlOrKey, CancellationToken cancellationToken)
    {
        if (!TryParseObjectKey(storedUrlOrKey, out var key))
            return;

        // If the object doesn't exist, treat as success.
        try
        {
            await blobStorage.DeleteAsync(key, cancellationToken);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    public string BuildPublicUrl(string objectKey, TimeSpan cacheWindow, string cacheControl)
    {
        var endpoint = _options.Endpoint.TrimEnd('/');
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
            throw new InvalidOperationException("Object storage endpoint is not a valid URI.");

        var accessKey = _options.AccessKeyId;
        var secretKey = _options.SecretAccessKey;
        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey))
            throw new InvalidOperationException("Object storage access keys are not configured.");

        var region = string.IsNullOrWhiteSpace(_options.Region) ? "us-east-1" : _options.Region;
        const string service = "s3";

        // Use path-style: {endpoint}/{bucket}/{key}
        var objectPath =
            $"/{_options.Bucket}/{Uri.EscapeDataString(objectKey).Replace("%2F", "/", StringComparison.Ordinal)}";

        var host = endpointUri.IsDefaultPort
            ? endpointUri.Host
            : $"{endpointUri.Host}:{endpointUri.Port}";

        // Round timestamp to the nearest boundary so the same URL is generated within a window,
        // enabling browser caching. The URL expires at the end of the rounding interval.
        var now = DateTimeOffset.UtcNow;
        var roundedTicks = now.UtcTicks - now.UtcTicks % cacheWindow.Ticks;
        var roundedNow = new DateTimeOffset(roundedTicks, TimeSpan.Zero);
        var amzDate = roundedNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        var dateStamp = roundedNow.ToString("yyyyMMdd");
        // double the requested expiration to account for clock skew and ensure the URL is valid for at least the requested time.
        var expires = (int)cacheWindow.TotalSeconds * 2;

        var algorithm = "AWS4-HMAC-SHA256";
        var credentialScope = $"{dateStamp}/{region}/{service}/aws4_request";
        var credential = Uri.EscapeDataString($"{accessKey}/{credentialScope}");

        var query = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["X-Amz-Algorithm"] = algorithm,
            ["X-Amz-Credential"] = credential,
            ["X-Amz-Date"] = amzDate,
            ["X-Amz-Expires"] = expires.ToString(),
            ["X-Amz-SignedHeaders"] = "host",
            ["response-cache-control"] = Uri.EscapeDataString(cacheControl)
        };

        var canonicalQueryString = string.Join("&",
            query.Select(kvp => $"{kvp.Key}={kvp.Value}"));

        var canonicalHeaders = $"host:{host}\n";
        var signedHeaders = "host";
        var payloadHash = "UNSIGNED-PAYLOAD";

        var canonicalRequest = string.Join("\n", new[]
        {
            "GET",
            objectPath,
            canonicalQueryString,
            canonicalHeaders,
            signedHeaders,
            payloadHash
        });

        var stringToSign = string.Join("\n", new[]
        {
            algorithm,
            amzDate,
            credentialScope,
            HashHex(canonicalRequest)
        });

        var signingKey = GetSignatureKey(secretKey, dateStamp, region, service);
        var signature = HmacHex(signingKey, stringToSign);

        var finalUri = new UriBuilder(endpointUri)
        {
            Path = objectPath,
            Query = $"{canonicalQueryString}&X-Amz-Signature={signature}"
        };

        return finalUri.Uri.ToString();
    }

    public bool TryParseObjectKey(string? storedUrlOrKey, out string objectKey)
    {
        objectKey = string.Empty;

        if (string.IsNullOrWhiteSpace(storedUrlOrKey))
            return false;

        // treat as a raw key only if it looks like one of ours.
        // (Avoid deleting arbitrary external URLs already stored in Photo.)
        if (storedUrlOrKey.StartsWith("users/", StringComparison.OrdinalIgnoreCase) ||
            storedUrlOrKey.StartsWith("groups/", StringComparison.OrdinalIgnoreCase) ||
            storedUrlOrKey.StartsWith("transactions/", StringComparison.OrdinalIgnoreCase) ||
            storedUrlOrKey.StartsWith("drafts/", StringComparison.OrdinalIgnoreCase))
        {
            objectKey = storedUrlOrKey;
            return true;
        }

        return false;
    }

    private static string HashHex(string data)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static byte[] Hmac(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string HmacHex(byte[] key, string data)
    {
        var bytes = Hmac(key, data);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static byte[] GetSignatureKey(string secretKey, string dateStamp, string regionName, string serviceName)
    {
        var kSecret = Encoding.UTF8.GetBytes("AWS4" + secretKey);
        var kDate = Hmac(kSecret, dateStamp);
        var kRegion = Hmac(kDate, regionName);
        var kService = Hmac(kRegion, serviceName);
        return Hmac(kService, "aws4_request");
    }
}