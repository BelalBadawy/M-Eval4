using System.Text.Json;
using MEval.Api.Configuration;
using MEval.Api.Data;
using MEval.Api.Models;
using MEval.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MEval.Api.Workers;

public class DefaultPasswordInactivityWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DefaultPasswordInactivityWorker> _logger;
    private readonly SecuritySettings _securitySettings;

    public DefaultPasswordInactivityWorker(
        IServiceProvider serviceProvider,
        ILogger<DefaultPasswordInactivityWorker> logger,
        IOptions<SecuritySettings> securitySettings)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _securitySettings = securitySettings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DefaultPasswordInactivityWorker started.");

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessInactivityScanAsync(stoppingToken);
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during DefaultPasswordInactivityWorker scan.");
            }
        }

        _logger.LogInformation("DefaultPasswordInactivityWorker stopped.");
    }

    public async Task<int> ProcessInactivityScanAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var gracePeriodDays = _securitySettings.DefaultPasswordGracePeriodDays > 0
            ? _securitySettings.DefaultPasswordGracePeriodDays
            : 14;

        var cutoffDate = DateTime.UtcNow.AddDays(-gracePeriodDays);

        // Scan for active accounts still on default password created/reset before cutoff
        var inactiveUsers = await context.Users
            .Where(u => u.IsActive &&
                        u.MustChangePassword &&
                        u.SoftDeletedAtUtc == null &&
                        (u.PasswordChangedAtUtc ?? u.CreatedAtUtc) < cutoffDate)
            .ToListAsync(cancellationToken);

        if (inactiveUsers.Count == 0)
        {
            return 0;
        }

        // Fetch system administrator emails for notification
        var adminEmails = await context.UserRoles
            .Where(ur => (ur.Role.Name == "Admin" || ur.Role.Name == "Super Admin") &&
                         ur.User.IsActive &&
                         ur.User.SoftDeletedAtUtc == null)
            .Select(ur => ur.User.Email)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var user in inactiveUsers)
        {
            user.IsActive = false;
            user.UpdatedAtUtc = DateTime.UtcNow;

            // Revoke active session
            await tokenService.RevokeActiveSessionAsync(user.Id, RevokeReasons.AccountDeactivated);

            // Create security audit log
            context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                Action = "User.AutoSuspended",
                EntityType = "User",
                EntityId = user.Id.ToString(),
                Details = JsonSerializer.Serialize(new
                {
                    Reason = $"Account remained on temporary default password for more than {gracePeriodDays} days.",
                    CutoffDate = cutoffDate,
                    UserEmail = user.Email
                }),
                IpAddress = "System.BackgroundWorker",
                TimestampUtc = DateTime.UtcNow
            });

            // Notify admins
            foreach (var adminEmail in adminEmails)
            {
                await emailService.SendDefaultPasswordSuspendedAlertAsync(adminEmail, user.FullName);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("DefaultPasswordInactivityWorker auto-suspended {Count} inactive user accounts.", inactiveUsers.Count);

        return inactiveUsers.Count;
    }
}
