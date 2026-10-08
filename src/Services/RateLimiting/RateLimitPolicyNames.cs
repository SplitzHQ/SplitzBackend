namespace SplitzBackend.Services.RateLimiting;

/// <summary>Names of the policies registered with <c>AddRateLimiter</c>, used by <c>[EnableRateLimiting]</c>.</summary>
public static class RateLimitPolicyNames
{
    public const string EmailConfirmationIp = "email-confirmation-ip";
    public const string EmailDeliveryIp = "email-delivery-ip";
    public const string LoginIp = "login-ip";
    public const string PasswordResetIp = "password-reset-ip";
    public const string RegistrationIp = "registration-ip";
    public const string UploadPerUser = "upload-per-user";
}

/// <summary>Workflow being limited. Used as the account limiter key and in rejection logs.</summary>
public static class RateLimitCategories
{
    public const string EmailConfirmation = "email-confirmation";
    public const string EmailDelivery = "email-delivery";
    public const string Login = "login";
    public const string PasswordReset = "password-reset";
    public const string Registration = "registration";
    public const string Upload = "upload";
}

/// <summary>What a rejected request was partitioned by. Only appears in rejection logs.</summary>
public static class RateLimitPartitionTypes
{
    public const string Account = "account";
    public const string Global = "global";
    public const string Ip = "ip";
    public const string User = "user";
}