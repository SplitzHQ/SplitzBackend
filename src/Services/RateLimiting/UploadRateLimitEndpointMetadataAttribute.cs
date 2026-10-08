namespace SplitzBackend.Services.RateLimiting;

/// <summary>
/// Marks an upload action so the global concurrency limiter applies to it. Pair it with
/// <c>[EnableRateLimiting(RateLimitPolicyNames.UploadPerUser)]</c> for the per-user hourly limit.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UploadRateLimitEndpointMetadataAttribute : Attribute;