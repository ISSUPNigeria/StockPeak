using StockPeak.Shared;
namespace StockPeak.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool EmailVerified { get; set; }
    public Plan Plan { get; set; } = Plan.Free;
    public DateTime? PlanExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Plan EffectivePlan => PlanExpiresAt > DateTime.UtcNow ? Plan : Plan.Free;
}
/// <summary>A team or business space. One email can belong to many workspaces, each with its own role.</summary>
public class Workspace { public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = ""; public Guid OwnerId { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public class Membership { public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public Guid WorkspaceId { get; set; } public Role Role { get; set; } }
public class OtpCode { public Guid Id { get; set; } = Guid.NewGuid(); public string Email { get; set; } = ""; public OtpPurpose Purpose { get; set; } public string CodeHash { get; set; } = ""; public DateTime ExpiresAt { get; set; } public int Attempts { get; set; } public bool Used { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public class Invite { public Guid Id { get; set; } = Guid.NewGuid(); public Guid WorkspaceId { get; set; } public string Email { get; set; } = ""; public Role Role { get; set; } public string TokenHash { get; set; } = ""; public Guid InvitedById { get; set; } public DateTime ExpiresAt { get; set; } public DateTime? AcceptedAt { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public class Notification { public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public string Title { get; set; } = ""; public string Body { get; set; } = ""; public bool IsRead { get; set; } public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }
public class WatchItem { public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public string Symbol { get; set; } = ""; }
public class Payment { public Guid Id { get; set; } = Guid.NewGuid(); public Guid UserId { get; set; } public string TxRef { get; set; } = ""; public Plan Plan { get; set; } public decimal Amount { get; set; } public string Status { get; set; } = "pending"; public DateTime CreatedAt { get; set; } = DateTime.UtcNow; }

/// <summary>Trade journal entry. The review fields are filled in when the trade is completed.</summary>
public class Trade
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Symbol { get; set; } = "";
    public TradeSide Side { get; set; }
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal Fees { get; set; }
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public string? Strategy { get; set; }
    public string? Reason { get; set; }
    public string? WhatIDid { get; set; }
    public string? WhatWentWell { get; set; }
    public string? WhatToImprove { get; set; }
    public decimal? Pnl => ExitPrice is null ? null : (ExitPrice.Value - EntryPrice) * Quantity * (Side == TradeSide.Buy ? 1 : -1) - Fees;
}
