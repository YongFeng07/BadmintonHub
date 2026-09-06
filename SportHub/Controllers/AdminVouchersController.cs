using SportHub.Models;
using SportHub.Services;
using SportHub.Data;
using SportHub.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Controllers;

/// <summary>
/// Admin discount-voucher maintenance (revised spec): CRUD over checkout vouchers.
/// Deleting a voucher is safe — reservations keep their applied code for history.
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminVouchersController : Controller
{
    private readonly IVoucherService _voucherService;
    private readonly ApplicationDbContext _db;

    public AdminVouchersController(IVoucherService voucherService, ApplicationDbContext db)
    {
        _voucherService = voucherService;
        _db = db;
    }

    // ---------- List (G-M6: AJAX search/sort/paging + batch delete) ----------

    public async Task<IActionResult> Index(string? search, string? status, string? sort, string? dir,
        int page = 1, int size = 10)
    {
        var request = AjaxListRequest.From(Request.Query);
        var query = _db.Vouchers.AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.Search))
            query = query.Where(v => v.Code.Contains(request.Search) ||
                                     (v.Description != null && v.Description.Contains(request.Search)));

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<VoucherStatus>(status, true, out var parsedStatus))
            query = query.Where(v => v.Status == parsedStatus);

        query = (request.Sort, request.Descending) switch
        {
            ("code", false) => query.OrderBy(v => v.Code),
            ("code", true) => query.OrderByDescending(v => v.Code),
            ("discount", false) => query.OrderBy(v => v.DiscountValue),
            ("discount", true) => query.OrderByDescending(v => v.DiscountValue),
            ("expiry", false) => query.OrderBy(v => v.ExpiryDate),
            ("expiry", true) => query.OrderByDescending(v => v.ExpiryDate),
            ("used", false) => query.OrderBy(v => v.UsageCount),
            ("used", true) => query.OrderByDescending(v => v.UsageCount),
            ("status", false) => query.OrderBy(v => v.Status),
            ("status", true) => query.OrderByDescending(v => v.Status),
            _ => query.OrderByDescending(v => v.Id) // newest first
        };

        var total = await query.CountAsync();
        var pager = AjaxPager.For(request, total);
        pager.Extra["status"] = status ?? "";

        var model = new AdminVoucherIndexViewModel
        {
            StatusFilter = status,
            Page = new AjaxListPage<Voucher>
            {
                Items = await query.Skip((pager.Page - 1) * pager.PageSize).Take(pager.PageSize).ToListAsync(),
                Pager = pager
            }
        };

        if (Request.IsAjaxListRequest())
            return PartialView("_VoucherTable", model);
        return View(model);
    }

    // G-M6 batch deletion: owner-scoped-style bulk action for the voucher list.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBatch(List<int> itemIds)
    {
        var ids = itemIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            TempData["ErrorMessage"] = "Select at least one voucher to delete.";
            return RedirectToAction(nameof(Index));
        }

        var vouchers = await _db.Vouchers.Where(v => ids.Contains(v.Id)).ToListAsync();
        _db.Vouchers.RemoveRange(vouchers);
        await _db.SaveChangesAsync();

        TempData["SuccessMessage"] = $"{vouchers.Count} voucher(s) deleted.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Create ----------

    [HttpGet]
    public IActionResult Create()
    {
        return View(new Voucher { ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30) });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Voucher model)
    {
        if (!ModelState.IsValid)
            return View(model);

        // UsageCount/CreatedAt are system-owned — copy only the editable fields.
        var (success, error, voucher) = await _voucherService.CreateAsync(new Voucher
        {
            Code = model.Code,
            Description = model.Description,
            DiscountType = model.DiscountType,
            DiscountValue = model.DiscountValue,
            StartDate = model.StartDate,
            ExpiryDate = model.ExpiryDate,
            MinSpend = model.MinSpend,
            MaxDiscount = model.MaxDiscount,
            UsageLimit = model.UsageLimit,
            PerUserLimit = model.PerUserLimit,
            Status = model.Status
        });

        if (!success || voucher == null)
        {
            ModelState.AddModelError(string.Empty, error ?? "Could not create the voucher.");
            return View(model);
        }

        TempData["SuccessMessage"] = $"Voucher \"{voucher.Code}\" created.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Edit ----------

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var voucher = await _voucherService.GetByIdAsync(id);
        if (voucher == null) return NotFound();
        return View(voucher);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(Voucher model)
    {
        var voucher = await _voucherService.GetByIdAsync(model.Id);
        if (voucher == null) return NotFound();

        if (!ModelState.IsValid)
        {
            // Keep the system-maintained stats visible on re-render.
            model.UsageCount = voucher.UsageCount;
            model.CreatedAt = voucher.CreatedAt;
            return View(model);
        }

        voucher.Code = model.Code;
        voucher.Description = model.Description;
        voucher.DiscountType = model.DiscountType;
        voucher.DiscountValue = model.DiscountValue;
        voucher.StartDate = model.StartDate;
        voucher.ExpiryDate = model.ExpiryDate;
        voucher.MinSpend = model.MinSpend;
        voucher.MaxDiscount = model.MaxDiscount;
        voucher.UsageLimit = model.UsageLimit;
        voucher.PerUserLimit = model.PerUserLimit;
        voucher.Status = model.Status;

        var (success, error) = await _voucherService.UpdateAsync(voucher);
        if (!success)
        {
            ModelState.AddModelError(string.Empty, error ?? "Could not update the voucher.");
            return View(model);
        }

        TempData["SuccessMessage"] = $"Voucher \"{voucher.Code}\" updated.";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Bulk generate ----------

    /// <summary>
    /// Creates a batch of vouchers from a shared template with generated unique codes
    /// (G-M2). Codes use an unambiguous alphabet and take the form PREFIX-XXXXXX.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkGenerate(int count, string? prefix, Voucher template)
    {
        // Sensible defaults so the form can stay short.
        if (template.DiscountValue <= 0) template.DiscountValue = 10;
        if (template.ExpiryDate == default) template.ExpiryDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30);
        if (string.IsNullOrWhiteSpace(template.Description)) template.Description = "Bulk-generated voucher";

        var (success, error, created, codes) = await _voucherService.BulkGenerateAsync(template, count, prefix);
        if (!success)
            TempData["ErrorMessage"] = error;
        else if (created == 0)
            TempData["ErrorMessage"] = "Could not generate any unique codes — try a different prefix.";
        else
        {
            var sample = string.Join(", ", codes.Take(5)) + (codes.Count > 5 ? ", …" : string.Empty);
            TempData["SuccessMessage"] = $"{created} voucher(s) generated: {sample}";
        }

        return RedirectToAction(nameof(Index));
    }

    // ---------- Delete ----------

    [HttpGet]
    public async Task<IActionResult> Delete(int id)
    {
        var voucher = await _voucherService.GetByIdAsync(id);
        if (voucher == null) return NotFound();
        return View(voucher);
    }

    [HttpPost]
    [ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var voucher = await _voucherService.GetByIdAsync(id);
        if (voucher == null) return NotFound();

        var (success, error) = await _voucherService.DeleteAsync(id);
        if (!success)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToAction(nameof(Index));
        }

        TempData["SuccessMessage"] = $"Voucher \"{voucher.Code}\" deleted.";
        return RedirectToAction(nameof(Index));
    }
}
