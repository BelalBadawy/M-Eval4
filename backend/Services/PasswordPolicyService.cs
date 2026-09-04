using MEval.Api.Configuration;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

namespace MEval.Api.Services;

public interface IPasswordPolicyService
{
    (bool IsValid, string? ErrorMessage) ValidatePassword(string password);
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}

public class PasswordPolicyService : IPasswordPolicyService
{
    private readonly SecuritySettings _securitySettings;

    public PasswordPolicyService(IOptions<SecuritySettings> securitySettings)
    {
        _securitySettings = securitySettings.Value;
    }

    public (bool IsValid, string? ErrorMessage) ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return (false, "Password cannot be empty.");
        }

        if (password.Length < 8)
        {
            return (false, "Password must be at least 8 characters long.");
        }

        if (!password.Any(char.IsUpper))
        {
            return (false, "Password must contain at least one uppercase letter.");
        }

        if (!password.Any(char.IsLower))
        {
            return (false, "Password must contain at least one lowercase letter.");
        }

        if (!password.Any(char.IsDigit))
        {
            return (false, "Password must contain at least one digit.");
        }

        if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            return (false, "Password must contain at least one special character.");
        }

        var defaultPassword = _securitySettings.DefaultUserPassword ?? "Mina@123";
        if (string.Equals(password, defaultPassword, StringComparison.Ordinal))
        {
            return (false, "New password cannot match the default temporary password.");
        }

        return (true, null);
    }

    public string HashPassword(string password)
    {
        var workFactor = _securitySettings.BcryptWorkFactor > 0 ? _securitySettings.BcryptWorkFactor : 11;
        return BCrypt.Net.BCrypt.HashPassword(password, workFactor);
    }

    public bool VerifyPassword(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch
        {
            return false;
        }
    }
}
