namespace MEval.Api.Services;

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(string toEmail, string resetToken);
    Task SendPasswordChangedNotificationAsync(string toEmail);
    Task SendTemporaryPasswordEmailAsync(string toEmail, string temporaryPassword);
    Task SendDefaultPasswordSuspendedAlertAsync(string toEmail, string userFullName);
    Task SendImportCompletedNotificationAsync(string toEmail, int batchId, int totalRows, int createdRows);
}

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public Task SendPasswordResetEmailAsync(string toEmail, string resetToken)
    {
        _logger.LogInformation("[EmailService] Password reset token dispatched to {Email}. Token: {Token}", toEmail, resetToken);
        return Task.CompletedTask;
    }

    public Task SendPasswordChangedNotificationAsync(string toEmail)
    {
        _logger.LogInformation("[EmailService] Security notice: Password was changed for account {Email}", toEmail);
        return Task.CompletedTask;
    }

    public Task SendTemporaryPasswordEmailAsync(string toEmail, string temporaryPassword)
    {
        _logger.LogInformation("[EmailService] Temporary password email dispatched to {Email}.", toEmail);
        return Task.CompletedTask;
    }

    public Task SendDefaultPasswordSuspendedAlertAsync(string toEmail, string userFullName)
    {
        _logger.LogWarning("[EmailService] Administrator alert sent to {Email}: User {FullName} account was auto-suspended (remained on default password beyond grace period).", toEmail, userFullName);
        return Task.CompletedTask;
    }

    public Task SendImportCompletedNotificationAsync(string toEmail, int batchId, int totalRows, int createdRows)
    {
        _logger.LogInformation("[EmailService] Import batch {BatchId} completed for {Email}. Total rows: {TotalRows}, Created rows: {CreatedRows}", batchId, toEmail, totalRows, createdRows);
        return Task.CompletedTask;
    }
}
