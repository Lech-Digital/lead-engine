namespace Coldstart.Web;

public static class ColdstartRoles
{
    public const string SuperAdmin = "SuperAdmin";
    public const string CompanyAdmin = "CompanyAdmin";
    public const string User = "User";

    // Hardcoded operator identity — seeded on startup, sets its own password via Forgot password.
    public const string SuperAdminEmail = "adzius.lech@gmail.com";

    public static readonly string[] All = [SuperAdmin, CompanyAdmin, User];
}
