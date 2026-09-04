namespace MEval.Api.DTOs;

public record LoginRequest(string Email, string Password);

public record UserSummaryDto(
    int Id,
    string FullName,
    string Email,
    bool MustChangePassword,
    List<string> Roles,
    List<string> Permissions
);

public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    UserSummaryDto User
);

public record RefreshTokenRequest(string RefreshToken);

public record RefreshTokenResponse(
    string AccessToken,
    string RefreshToken
);

public record ChangePasswordRequest(
    string CurrentPassword,
    string NewPassword
);

public record SessionResponse(
    int UserId,
    string FullName,
    string Email,
    bool MustChangePassword,
    List<string> Roles,
    List<string> Permissions,
    string? ClientIp
);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);
