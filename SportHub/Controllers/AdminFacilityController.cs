using SportHub.Data;
using SportHub.Models;
using SportHub.Services;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Controllers;

/// <summary>
/// Admin facility maintenance (revised spec): multiple facilities, each inside a
/// category, with their own opening hours and photo gallery.
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminFacilityController : Controller
{
    private static readonly string[] DayOrder = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    private readonly ApplicationDbContext _db;
    private readonly IImageService _imageService;

    public AdminFacilityController(ApplicationDbContext db, IImageService imageService)
    {
        _db = db;
        _imageService = imageService;
    }

    // ---------- List (G-M6: AJAX search/sort/paging) ----------

    public async Task<IActionResult> Index(string? search, string? sort, string? dir, int page = 1, int size = 10)
    {
        var request = AjaxListRequest.From(Request.Query);
        var query = _db.Facilities
            .Include(f => f.Category)
            .Include(f => f.Courts)
            .Include(f => f.Photos)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(f => f.Name.Contains(request.Search) ||
                                     (f.Address != null && f.Address.Contains(request.Search)));

        query = (request.Sort, request.Descending) switch
        {
            ("name", false) => query.OrderBy(f => f.Name),
            ("name", true) => query.OrderByDescending(f => f.Name),
            ("category", false) => query.OrderBy(f => f.Category!.Name).ThenBy(f => f.Name),
            ("category", true) => query.OrderByDescending(f => f.Category!.Name).ThenBy(f => f.Name),
            _ => query.OrderBy(f => f.Name)
        };

        var total = await query.CountAsync();
        var pager = AjaxPager.For(request, total);
        var pageModel = new AjaxListPage<Facility>
        {
            Items = await query.Skip((pager.Page - 1) * pager.PageSize).Take(pager.PageSize).ToListAsync(),
            Pager = pager
        };

        if (Request.IsAjaxListRequest())
            return PartialView("_FacilityTable", pageModel);
        return View(pageModel);
    }

    // ---------- Create ----------

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        var vm = new FacilityEditViewModel
        {
            OpeningTime = new TimeSpan(8, 0, 0),
            ClosingTime = new TimeSpan(23, 0, 0),
            OperatingDays = new List<string> { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" },
            Categories = await _db.Categories.Where(c => c.Status == CategoryStatus.Active)
                .OrderBy(c => c.DisplayOrder).ToListAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(FacilityEditViewModel vm)
    {
        if (await _db.Categories.AnyAsync(c => c.Id == vm.CategoryId) == false)
            ModelState.AddModelError(nameof(vm.CategoryId), "Please select a category.");

        if (!ModelState.IsValid)
        {
            vm.Categories = await _db.Categories.Where(c => c.Status == CategoryStatus.Active)
                .OrderBy(c => c.DisplayOrder).ToListAsync();
            return View(vm);
        }

        _db.Facilities.Add(new Facility
        {
            CategoryId = vm.CategoryId,
            Name = vm.Name.Trim(),
            Description = vm.Description,
            Address = vm.Address.Trim(),
            Phone = vm.Phone,
            Email = vm.Email,
            OpeningTime = vm.OpeningTime,
            ClosingTime = vm.ClosingTime,
            OperatingDays = string.Join(",", vm.OperatingDays.Distinct().OrderBy(d => Array.IndexOf(DayOrder, d))),
            Rules = vm.Rules,
            Status = vm.Status
        });
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Facility \"{vm.Name}\" created. Add its courts and photos next.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Edit ----------

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var facility = await _db.Facilities.FindAsync(id);
        if (facility == null) return NotFound();

        var vm = new FacilityEditViewModel
        {
            Id = facility.Id,
            CategoryId = facility.CategoryId,
            Name = facility.Name,
            Description = facility.Description,
            Address = facility.Address,
            Phone = facility.Phone,
            Email = facility.Email,
            OpeningTime = facility.OpeningTime,
            ClosingTime = facility.ClosingTime,
            OperatingDays = facility.OperatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim()).ToList(),
            Rules = facility.Rules,
            Status = facility.Status,
            Categories = await _db.Categories.OrderBy(c => c.DisplayOrder).ToListAsync()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(FacilityEditViewModel vm)
    {
        if (await _db.Categories.AnyAsync(c => c.Id == vm.CategoryId) == false)
            ModelState.AddModelError(nameof(vm.CategoryId), "Please select a category.");

        if (!ModelState.IsValid)
        {
            vm.Categories = await _db.Categories.OrderBy(c => c.DisplayOrder).ToListAsync();
            return View(vm);
        }

        var facility = await _db.Facilities.FindAsync(vm.Id);
        if (facility == null) return NotFound();

        facility.CategoryId = vm.CategoryId;
        facility.Name = vm.Name.Trim();
        facility.Description = vm.Description;
        facility.Address = vm.Address.Trim();
        facility.Phone = vm.Phone;
        facility.Email = vm.Email;
        facility.OpeningTime = vm.OpeningTime;
        facility.ClosingTime = vm.ClosingTime;
        facility.OperatingDays = string.Join(",", vm.OperatingDays.Distinct().OrderBy(d => Array.IndexOf(DayOrder, d)));
        facility.Rules = vm.Rules;
        facility.Status = vm.Status;

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Facility settings updated successfully.";
        return RedirectToAction(nameof(Edit), new { id = vm.Id });
    }

    // ---------- Delete (blocked when the facility has courts) ----------

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var facility = await _db.Facilities
            .Include(f => f.Courts)
            .FirstOrDefaultAsync(f => f.Id == id);
        if (facility == null) return NotFound();

        ViewBag.CourtCount = facility.Courts.Count;
        return View(facility);
    }

    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var facility = await _db.Facilities
            .Include(f => f.Courts)
            .Include(f => f.Photos)
            .FirstOrDefaultAsync(f => f.Id == id);
        if (facility == null) return NotFound();

        // Business rule: a facility owns its courts — delete the courts first.
        // (The FK is Restrict anyway; this gives the staff a friendly message.)
        if (facility.Courts.Count > 0)
        {
            TempData["ErrorMessage"] =
                $"Facility \"{facility.Name}\" cannot be deleted because it has {facility.Courts.Count} court(s). " +
                "Delete the courts first.";
            return RedirectToAction(nameof(Index));
        }

        foreach (var photo in facility.Photos)
            _imageService.DeleteFacilityPhoto(photo.FilePath);

        _db.Facilities.Remove(facility);
        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Facility \"{facility.Name}\" deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Photo gallery (drag & drop upload, 800×450) ----------

    [HttpGet]
    public async Task<IActionResult> ManagePhotos(int id)
    {
        var facility = await _db.Facilities
            .Include(f => f.Category)
            .Include(f => f.Photos.OrderBy(p => p.DisplayOrder))
            .FirstOrDefaultAsync(f => f.Id == id);
        if (facility == null) return NotFound();
        return View(facility);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadPhotos(int facilityId, List<IFormFile> photos)
    {
        var facility = await _db.Facilities
            .Include(f => f.Photos)
            .FirstOrDefaultAsync(f => f.Id == facilityId);
        if (facility == null) return NotFound();

        var saved = 0;
        foreach (var file in photos.Where(f => f.Length > 0))
        {
            var (error, relativePath) = _imageService.SaveFacilityPhoto(file, facilityId);
            if (error != null)
            {
                TempData["ErrorMessage"] = $"Photo \"{file.FileName}\" was skipped: {error}";
                continue;
            }

            var nextOrder = facility.Photos.Count == 0 ? 1 : facility.Photos.Max(p => p.DisplayOrder) + 1;
            _db.FacilityPhotos.Add(new FacilityPhoto
            {
                FacilityId = facilityId,
                FilePath = relativePath!,
                Caption = Path.GetFileNameWithoutExtension(file.FileName),
                DisplayOrder = nextOrder,
                IsPrimary = facility.Photos.Count == 0
            });
            saved++;
        }

        await _db.SaveChangesAsync();
        if (saved > 0)
            TempData["SuccessMessage"] = $"{saved} photo(s) uploaded for {facility.Name}.";
        return RedirectToAction(nameof(ManagePhotos), new { id = facilityId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPrimaryPhoto(int photoId)
    {
        var photo = await _db.FacilityPhotos.Include(p => p.Facility)
            .FirstOrDefaultAsync(p => p.Id == photoId);
        if (photo == null) return NotFound();

        await _db.FacilityPhotos.Where(p => p.FacilityId == photo.FacilityId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsPrimary, false));
        photo.IsPrimary = true;
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = "Cover photo updated.";
        return RedirectToAction(nameof(ManagePhotos), new { id = photo.FacilityId });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePhoto(int photoId)
    {
        var photo = await _db.FacilityPhotos.Include(p => p.Facility)
            .FirstOrDefaultAsync(p => p.Id == photoId);
        if (photo == null) return NotFound();

        var facilityId = photo.FacilityId;
        var wasPrimary = photo.IsPrimary;

        _imageService.DeleteFacilityPhoto(photo.FilePath);
        _db.FacilityPhotos.Remove(photo);
        await _db.SaveChangesAsync();

        // Promote another photo to cover so the gallery always has a primary image.
        if (wasPrimary)
        {
            var next = await _db.FacilityPhotos.Where(p => p.FacilityId == facilityId)
                .OrderBy(p => p.DisplayOrder).FirstOrDefaultAsync();
            if (next != null)
            {
                next.IsPrimary = true;
                await _db.SaveChangesAsync();
            }
        }

        TempData["SuccessMessage"] = "Photo removed.";
        return RedirectToAction(nameof(ManagePhotos), new { id = facilityId });
    }
}
