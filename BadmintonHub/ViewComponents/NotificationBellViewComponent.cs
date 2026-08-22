using BadmintonHub.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace BadmintonHub.ViewComponents;

/// <summary>Navbar bell: latest unread notifications and their count.</summary>
public class NotificationBellViewComponent : ViewComponent
{
    private readonly ApplicationDbContext _db;

    public NotificationBellViewComponent(ApplicationDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Content(string.Empty);

        var userId = int.Parse(((ClaimsPrincipal)User).FindFirstValue(ClaimTypes.NameIdentifier)!);
        var notifications = await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(8)
            .ToListAsync();

        return View(notifications);
    }
}
