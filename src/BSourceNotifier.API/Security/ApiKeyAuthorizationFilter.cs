using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BSourceNotifier.API.Security;

public sealed class ApiKeyAuthorizationFilter : IAsyncActionFilter
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private readonly IApiKeyValidator _apiKeyValidator;
    private readonly ILogger<ApiKeyAuthorizationFilter> _logger;

    public ApiKeyAuthorizationFilter(
        IApiKeyValidator apiKeyValidator,
        ILogger<ApiKeyAuthorizationFilter> logger)
    {
        _apiKeyValidator = apiKeyValidator;
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var result = _apiKeyValidator.Validate(context.HttpContext.Request.Headers[ApiKeyHeaderName]);

        if (result.Status == ApiKeyValidationStatus.Valid)
        {
            _logger.LogInformation(
                "Notification API key authentication completed for {ClientName} with result {AuthenticationResult}.",
                result.ClientName,
                result.Status);

            await next();
            return;
        }

        _logger.LogWarning(
            "Notification API key authentication completed for {ClientName} with result {AuthenticationResult}.",
            result.ClientName,
            result.Status);

        context.Result = result.Status == ApiKeyValidationStatus.Missing
            ? new UnauthorizedResult()
            : new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}
