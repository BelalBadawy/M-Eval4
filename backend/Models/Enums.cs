namespace MEval.Api.Models;

public enum UserSource
{
    Manual = 1,
    Imported = 2
}

public enum ImportStatus
{
    Pending = 1,
    Validated = 2,
    Processing = 3,
    Completed = 4,
    Failed = 5,
    Cancelled = 6,
    RolledBack = 7
}

public enum DuplicateStrategy
{
    Skip = 1,
    Update = 2,
    FailRow = 3
}

public enum CommitPolicy
{
    AllOrNothing = 1,
    PartialValidOnly = 2
}

public enum RowStatus
{
    Pending = 0,
    Valid = 1,
    Invalid = 2,
    DuplicateInFile = 3,
    DuplicateInDb = 4,
    Skipped = 5,
    Created = 6,
    Updated = 7,
    Failed = 8
}

public static class RevokeReasons
{
    public const string SupersededByNewLogin = "SupersededByNewLogin";
    public const string Rotated = "Rotated";
    public const string SuspiciousReplay = "SuspiciousReplay";
    public const string UserLogout = "UserLogout";
    public const string PasswordChanged = "PasswordChanged";
    public const string PasswordReset = "PasswordReset";
    public const string AdminForceReset = "AdminForceReset";
    public const string AdminForceLogout = "AdminForceLogout";
    public const string AccountDeactivated = "AccountDeactivated";
    public const string RoleDowngraded = "RoleDowngraded";
    public const string BatchRolledBack = "BatchRolledBack";
}
