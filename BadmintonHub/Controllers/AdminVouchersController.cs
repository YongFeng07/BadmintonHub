using BadmintonHub.Models;
using BadmintonHub.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BadmintonHub.Controllers;

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
            ExpiryDate = model.ExpiryDate,
            UsageLimit = model.UsageLimit,
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
        voucher.ExpiryDate = model.ExpiryDate;
        voucher.UsageLimit = model.UsageLimit;
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
