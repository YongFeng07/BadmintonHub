using SportHub.Data;
using SportHub.Models;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Controllers;

/// <summary>
/// Admin facility-category maintenance (revised spec): CRUD over the 11 categories,
/// with status and display-order control. A category that owns facilities cannot
/// be deleted (business rule + FK Restrict).
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminCategoriesController : Controller
{
    private readonly ApplicationDbContext _db;

    public AdminCategoriesController(ApplicationDbContext db) => _db = db;

    // ---------- List (G-M6: AJAX search/sort/paging) ----------

    public async Task<IActionResult> Index(string? search, string? sort, string? dir, int page = 1, int size = 10)
    {
        var request = AjaxListRequest.From(Request.Query);
        var query = _db.Categories.Include(c => c.Facilities).AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(c => c.Name.Contains(request.Search) ||
                                     (c.Description != null && c.Description.Contains(request.Search)));

        query = (request.Sort, request.Descending) switch
        {
            ("order", false) => query.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name),
            ("order", true) => query.OrderByDescending(c => c.DisplayOrder).ThenBy(c => c.Name),
            ("name", false) => query.OrderBy(c => c.Name),
            ("name", true) => query.OrderByDescending(c => c.Name),
            _ => query.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name)
        };

        var total = await query.CountAsync();
        var pager = AjaxPager.For(request, total);
        var pageModel = new AjaxListPage<Category>
        {
            Items = await query.Skip((pager.Page - 1) * pager.PageSize).Take(pager.PageSize).ToListAsync(),
            Pager = pager
        };

        if (Request.IsAjaxListRequest())
            return PartialView("_CategoryTable", pageModel);
        return View(pageModel);
    }

    // ---------- Create ----------

    [HttpGet]
    public IActionResult Create() =>
        View(new CategoryFormViewModel { DisplayOrder = 12 });

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryFormViewModel vm)
    {
        if (await _db.Categories.AnyAsync(c => c.Name == vm.Name.Trim()))
            ModelState.AddModelError(nameof(vm.Name), "A category with this name already exists.");

        if (!ModelState.IsValid)
            return View(vm);

        _db.Categories.Add(new Category
        {
            Name = vm.Name.Trim(),
            Description = vm.Description,
            UnitLabel = vm.UnitLabel.Trim(),
            Icon = vm.Icon?.Trim(),
            Status = vm.Status,
            DisplayOrder = vm.DisplayOrder
        });
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"Category \"{vm.Name}\" created.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Edit ----------

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category == null) return NotFound();

        return View(new CategoryFormViewModel
        {
            Id = category.Id,
            Name = category.Name,
            Description = category.Description,
            UnitLabel = category.UnitLabel,
            Icon = category.Icon,
            Status = category.Status,
            DisplayOrder = category.DisplayOrder
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(CategoryFormViewModel vm)
    {
        if (await _db.Categories.AnyAsync(c => c.Name == vm.Name.Trim() && c.Id != vm.Id))
            ModelState.AddModelError(nameof(vm.Name), "A category with this name already exists.");

        if (!ModelState.IsValid)
            return View(vm);

        var category = await _db.Categories.FindAsync(vm.Id);
        if (category == null) return NotFound();

        category.Name = vm.Name.Trim();
        category.Description = vm.Description;
        category.UnitLabel = vm.UnitLabel.Trim();
        category.Icon = vm.Icon?.Trim();
        category.Status = vm.Status;
        category.DisplayOrder = vm.DisplayOrder;

        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Category \"{vm.Name}\" updated.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Delete (blocked when the category owns facilities) ----------

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var category = await _db.Categories
            .Include(c => c.Facilities)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();

        ViewBag.FacilityCount = category.Facilities.Count;
        return View(category);
    }

    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var category = await _db.Categories
            .Include(c => c.Facilities)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (category == null) return NotFound();

        if (category.Facilities.Count > 0)
        {
            TempData["ErrorMessage"] =
                $"Category \"{category.Name}\" cannot be deleted because it has {category.Facilities.Count} facility(ies). " +
                "Move or delete the facilities first.";
            return RedirectToAction(nameof(Index));
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Category \"{category.Name}\" deleted.";
        return RedirectToAction(nameof(Index));
    }
}
