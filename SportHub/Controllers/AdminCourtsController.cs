using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Controllers;

/// <summary>Admin court management: full CRUD plus multiple-photo uploads.</summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminCourtsController : Controller
{
    private const int PageSize = 8;
    private static readonly string[] AllowedPhotoExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private const long MaxPhotoBytes = 2 * 1024 * 1024; // 2 MB

    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IWishlistService _wishlistService;

    public AdminCourtsController(ApplicationDbContext db, IWebHostEnvironment env, IWishlistService wishlistService)
    {
        _db = db;
        _env = env;
        _wishlistService = wishlistService;
    }

    // ---------- List with search / filter / pagination ----------
    // G-M6: AJAX search/sort/paging. Filter selects and pager/sort links all
    // carry the facility/type/status filters through every request (the old
    // pagination dropped the facility filter — fixed via pager.Extra).

    public async Task<IActionResult> Index(string? search, int? facilityId, CourtType? type, CourtStatus? status,
        string? sort, string? dir, int page = 1, int size = 10)
    {
        var request = AjaxListRequest.From(Request.Query);

        var query = _db.Courts.Include(c => c.Facility).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(c => c.CourtNumber.Contains(request.Search));
        if (facilityId.HasValue) query = query.Where(c => c.FacilityId == facilityId);
        if (type.HasValue) query = query.Where(c => c.CourtType == type);
        if (status.HasValue) query = query.Where(c => c.Status == status);

        query = (request.Sort, request.Descending) switch
        {
            ("rate", false) => query.OrderBy(c => c.HourlyRate).ThenBy(c => c.CourtNumber),
            ("rate", true) => query.OrderByDescending(c => c.HourlyRate).ThenBy(c => c.CourtNumber),
            ("type", false) => query.OrderBy(c => c.CourtType).ThenBy(c => c.CourtNumber),
            ("type", true) => query.OrderByDescending(c => c.CourtType).ThenBy(c => c.CourtNumber),
            ("status", false) => query.OrderBy(c => c.Status).ThenBy(c => c.CourtNumber),
            ("status", true) => query.OrderByDescending(c => c.Status).ThenBy(c => c.CourtNumber),
            ("facility", false) => query.OrderBy(c => c.Facility!.Name).ThenBy(c => c.CourtNumber),
            ("facility", true) => query.OrderByDescending(c => c.Facility!.Name).ThenBy(c => c.CourtNumber),
            _ => request.Descending
                ? query.OrderByDescending(c => c.CourtNumber)
                : query.OrderBy(c => c.FacilityId).ThenBy(c => c.CourtNumber)
        };

        var total = await query.CountAsync();
        var pager = AjaxPager.For(request, total);
        pager.Extra["facilityId"] = facilityId?.ToString() ?? "";
        pager.Extra["type"] = type?.ToString() ?? "";
        pager.Extra["status"] = status?.ToString() ?? "";

        var vm = new AdminCourtIndexViewModel
        {
            FacilityId = facilityId,
            Type = type,
            Status = status,
            Facilities = await _db.Facilities.OrderBy(f => f.Name).ToListAsync(),
            Page = new AjaxListPage<Court>
            {
                Items = await query.Skip((pager.Page - 1) * pager.PageSize).Take(pager.PageSize).ToListAsync(),
                Pager = pager
            }
        };

        if (Request.IsAjaxListRequest())
            return PartialView("_CourtTable", vm.Page);
        return View(vm);
    }

    // ---------- Create ----------

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new CourtFormViewModel
        {
            HourlyRate = 25.00m,
            Facilities = await _db.Facilities.OrderBy(f => f.Name).ToListAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CourtFormViewModel vm)
    {
        if (await _db.Facilities.AnyAsync(f => f.Id == vm.FacilityId) == false)
            ModelState.AddModelError(nameof(vm.FacilityId), "Please select a facility.");
        else if (await _db.Courts.AnyAsync(c => c.CourtNumber == vm.CourtNumber && c.FacilityId == vm.FacilityId))
            ModelState.AddModelError(nameof(vm.CourtNumber), "This facility already has a court with this number.");

        if (!ModelState.IsValid)
        {
            vm.Facilities = await _db.Facilities.OrderBy(f => f.Name).ToListAsync();
            return View(vm);
        }

        _db.Courts.Add(new Court
        {
            FacilityId = vm.FacilityId,
            CourtNumber = vm.CourtNumber.Trim(),
            CourtType = vm.CourtType,
            Status = vm.Status,
            HourlyRate = vm.HourlyRate,
            Description = vm.Description
        });
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Court {vm.CourtNumber} created successfully.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Edit ----------

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var court = await _db.Courts.FindAsync(id);
        if (court == null) return NotFound();

        var vm = new CourtFormViewModel
        {
            Id = court.Id,
            FacilityId = court.FacilityId,
            CourtNumber = court.CourtNumber,
            CourtType = court.CourtType,
            Status = court.Status,
            HourlyRate = court.HourlyRate,
            Description = court.Description,
            Facilities = await _db.Facilities.OrderBy(f => f.Name).ToListAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CourtFormViewModel vm)
    {
        if (await _db.Facilities.AnyAsync(f => f.Id == vm.FacilityId) == false)
            ModelState.AddModelError(nameof(vm.FacilityId), "Please select a facility.");
        else if (await _db.Courts.AnyAsync(c => c.CourtNumber == vm.CourtNumber && c.Id != vm.Id && c.FacilityId == vm.FacilityId))
            ModelState.AddModelError(nameof(vm.CourtNumber), "This facility already has a court with this number.");

        if (!ModelState.IsValid)
        {
            vm.Facilities = await _db.Facilities.OrderBy(f => f.Name).ToListAsync();
            return View(vm);
        }

        var court = await _db.Courts.FindAsync(vm.Id);
        if (court == null) return NotFound();

        // G-M4: when a court leaves Available, re-arm wishlist notifications so the
        // members waiting on it get told about the next reopening.
        if (court.Status == CourtStatus.Available && vm.Status != CourtStatus.Available)
            await _wishlistService.ResetNotifiedForCourtAsync(court.Id);

        var reopening = court.Status != CourtStatus.Available && vm.Status == CourtStatus.Available;

        court.FacilityId = vm.FacilityId;
        court.CourtNumber = vm.CourtNumber.Trim();
        court.CourtType = vm.CourtType;
        court.Status = vm.Status;
        court.HourlyRate = vm.HourlyRate;
        court.Description = vm.Description;
        court.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();

        // G-M4: reopening a court tells the waiting members immediately (one-shot
        // via NotifiedAt); the 15-minute worker stays as the safety net.
        if (reopening)
            await _wishlistService.NotifyForAvailableCourtsAsync();

        TempData["SuccessMessage"] = $"Court {vm.CourtNumber} updated successfully.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Details ----------

    [HttpGet]
    public async Task<IActionResult> Details(int id)
    {
        var court = await _db.Courts
            .Include(c => c.Facility)
            .Include(c => c.Photos.OrderBy(p => p.DisplayOrder))
            .FirstOrDefaultAsync(c => c.Id == id);
        if (court == null) return NotFound();

        ViewBag.UpcomingReservations = await _db.Reservations
            .Include(r => r.User)
            .Where(r => r.CourtId == id &&
                        r.ReservationDate >= DateOnly.FromDateTime(DateTime.Today) &&
                        (r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.Confirmed))
            .OrderBy(r => r.ReservationDate).ThenBy(r => r.StartTime)
            .Take(10)
            .ToListAsync();

        return View(court);
    }

    // ---------- Delete (blocked when the court has reservation history) ----------

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var court = await _db.Courts.FindAsync(id);
        if (court == null) return NotFound();

        ViewBag.ReservationCount = await _db.Reservations.CountAsync(r => r.CourtId == id);
        return View(court);
    }

    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var court = await _db.Courts.FindAsync(id);
        if (court == null) return NotFound();

        // Business rule: keep audit history — a court with any reservations cannot be deleted.
        var hasReservations = await _db.Reservations.AnyAsync(r => r.CourtId == id);
        if (hasReservations)
        {
            TempData["ErrorMessage"] = "Court cannot be deleted because it has reservation history. Set its status to Unavailable instead.";
            return RedirectToAction(nameof(Index));
        }

        var uploadsDir = Path.Combine(_env.WebRootPath ?? string.Empty, "uploads", "courts", id.ToString());
        _db.Courts.Remove(court);
        await _db.SaveChangesAsync();

        if (Directory.Exists(uploadsDir))
            Directory.Delete(uploadsDir, recursive: true);

        TempData["SuccessMessage"] = $"Court {court.CourtNumber} deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Photos ----------

    [HttpGet]
    public async Task<IActionResult> ManagePhotos(int id)
    {
        var court = await _db.Courts
            .Include(c => c.Photos.OrderBy(p => p.DisplayOrder))
            .FirstOrDefaultAsync(c => c.Id == id);
        if (court == null) return NotFound();
        return View(court);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPhotos(int courtId, List<IFormFile> photos)
    {
        var court = await _db.Courts
            .Include(c => c.Photos)
            .FirstOrDefaultAsync(c => c.Id == courtId);
        if (court == null) return NotFound();

        var uploadsRoot = Path.Combine(_env.WebRootPath ?? string.Empty, "uploads", "courts", courtId.ToString());
        Directory.CreateDirectory(uploadsRoot);

        var saved = 0;
        foreach (var file in photos.Where(f => f.Length > 0))
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedPhotoExtensions.Contains(extension) || file.Length > MaxPhotoBytes)
            {
                TempData["ErrorMessage"] = $"Photo \"{file.FileName}\" was skipped: only JPG/PNG/WebP/GIF up to 2 MB are allowed.";
                continue;
            }

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var fullPath = Path.Combine(uploadsRoot, fileName);
            await using (var stream = System.IO.File.Create(fullPath))
            {
                await file.CopyToAsync(stream);
            }

            var nextOrder = court.Photos.Count == 0 ? 1 : court.Photos.Max(p => p.DisplayOrder) + 1;
            _db.CourtPhotos.Add(new CourtPhoto
            {
                CourtId = courtId,
                FilePath = $"/uploads/courts/{courtId}/{fileName}",
                Caption = Path.GetFileNameWithoutExtension(file.FileName),
                DisplayOrder = nextOrder,
                IsPrimary = court.Photos.Count == 0
            });
            saved++;
        }

        await _db.SaveChangesAsync();
        if (saved > 0)
            TempData["SuccessMessage"] = $"{saved} photo(s) uploaded for Court {court.CourtNumber}.";
        return RedirectToAction(nameof(ManagePhotos), new { id = courtId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePhoto(int photoId)
    {
        var photo = await _db.CourtPhotos.Include(p => p.Court).FirstOrDefaultAsync(p => p.Id == photoId);
        if (photo == null) return NotFound();

        var courtId = photo.CourtId;
        var wasPrimary = photo.IsPrimary;

        // Only remove physical files we manage under /uploads (seeded demo images live in /images).
        if (photo.FilePath.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            var fullPath = Path.Combine(_env.WebRootPath ?? string.Empty, photo.FilePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(fullPath))
                System.IO.File.Delete(fullPath);
        }

        _db.CourtPhotos.Remove(photo);
        await _db.SaveChangesAsync();

        // Promote another photo to primary so the gallery always has a cover image.
        if (wasPrimary)
        {
            var next = await _db.CourtPhotos.Where(p => p.CourtId == courtId).OrderBy(p => p.DisplayOrder).FirstOrDefaultAsync();
            if (next != null)
            {
                next.IsPrimary = true;
                await _db.SaveChangesAsync();
            }
        }

        TempData["SuccessMessage"] = "Photo removed.";
        return RedirectToAction(nameof(ManagePhotos), new { id = courtId });
    }
}
