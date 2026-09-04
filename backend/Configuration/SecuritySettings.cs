namespace MEval.Api.Configuration;

public class SecuritySettings
{
    public const string SectionName = "SecuritySettings";

    public string DefaultUserPassword { get; set; } = "Mina@123";
    public int BcryptWorkFactor { get; set; } = 11;
    public int LockoutThreshold { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 15;
    public int DefaultPasswordGracePeriodDays { get; set; } = 14;
    public int MaxImportFileSizeMb { get; set; } = 5;
    public int MaxImportRows { get; set; } = 5000;
}
