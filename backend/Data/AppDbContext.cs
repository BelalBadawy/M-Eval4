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

    // Module 2 — Organization & Employee Hierarchy
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Section> Sections => Set<Section>();
    public DbSet<Position> Positions => Set<Position>();
    public DbSet<Employee> Employees => Set<Employee>();

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

        // Module 2 — Company
        modelBuilder.Entity<Company>(b =>
        {
            b.HasKey(c => c.CompanyId);
            b.Property(c => c.CompanyId).ValueGeneratedNever();
            b.Property(c => c.Name).IsRequired().HasMaxLength(200);
        });

        // Module 2 — Department
        modelBuilder.Entity<Department>(b =>
        {
            b.HasKey(d => d.DepartmentId);
            b.Property(d => d.DepartmentId).ValueGeneratedNever();
            b.Property(d => d.Name).IsRequired().HasMaxLength(200);

            // Composite alternate key (target for Employee composite FK)
            b.HasAlternateKey(d => new { d.DepartmentId, d.CompanyId });

            b.HasOne(d => d.Company)
                .WithMany(c => c.Departments)
                .HasForeignKey(d => d.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Module 2 — Section
        modelBuilder.Entity<Section>(b =>
        {
            b.HasKey(s => s.SectionId);
            b.Property(s => s.SectionId).ValueGeneratedNever();
            b.Property(s => s.Name).IsRequired().HasMaxLength(200);

            // Composite alternate key (target for Employee composite FK)
            b.HasAlternateKey(s => new { s.SectionId, s.DepartmentId });

            b.HasOne(s => s.Department)
                .WithMany(d => d.Sections)
                .HasForeignKey(s => s.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Module 2 — Position
        modelBuilder.Entity<Position>(b =>
        {
            b.HasKey(p => p.PositionId);
            b.Property(p => p.PositionId).ValueGeneratedNever();
            b.Property(p => p.Name).IsRequired().HasMaxLength(200);

            b.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Positions_NLevel", "[NLevel] >= 1");
            });
        });

        // Module 2 — Employee
        modelBuilder.Entity<Employee>(b =>
        {
            b.HasKey(e => e.EmployeeId);
            b.Property(e => e.EmployeeId).ValueGeneratedNever();
            b.Property(e => e.EmployeeNumber).IsRequired().HasMaxLength(30);
            b.Property(e => e.FullName).IsRequired().HasMaxLength(200);
            b.Property(e => e.Email).HasMaxLength(200);

            b.HasIndex(e => e.EmployeeNumber).IsUnique();

            // Unique filtered index on Email (when not null and IsActive = 1)
            b.HasIndex(e => e.Email)
                .IsUnique()
                .HasFilter("[Email] IS NOT NULL AND [IsActive] = 1");

            // Unique filtered index on UserId (1:1 link to Users table)
            b.HasIndex(e => e.UserId)
                .IsUnique()
                .HasFilter("[UserId] IS NOT NULL");

            b.HasIndex(e => e.DirectManagerId);
            b.HasIndex(e => e.DepartmentId);
            b.HasIndex(e => e.SectionId);
            b.HasIndex(e => e.EmploymentStatus);

            b.HasOne(e => e.Company)
                .WithMany(c => c.Employees)
                .HasForeignKey(e => e.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(e => e.Position)
                .WithMany(p => p.Employees)
                .HasForeignKey(e => e.PositionId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(e => e.DirectManager)
                .WithMany(m => m.DirectReports)
                .HasForeignKey(e => e.DirectManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(e => e.User)
                .WithOne()
                .HasForeignKey<Employee>(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Composite FK: (DepartmentId, CompanyId) -> Departments
            b.HasOne(e => e.Department)
                .WithMany(d => d.Employees)
                .HasForeignKey(e => new { e.DepartmentId, e.CompanyId })
                .HasPrincipalKey(d => new { d.DepartmentId, d.CompanyId })
                .OnDelete(DeleteBehavior.Restrict);

            // Composite FK: (SectionId, DepartmentId) -> Sections
            b.HasOne(e => e.Section)
                .WithMany(s => s.Employees)
                .HasForeignKey(e => new { e.SectionId, e.DepartmentId })
                .HasPrincipalKey(s => new { s.SectionId, s.DepartmentId })
                .OnDelete(DeleteBehavior.Restrict);

            b.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Empl_SectionNeedsDept", "[SectionId] IS NULL OR [DepartmentId] IS NOT NULL");
                t.HasCheckConstraint("CK_Empl_StatusDates", "([EmploymentStatus] = 1 AND [ResignationDate] IS NULL) OR ([EmploymentStatus] IN (2, 3) AND [ResignationDate] IS NOT NULL)");
                t.HasCheckConstraint("CK_Empl_ResignationAfterHire", "[ResignationDate] IS NULL OR [ResignationDate] >= [HireDate]");
            });
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
