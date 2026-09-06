using SportHub.Models;

namespace SportHub.ViewModels;

/// <summary>
/// G-M4 wishlist index: each saved court carries its true availability over the
/// next few days so the card can show a real badge and a "Book Now" button that
/// jumps straight to the first open date.
/// </summary>
public class WishlistItemViewModel
{
    public WishlistItem Item { get; set; } = null!;

    public bool HasOpenSlots { get; set; }

    public int OpenSlotCount { get; set; }

    public DateOnly? FirstOpenDate { get; set; }
}

public class WishlistIndexViewModel
{
    public List<WishlistItemViewModel> Items { get; set; } = new();
}
