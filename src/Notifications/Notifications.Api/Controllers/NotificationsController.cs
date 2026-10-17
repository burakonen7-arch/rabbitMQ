using Microsoft.AspNetCore.Mvc;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Api.Controllers;

[ApiController]
[Route("api/notifications")]
public sealed class NotificationsController(NotificationInbox inbox) : ControllerBase
{
    [HttpGet]
    public ActionResult<IReadOnlyList<ReceivedNotification>> GetAll() => Ok(inbox.GetAll());
}
