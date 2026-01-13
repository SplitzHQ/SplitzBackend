namespace SplitzBackend.Services;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string Provider { get; init; } = "S3";
    public string Endpoint { get; init; } = string.Empty;
    public string AccessKeyId { get; init; } = string.Empty;
    public string SecretAccessKey { get; init; } = string.Empty;
    public string Bucket { get; init; } = "splitz";
    public string Region { get; init; } = "us-east-1";
}