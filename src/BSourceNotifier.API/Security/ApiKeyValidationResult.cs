namespace BSourceNotifier.API.Security;

public sealed record ApiKeyValidationResult(ApiKeyValidationStatus Status, string? ClientName)
{
    public static ApiKeyValidationResult Missing { get; } = new(ApiKeyValidationStatus.Missing, null);
    public static ApiKeyValidationResult Invalid { get; } = new(ApiKeyValidationStatus.Invalid, null);
    public static ApiKeyValidationResult Valid(string clientName) => new(ApiKeyValidationStatus.Valid, clientName);
}

public enum ApiKeyValidationStatus
{
    Missing,
    Invalid,
    Valid
}
