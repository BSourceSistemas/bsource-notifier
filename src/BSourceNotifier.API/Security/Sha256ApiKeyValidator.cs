using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace BSourceNotifier.API.Security;

public sealed class Sha256ApiKeyValidator : IApiKeyValidator
{
    private const int Sha256HashByteLength = 32;
    private readonly IOptionsMonitor<NotificationSecurityOptions> _options;

    public Sha256ApiKeyValidator(IOptionsMonitor<NotificationSecurityOptions> options)
    {
        _options = options;
    }

    public ApiKeyValidationResult Validate(StringValues apiKeyHeader)
    {
        if (apiKeyHeader.Count == 0)
        {
            return ApiKeyValidationResult.Missing;
        }

        if (apiKeyHeader.Count != 1 || string.IsNullOrEmpty(apiKeyHeader[0]))
        {
            return ApiKeyValidationResult.Invalid;
        }

        var presentedHash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKeyHeader[0]!));

        foreach (var client in _options.CurrentValue.AuthorizedClients)
        {
            if (string.IsNullOrWhiteSpace(client.Name) ||
                !TryParseSha256Hash(client.ApiKeyHash, out var configuredHash))
            {
                continue;
            }

            if (CryptographicOperations.FixedTimeEquals(presentedHash, configuredHash))
            {
                return ApiKeyValidationResult.Valid(client.Name);
            }
        }

        return ApiKeyValidationResult.Invalid;
    }

    private static bool TryParseSha256Hash(string? value, out byte[] hash)
    {
        hash = Array.Empty<byte>();

        if (string.IsNullOrWhiteSpace(value) || value.Length != Sha256HashByteLength * 2)
        {
            return false;
        }

        var parsed = new byte[Sha256HashByteLength];
        for (var index = 0; index < parsed.Length; index++)
        {
            var high = FromHex(value[index * 2]);
            var low = FromHex(value[index * 2 + 1]);

            if (high < 0 || low < 0)
            {
                return false;
            }

            parsed[index] = (byte)((high << 4) | low);
        }

        hash = parsed;
        return true;
    }

    private static int FromHex(char value)
        => value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' => value - 'a' + 10,
            >= 'A' and <= 'F' => value - 'A' + 10,
            _ => -1
        };
}
