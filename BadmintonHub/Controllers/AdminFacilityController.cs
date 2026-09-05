using BadmintonHub.Data;
using BadmintonHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Controllers;

/// <summary>Admin-only facility settings (single-facility system).</summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminFacilityController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminFacilityController(ApplicationDbContext db) => _db = db;

    public IActionResult Index() => RedirectToAction(nameof(Edit));

    [HttpGet]
    public async Task<IActionResult> Edit()
    {
        var facility = await _db.Facilities.FirstOrDefaultAsync();
        if (facility == null)
            return NotFound();

        var vm = new FacilityEditViewModel
        {
            Id = facility.Id,
            Name = facility.Name,
            Description = facility.Description,
            Address = facility.Address,
            Phone = facility.Phone,
            Email = facility.Email,
            OpeningTime = facility.OpeningTime,
            ClosingTime = facility.ClosingTime,
            OperatingDays = facility.OperatingDays.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(d => d.Trim()).ToList(),
            Rules = facility.Rules,
            Status = facility.Status
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(FacilityEditViewModel vm)
    {
        if (!ModelState.IsValid)
            return View(vm);

        var facility = await _db.Facilities.FindAsync(vm.Id);
        if (facility == null)
            return NotFound();

        facility.Name = vm.Name.Trim();
        facility.Description = vm.Description;
        facility.Address = vm.Address.Trim();
        facility.Phone = vm.Phone;
        facility.Email = vm.Email;
        facility.OpeningTime = vm.OpeningTime;
        facility.ClosingTime = vm.ClosingTime;
        facility.OperatingDays = string.Join(",", vm.OperatingDays.Distinct().OrderBy(d => d));
        facility.Rules = vm.Rules;
        facility.Status = vm.Status;

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = "Facility settings updated successfully.";
        return RedirectToAction(nameof(Edit));
    }
}
