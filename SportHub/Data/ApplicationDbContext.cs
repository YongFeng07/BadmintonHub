using SportHub.Models;
using Microsoft.EntityFrameworkCore;

namespace SportHub.Data;

/// <summary>
/// EF Core Code First context. Schema is defined via Data Annotations on the entities.
/// Fluent API is used ONLY where SQL Server requires it: multiple cascade paths would
/// otherwise fail at migration time (e.g. User -> Reservation -> Payment vs User -> Payment).
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<FacilityPhoto> FacilityPhotos => Set<FacilityPhoto>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<CourtPhoto> CourtPhotos => Set<CourtPhoto>();
    public DbSet<CourtAvailability> CourtAvailabilities => Set<CourtAvailability>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<DemoEmail> DemoEmails => Set<DemoEmail>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<VoucherRedemption> VoucherRedemptions => Set<VoucherRedemption>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Deleting a court removes its photos and availability slots (no history value).
        modelBuilder.Entity<Court>()
            .HasMany(c => c.Photos)
            .WithOne(p => p.Court!)
            .HasForeignKey(p => p.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Court>()
            .HasMany(c => c.Availabilities)
            .WithOne(a => a.Court!)
            .HasForeignKey(a => a.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        // A facility's photos are deleted with the facility; its courts are not
        // (a facility with courts must not be deleted — audit history lives there).
        modelBuilder.Entity<Facility>()
            .HasMany(f => f.Photos)
            .WithOne(p => p.Facility!)
            .HasForeignKey(p => p.FacilityId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Facility>()
            .HasMany(f => f.Courts)
            .WithOne(c => c.Facility!)
            .HasForeignKey(c => c.FacilityId)
            .OnDelete(DeleteBehavior.Restrict);

        // A category with facilities cannot be deleted (checked in the controller too).
        modelBuilder.Entity<Category>()
            .HasMany(c => c.Facilities)
            .WithOne(f => f.Category!)
            .HasForeignKey(f => f.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // A court or user with reservations must not be deleted (financial/audit history).
        // Restrict also avoids SQL Server's "multiple cascade paths" error.
        modelBuilder.Entity<Reservation>()
            .HasOne(r => r.Court)
            .WithMany(c => c.Reservations)
            .HasForeignKey(r => r.CourtId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Reservation>()
            .HasOne(r => r.User)
            .WithMany(u => u.Reservations)
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Payment history is preserved with the reservation it belongs to.
        modelBuilder.Entity<Payment>()
            .HasOne(p => p.User)
            .WithMany(u => u.Payments)
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cart and wishlist lines are ephemeral member data: they die with the court,
        // and (like every other user row) are restricted from the user side.
        modelBuilder.Entity<CartItem>()
            .HasOne(i => i.User)
            .WithMany(u => u.CartItems)
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CartItem>()
            .HasOne(i => i.Court)
            .WithMany()
            .HasForeignKey(i => i.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<WishlistItem>()
            .HasOne(i => i.User)
            .WithMany(u => u.WishlistItems)
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<WishlistItem>()
            .HasOne(i => i.Court)
            .WithMany()
            .HasForeignKey(i => i.CourtId)
            .OnDelete(DeleteBehavior.Cascade);

        // Per-member voucher redemption counters die with the voucher or the member.
        modelBuilder.Entity<VoucherRedemption>()
            .HasOne(r => r.Voucher)
            .WithMany(v => v.Redemptions)
            .HasForeignKey(r => r.VoucherId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<VoucherRedemption>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
