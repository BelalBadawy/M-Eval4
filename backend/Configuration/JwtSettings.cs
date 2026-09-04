namespace MEval.Api.Configuration;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = "MEval_SuperSecretKey_ForDevelopment_MustBeAtLeast32BytesLong!";
    public string Issuer { get; set; } = "MEval.Api";
    public string Audience { get; set; } = "MEval.Client";
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;
    public int PasswordResetTokenExpirationMinutes { get; set; } = 30;
}
