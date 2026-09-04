using MEval.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MEval.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportBatchRow> ImportBatchRows => Set<ImportBatchRow>();
    public DbSet<ImportRowError> ImportRowErrors => Set<ImportRowError>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(u => u.Id);
            b.Property(u => u.Email).IsRequired().HasMaxLength(255);
            b.Property(u => u.FullName).IsRequired().HasMaxLength(150);
            b.Property(u => u.PasswordHash).IsRequired().HasMaxLength(255);

            // Filtered unique index: allows re-creating an email if soft deleted
            b.HasIndex(u => u.Email)
                .IsUnique()
                .HasFilter("[SoftDeletedAtUtc] IS NULL");

            // Global soft-delete query filter
            b.HasQueryFilter(u => u.SoftDeletedAtUtc == null);

            b.HasOne(u => u.ImportBatch)
                .WithMany(b => b.CreatedUsers)
                .HasForeignKey(u => u.ImportBatchId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Role
        modelBuilder.Entity<Role>(b =>
        {
            b.HasKey(r => r.Id);
            b.Property(r => r.Name).IsRequired().HasMaxLength(50);
            b.HasIndex(r => r.Name).IsUnique();
        });

        // Permission
        modelBuilder.Entity<Permission>(b =>
        {
            b.HasKey(p => p.Id);
            b.Property(p => p.Code).IsRequired().HasMaxLength(100);
            b.HasIndex(p => p.Code).IsUnique();
        });

        // UserRole (Junction)
        modelBuilder.Entity<UserRole>(b =>
        {
            b.HasKey(ur => new { ur.UserId, ur.RoleId });

            b.HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(ur => ur.AssignedByUser)
                .WithMany()
                .HasForeignKey(ur => ur.AssignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(ur => ur.User.SoftDeletedAtUtc == null);
        });

        // RolePermission (Junction)
        modelBuilder.Entity<RolePermission>(b =>
        {
            b.HasKey(rp => new { rp.RoleId, rp.PermissionId });

            b.HasOne(rp => rp.Role)
                .WithMany(r => r.RolePermissions)
                .HasForeignKey(rp => rp.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasOne(rp => rp.Permission)
                .WithMany(p => p.RolePermissions)
                .HasForeignKey(rp => rp.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // RefreshToken (Single active session per user)
        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.HasKey(rt => rt.Id);
            b.Property(rt => rt.TokenHash).IsRequired().HasMaxLength(255);

            // Filtered unique index: at most one unrevoked token per user
            b.HasIndex(rt => rt.UserId)
                .IsUnique()
                .HasFilter("[RevokedAtUtc] IS NULL");

            b.HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(rt => rt.User.SoftDeletedAtUtc == null);
        });

        // PasswordResetToken
        modelBuilder.Entity<PasswordResetToken>(b =>
        {
            b.HasKey(prt => prt.Id);
            b.Property(prt => prt.TokenHash).IsRequired().HasMaxLength(255);

            b.HasOne(prt => prt.User)
                .WithMany(u => u.PasswordResetTokens)
                .HasForeignKey(prt => prt.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasQueryFilter(prt => prt.User.SoftDeletedAtUtc == null);
        });

        // ImportBatch
        modelBuilder.Entity<ImportBatch>(b =>
        {
            b.HasKey(ib => ib.Id);
            b.Property(ib => ib.FileName).IsRequired().HasMaxLength(255);

            b.HasOne(ib => ib.CreatedByUser)
                .WithMany()
                .HasForeignKey(ib => ib.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasQueryFilter(ib => ib.CreatedByUser.SoftDeletedAtUtc == null);
        });

        // ImportBatchRow (Staging)
        modelBuilder.Entity<ImportBatchRow>(b =>
        {
            b.HasKey(ibr => ibr.Id);
            b.Property(ibr => ibr.FullName).IsRequired().HasMaxLength(150);
            b.Property(ibr => ibr.Email).IsRequired().HasMaxLength(255);

            b.HasOne(ibr => ibr.Batch)
                .WithMany(b => b.Rows)
                .HasForeignKey(ibr => ibr.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(ibr => ibr.BatchId);
        });

        // ImportRowError
        modelBuilder.Entity<ImportRowError>(b =>
        {
            b.HasKey(ire => ire.Id);
            b.Property(ire => ire.ColumnName).IsRequired().HasMaxLength(50);
            b.Property(ire => ire.Reason).IsRequired().HasMaxLength(255);
            b.Property(ire => ire.RawValue).HasMaxLength(100);

            b.HasOne(ire => ire.Batch)
                .WithMany(b => b.Errors)
                .HasForeignKey(ire => ire.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(ire => ire.BatchId);
        });

        // AuditLog (Immutable)
        modelBuilder.Entity<AuditLog>(b =>
        {
            b.HasKey(al => al.Id);
            b.Property(al => al.Action).IsRequired().HasMaxLength(100);
            b.Property(al => al.EntityType).IsRequired().HasMaxLength(50);

            b.HasOne(al => al.ActorUser)
                .WithMany()
                .HasForeignKey(al => al.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(al => al.TimestampUtc);
            b.HasIndex(al => al.ActorUserId);
        });
    }

    public override int SaveChanges()
    {
        EnforceAuditImmutability();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceAuditImmutability();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void EnforceAuditImmutability()
    {
        var forbiddenEntries = ChangeTracker.Entries<AuditLog>()
            .Where(e => e.State == EntityState.Modified || e.State == EntityState.Deleted);

        if (forbiddenEntries.Any())
        {
            throw new InvalidOperationException("AuditLog records are strictly immutable and cannot be updated or deleted.");
        }
    }
}
