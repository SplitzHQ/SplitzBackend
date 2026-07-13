namespace SplitzBackend.Services.RateLimiting;

public static class RateLimitPolicyNames
{
    public const string EmailConfirmationIp = "email-confirmation-ip";
    public const string EmailDeliveryIp = "email-delivery-ip";
    public const string LoginIp = "login-ip";
    public const string PasswordResetIp = "password-reset-ip";
    public const string RegistrationIp = "registration-ip";
    public const string UploadPerUser = "upload-per-user";
}

public sealed record RateLimitEndpointMetadata(string Category, string PartitionType);

public sealed record AccountRateLimitEndpointMetadata(string Category);

[AttributeUsage(AttributeTargets.Method)]
public sealed class UploadRateLimitEndpointMetadataAttribute : Attribute;