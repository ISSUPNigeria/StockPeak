using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockPeak.Application;
using StockPeak.Domain;
using StockPeak.Infrastructure;
using StockPeak.Shared;
namespace StockPeak.API.Endpoints;

public static class MarketEndpoints
{
    public static void Map(RouteGroupBuilder g)
    {
        g.MapGet("/stocks", async (IMarketDataService m) => await m.GetQuotesAsync());
        g.MapGet("/stocks/{symbol}", async (string symbol, int? days, IMarketDataService m) =>
        {
            var h = await m.GetHistoryAsync(symbol, Math.Clamp(days ?? 120, 30, 365));
            if (h is null) return Ctx.Fail("Unknown symbol.", 404);
            var rsi = PeakDetector.Rsi(h.Candles); var (peaks, troughs) = PeakDetector.Find(h.Candles);
            return Results.Ok(new StockDetail(h.Symbol, h.Name, h.Candles[^1].Close, Math.Round(rsi, 1), PeakDetector.Signal(h.Candles, rsi), h.Candles, peaks, troughs));
        });
        g.MapGet("/stocks/{symbol}/predict", async (string symbol, ClaimsPrincipal p, AppDbContext db, IMarketDataService m, IAiPredictor ai) =>
        {
            var u = await db.Users.FindAsync(p.Uid());
            if (u is null || u.EffectivePlan == Plan.Free) return Ctx.Fail("AI forecasts are part of Pro and Elite.", 402);
            var h = await m.GetHistoryAsync(symbol, 90); if (h is null) return Ctx.Fail("Unknown symbol.", 404);
            try { return Results.Ok(await ai.PredictAsync(h.Symbol, h.Candles, PeakDetector.Rsi(h.Candles))); }
            catch { return Ctx.Fail("The AI service is unavailable. Try again shortly.", 502); }
        }).RequireAuthorization();

        g.MapGet("/watchlist", async (ClaimsPrincipal p, AppDbContext db) => { var uid = p.Uid(); return await db.WatchItems.Where(w => w.UserId == uid).Select(w => w.Symbol).ToListAsync(); }).RequireAuthorization();
        g.MapPost("/watchlist/{symbol}", async (string symbol, ClaimsPrincipal p, AppDbContext db) =>
        {
            var id = p.Uid(); var u = await db.Users.FindAsync(id); symbol = symbol.ToUpperInvariant();
            var n = await db.WatchItems.CountAsync(w => w.UserId == id);
            if (u!.EffectivePlan == Plan.Free && n >= 3) return Ctx.Fail("Free plans can watch 3 stocks. Upgrade for unlimited.", 402);
            if (!await db.WatchItems.AnyAsync(w => w.UserId == id && w.Symbol == symbol)) { db.WatchItems.Add(new() { UserId = id, Symbol = symbol }); await db.SaveChangesAsync(); }
            return Results.Ok();
        }).RequireAuthorization();
        g.MapDelete("/watchlist/{symbol}", async (string symbol, ClaimsPrincipal p, AppDbContext db) =>
        { var uid = p.Uid(); var s = symbol.ToUpperInvariant(); await db.WatchItems.Where(w => w.UserId == uid && w.Symbol == s).ExecuteDeleteAsync(); return Results.Ok(); }).RequireAuthorization();

        g.MapGet("/notifications", async (ClaimsPrincipal p, AppDbContext db) => { var uid = p.Uid(); return await db.Notifications.Where(n => n.UserId == uid).OrderByDescending(n => n.CreatedAt).Take(30).ToListAsync(); }).RequireAuthorization();
        g.MapPost("/notifications/read", async (ClaimsPrincipal p, AppDbContext db) =>
        { var uid = p.Uid(); await db.Notifications.Where(n => n.UserId == uid && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true)); return Results.Ok(); }).RequireAuthorization();
    }
}

public static class BillingEndpoints
{
    public static void Map(RouteGroupBuilder g)
    {
        g.MapPost("/checkout", async (CheckoutRequest r, ClaimsPrincipal p, HttpContext ctx, AppDbContext db, IPaymentGateway gw) =>
        {
            if (r.Plan == Plan.Free) return Ctx.Fail("Choose Pro or Elite.");
            var u = (await db.Users.FindAsync(p.Uid()))!; var amount = PlanPrices.Of(r.Plan); var tx = "sp_" + Guid.NewGuid().ToString("N");
            db.Payments.Add(new() { UserId = u.Id, TxRef = tx, Plan = r.Plan, Amount = amount }); await db.SaveChangesAsync();
            try { return Results.Ok(new { link = await gw.CreateCheckoutLinkAsync(tx, amount, u.Email, u.Name, $"{ctx.BaseUrl()}/api/billing/callback") }); }
            catch { return Ctx.Fail("Payments are unavailable right now. Try again shortly.", 502); }
        }).RequireAuthorization();

        g.MapGet("/callback", async (
     string? status,
     string? tx_ref,
     string? transaction_id,
     BillingService bs,
     ILogger<Program> log) =>
        {
            log.LogInformation(
                "CALLBACK HIT status={Status} tx_ref={TxRef} tx_id={TxId}",
                status,
                tx_ref,
                transaction_id);

            var ok =
                status == "successful" &&
                tx_ref is not null &&
                transaction_id is not null &&
                await bs.ActivateAsync(tx_ref, transaction_id);

            log.LogInformation(
                "CALLBACK RESULT={Result}",
                ok);

            return Results.Redirect(
                ok ? "/?paid=1" : "/?paid=0");
        });

        g.MapPost("/webhook", async (HttpRequest req, IConfiguration cfg, BillingService bs, JsonElement body) =>
        {
            if (req.Headers["verif-hash"] != cfg["Flutterwave:SecretHash"]) return Results.Unauthorized();
            if (body.TryGetProperty("data", out var d)) await bs.ActivateAsync(d.GetProperty("tx_ref").GetString()!, d.GetProperty("id").GetInt64().ToString());
            return Results.Ok();
        });
    }
}
