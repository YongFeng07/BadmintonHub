using SportHub.Models;
using SportHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SportHub.Controllers;

/// <summary>
/// Admin discount-voucher maintenance (revised spec): CRUD over checkout vouchers.
/// Deleting a voucher is safe — reservations keep their applied code for history.
/// </summary>
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminVouchersController : Controller
{
    private readonly IVoucherService _voucherService;

    public AdminVouchersController(IVoucherService voucherService)
    {
        _voucherService = voucherService;
    }

    // ---------- List ----------

    public async Task<IActionResult> Index()
    {
        return View(await _voucherService.GetAllAsync());
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
