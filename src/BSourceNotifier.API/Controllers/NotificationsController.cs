using BSourceNotifier.API.Security;
using BSourceNotifier.Application.UseCases;
using BSourceNotifier.Contracts.Commands;
using Microsoft.AspNetCore.Mvc;

namespace BSourceNotifier.API.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly SendNotificationUseCase _useCase;

    public NotificationsController(SendNotificationUseCase useCase)
    {
        _useCase = useCase;
    }

    [HttpPost("send")]
    [ServiceFilter(typeof(ApiKeyAuthorizationFilter))]
    public async Task<IActionResult> SendAsync([FromBody] SendNotificationCommand command, CancellationToken cancellationToken)
    {
        await _useCase.ExecuteAsync(command, cancellationToken);
        return Accepted();
    }
}
