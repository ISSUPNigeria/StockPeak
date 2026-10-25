using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using StockPeak.Domain;
using StockPeak.Infrastructure;
using StockPeak.Shared;
namespace StockPeak.API.Endpoints;

/// <summary>Trade journal: log a trade when you open it, then record what you did and learned when you complete it.</summary>
public static class TradeEndpoints
{
    static string Csv(object? v) { var s = v?.ToString() ?? ""; if (s.Length > 0 && "=+-@".Contains(s[0])) s = "'" + s; return "\"" + s.Replace("\"", "\"\"") + "\""; }

    public static void Map(RouteGroupBuilder g)
    {
        g.MapPost("/", async (OpenTradeRequest r, ClaimsPrincipal p, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(r.Symbol) || r.Quantity <= 0 || r.EntryPrice <= 0) return Ctx.Fail("Enter a symbol, a quantity above 0 and an entry price above 0.");
            var t = new Trade { UserId = p.Uid(), WorkspaceId = p.Wid(), Symbol = r.Symbol.Trim().ToUpperInvariant(), Side = r.Side, Quantity = r.Quantity, EntryPrice = r.EntryPrice, Fees = Math.Max(0, r.Fees), Strategy = r.Strategy, Reason = r.Reason };
            db.Trades.Add(t); await db.SaveChangesAsync();
            return Results.Ok(t);
        });

        g.MapGet("/", async (bool? all, string? status, ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.Uid(); var wid = p.Wid(); var team = p.IsInRole("Admin") || p.IsInRole("Manager") || p.IsInRole("Agent");
            var q = db.Trades.Where(t => t.WorkspaceId == wid);
            if (!(all == true && team)) q = q.Where(t => t.UserId == uid);
            if (status == "open") q = q.Where(t => t.ClosedAt == null); else if (status == "closed") q = q.Where(t => t.ClosedAt != null);
            return await q.OrderByDescending(t => t.OpenedAt).Take(200).ToListAsync();
        });

        // Completing a trade records the exit and your review of it.
        g.MapPost("/{id:guid}/close", async (Guid id, CloseTradeRequest r, ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.Uid(); var t = await db.Trades.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
            if (t is null) return Ctx.Fail("Trade not found.", 404);
            if (t.ClosedAt != null) return Ctx.Fail("This trade is already closed.", 409);
            if (r.ExitPrice <= 0) return Ctx.Fail("Enter the exit price.");
            t.ExitPrice = r.ExitPrice; t.Fees += Math.Max(0, r.Fees); t.ClosedAt = DateTime.UtcNow;
            t.WhatIDid = r.WhatIDid; t.WhatWentWell = r.WhatWentWell; t.WhatToImprove = r.WhatToImprove;
            await db.SaveChangesAsync();
            return Results.Ok(t);
        });

        g.MapPut("/{id:guid}/notes", async (Guid id, TradeNotes n, ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.Uid(); var t = await db.Trades.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid);
            if (t is null) return Ctx.Fail("Trade not found.", 404);
            t.Strategy = n.Strategy; t.Reason = n.Reason; t.WhatIDid = n.WhatIDid; t.WhatWentWell = n.WhatWentWell; t.WhatToImprove = n.WhatToImprove;
            await db.SaveChangesAsync();
            return Results.Ok(t);
        });

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDbContext db) =>
        { var uid = p.Uid(); await db.Trades.Where(t => t.Id == id && t.UserId == uid).ExecuteDeleteAsync(); return Results.Ok(); });

        g.MapGet("/stats", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.Uid(); var wid = p.Wid();
            var all = await db.Trades.Where(t => t.UserId == uid && t.WorkspaceId == wid).ToListAsync();
            var c = all.Where(t => t.ClosedAt != null).ToList(); var wins = c.Count(t => t.Pnl > 0);
            return new TradeStats(all.Count, all.Count - c.Count, c.Count, wins, c.Count == 0 ? 0 : Math.Round(wins * 100.0 / c.Count, 1), c.Sum(t => t.Pnl ?? 0), c.Count == 0 ? 0 : c.Max(t => t.Pnl ?? 0), c.Count == 0 ? 0 : c.Min(t => t.Pnl ?? 0));
        });

        g.MapGet("/export", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.Uid(); var wid = p.Wid();
            var rows = await db.Trades.Where(t => t.UserId == uid && t.WorkspaceId == wid).OrderBy(t => t.OpenedAt).ToListAsync();
            var sb = new StringBuilder("Symbol,Side,Quantity,Entry,Exit,Fees,PnL,Opened,Closed,Strategy,Why I entered,What I did,What went well,What to improve\n");
            foreach (var t in rows) sb.AppendLine(string.Join(",", new object?[] { t.Symbol, t.Side, t.Quantity, t.EntryPrice, t.ExitPrice, t.Fees, t.Pnl, t.OpenedAt.ToString("u"), t.ClosedAt?.ToString("u"), t.Strategy, t.Reason, t.WhatIDid, t.WhatWentWell, t.WhatToImprove }.Select(Csv)));
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "stockpeak-journal.csv");
        });
    }
}
