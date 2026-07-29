using Microsoft.Extensions.Primitives;

namespace BSourceNotifier.API.Security;

public interface IApiKeyValidator
{
    ApiKeyValidationResult Validate(StringValues apiKeyHeader);
}
