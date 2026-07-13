namespace SplitzBackend.Services.RateLimiting;

public static class RateLimitPolicyNames
{
    public const string LoginIp = "login-ip";
    public const string RegistrationIp = "registration-ip";
}

public sealed record RateLimitEndpointMetadata(string Category, string PartitionType);

public sealed record AccountRateLimitEndpointMetadata(string Category);