using BadmintonHub.Models;
using Microsoft.EntityFrameworkCore;

namespace BadmintonHub.Data;

/// <summary>
/// EF Core Code First context. Schema is defined via Data Annotations on the entities.
/// Fluent API is used ONLY where SQL Server requires it: multiple cascade paths would
/// otherwise fail at migration time (e.g. User -> Reservation -> Payment vs User -> Payment).
/// </summary>
public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<CourtPhoto> CourtPhotos => Set<CourtPhoto>();
    public DbSet<CourtAvailability> CourtAvailabilities => Set<CourtAvailability>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

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
    }
}
