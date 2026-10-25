namespace StockPeak.Shared;
public enum Plan { Free, Pro, Elite }
public enum Role { User, Merchant, Agent, Manager, Admin }
public enum OtpPurpose { EmailVerification, PasswordReset }
public enum TradeSide { Buy, Sell }

public record RegisterRequest(string Name, string Email, string Password, Role Role = Role.User, string? WorkspaceName = null);
public record LoginRequest(string Email, string Password, Guid? WorkspaceId = null);
public record EmailRequest(string Email);
public record VerificationCodeRequest(string Email, OtpPurpose Purpose);
public record ConfirmOtpRequest(string Email, string Code);
public record ResetPasswordRequest(string Email, string Code, string NewPassword);
public record WorkspaceInfo(Guid Id, string Name, Role Role);
public record AuthResponse(string Token, string Name, string Email, Plan Plan, Role Role, Guid WorkspaceId, IReadOnlyList<WorkspaceInfo> Workspaces);
public record CreateWorkspaceRequest(string Name);
public record InviteRequest(string Email, Role Role);
public record AcceptInviteRequest(string Token, string? Name, string? Password);
public record ChangeRoleRequest(Role Role);
public record CheckoutRequest(Plan Plan);

public record OpenTradeRequest(string Symbol, TradeSide Side, decimal Quantity, decimal EntryPrice, decimal Fees, string? Strategy, string? Reason);
public record CloseTradeRequest(decimal ExitPrice, decimal Fees, string? WhatIDid, string? WhatWentWell, string? WhatToImprove);
public record TradeNotes(string? Strategy, string? Reason, string? WhatIDid, string? WhatWentWell, string? WhatToImprove);
public record TradeStats(int Total, int Open, int Closed, int Wins, double WinRate, decimal TotalPnl, decimal BestTrade, decimal WorstTrade);

public record Candle(DateTime Date, decimal Close);
public record PeakPoint(int Index, DateTime Date, decimal Price, double Strength);
public record Quote(string Symbol, string Name, decimal Price, decimal ChangePct, double Rsi, string Signal);
public record StockHistory(string Symbol, string Name, IReadOnlyList<Candle> Candles);
public record StockDetail(string Symbol, string Name, decimal Price, double Rsi, string Signal,
    IReadOnlyList<Candle> History, IReadOnlyList<PeakPoint> Peaks, IReadOnlyList<PeakPoint> Troughs);
public record Prediction(string Direction, decimal TargetPrice, int Confidence, string Reasoning, string Disclaimer);
public static class PlanPrices { public static decimal Of(Plan p) => p switch { Plan.Pro => 15m, Plan.Elite => 39m, _ => 0m }; }
