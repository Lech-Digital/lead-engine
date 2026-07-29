namespace Coldstart.Web.Api;

public sealed record AdminUserDto(string Email, string Role, string Status, int? InviteDaysLeft);

public sealed record AdminCustomerDto(
    Guid Id, string Name, string Plan, DateTimeOffset? PlanRenewsAt,
    bool HasStripeCustomer, bool HasSubscription,
    string HarvestStatus, DateTimeOffset? LastHarvestAt,
    int LeadsTotal, int LeadsPendingReview,
    IReadOnlyList<AdminUserDto> Users);

public sealed record AdminStatsDto(
    string Database, int Customers, int Users, int PendingInvites, int BlockedUsers,
    Dictionary<string, int> HarvestStatuses);

public sealed record InviteRequestDto(string? Email, string? Company, string? Niche, string? Info);
