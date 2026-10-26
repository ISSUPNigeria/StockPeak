using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using StockPeak.Application;
using StockPeak.Domain;
using StockPeak.Shared;

namespace StockPeak.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> o) : DbContext(o)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<Invite> Invites => Set<Invite>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<WatchItem> WatchItems => Set<WatchItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Trade> Trades => Set<Trade>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<User>(e => { e.HasIndex(u => u.Email).IsUnique(); e.Property(u => u.Plan).HasConversion<string>(); });
        m.Entity<Membership>(e => { e.HasIndex(x => new { x.UserId, x.WorkspaceId }).IsUnique(); e.Property(x => x.Role).HasConversion<string>(); });
        m.Entity<Invite>(e => { e.HasIndex(x => x.TokenHash).IsUnique(); e.Property(x => x.Role).HasConversion<string>(); });
        m.Entity<OtpCode>(e => { e.HasIndex(x => new { x.Email, x.Purpose }); e.Property(x => x.Purpose).HasConversion<string>(); });
        m.Entity<Payment>(e => { e.HasIndex(x => x.TxRef).IsUnique(); e.Property(x => x.Plan).HasConversion<string>(); });
        m.Entity<Trade>(e => { e.HasIndex(t => new { t.WorkspaceId, t.UserId, t.OpenedAt }); e.Property(t => t.Side).HasConversion<string>(); });
    }
}

/// <summary>Deterministic demo market. Replace with Alpha Vantage / Twelve Data / NGX feed by implementing IMarketDataService.</summary>
public class SimulatedMarketData : IMarketDataService
{
    static readonly (string S, string N, decimal P)[] Universe = [
        ("AAPL","Apple",230m),("MSFT","Microsoft",440m),("NVDA","Nvidia",140m),("TSLA","Tesla",250m),("AMZN","Amazon",190m),
        ("GOOGL","Alphabet",175m),("META","Meta",560m),("NFLX","Netflix",700m),("DANGCEM","Dangote Cement",480m),("MTNN","MTN Nigeria",250m)];

    public Task<StockHistory?> GetHistoryAsync(string symbol, int days)
    {
        var u = Universe.FirstOrDefault(x => x.S == symbol.ToUpperInvariant());
        if (u.S is null) return Task.FromResult<StockHistory?>(null);
        var rnd = new Random(u.S.Aggregate(17, (a, c) => a * 31 + c)); double p = (double)u.P; var start = DateTime.UtcNow.Date.AddDays(-days);
        var list = new List<Candle>();
        for (int i = 0; i < days; i++) { p *= 1 + 0.012 * Math.Sin(i / 7.0) + (rnd.NextDouble() - 0.5) * 0.025; list.Add(new(start.AddDays(i), Math.Round((decimal)p, 2))); }
        return Task.FromResult<StockHistory?>(new(u.S, u.N, list));
    }

    public async Task<IReadOnlyList<Quote>> GetQuotesAsync()
    {
        var res = new List<Quote>();
        foreach (var u in Universe)
        {
            var h = (await GetHistoryAsync(u.S, 90))!.Candles; var rsi = PeakDetector.Rsi(h);
            res.Add(new(u.S, u.N, h[^1].Close, Math.Round((h[^1].Close - h[^2].Close) / h[^2].Close * 100, 2), Math.Round(rsi, 1), PeakDetector.Signal(h, rsi)));
        }
        return res;
    }
}

/// <summary>Free Gemini API (Google AI Studio key) for 7-day forecasts.</summary>
public class GeminiPredictor(HttpClient http, IConfiguration cfg) : IAiPredictor
{
    public async Task<Prediction> PredictAsync(string symbol, IReadOnlyList<Candle> h, double rsi)
    {
        var closes = string.Join(",", h.TakeLast(60).Select(c => c.Close));
        var prompt = $"You are a market analyst. Last 60 daily closes for {symbol}: {closes}. RSI14={rsi:F1}. Forecast the next 7 days. Reply ONLY with JSON: {{\"direction\":\"up|down|sideways\",\"targetPrice\":number,\"confidence\":integer 0-100,\"reasoning\":\"max 40 words\"}}";
        var model = cfg["Gemini:Model"] ?? "gemini-2.5-flash";
        var res = await http.PostAsJsonAsync($"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={cfg["Gemini:ApiKey"]}",
            new { contents = new[] { new { parts = new[] { new { text = prompt } } } }, generationConfig = new { responseMimeType = "application/json" } });
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var text = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()!;
        using var j = JsonDocument.Parse(text); var r = j.RootElement;
        return new(r.GetProperty("direction").GetString()!, r.GetProperty("targetPrice").GetDecimal(), r.GetProperty("confidence").GetInt32(), r.GetProperty("reasoning").GetString()!, "AI forecasts are not financial advice.");
    }
}

public class FlutterwaveGateway(HttpClient http, IConfiguration cfg) : IPaymentGateway
{
    HttpRequestMessage Req(HttpMethod m, string path) { var r = new HttpRequestMessage(m, "https://api.flutterwave.com/v3/" + path); r.Headers.Authorization = new("Bearer", cfg["Flutterwave:SecretKey"]); return r; }

    public async Task<string> CreateCheckoutLinkAsync(string txRef, decimal amount, string email, string name, string redirectUrl)
    {
        var r = Req(HttpMethod.Post, "payments");
        r.Content = JsonContent.Create(new { tx_ref = txRef, amount, currency = cfg["Flutterwave:Currency"] ?? "USD", redirect_url = redirectUrl, customer = new { email, name }, customizations = new { title = "StockPeak", description = "Monthly subscription" } });
        var res = await http.SendAsync(r); res.EnsureSuccessStatusCode();
        using var d = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return d.RootElement.GetProperty("data").GetProperty("link").GetString()!;
    }

    public async Task<bool> VerifyAsync(string transactionId, string txRef, decimal expectedAmount)
    {
        var res = await http.SendAsync(Req(HttpMethod.Get, $"transactions/{transactionId}/verify"));
        if (!res.IsSuccessStatusCode) return false;
        using var d = JsonDocument.Parse(await res.Content.ReadAsStringAsync()); var x = d.RootElement.GetProperty("data");
        return x.GetProperty("status").GetString() == "successful" && x.GetProperty("tx_ref").GetString() == txRef
            && x.GetProperty("amount").GetDecimal() >= expectedAmount && x.GetProperty("currency").GetString() == (cfg["Flutterwave:Currency"] ?? "USD");
    }
}

public class ResendEmailSender : IEmailSender
{
    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;

    public ResendEmailSender(
        HttpClient http,
        IConfiguration cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    public async Task SendAsync(
        string to,
        string subject,
        string html)
    {
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer",
                _cfg["Resend:ApiKey"]);

        var response = await _http.PostAsJsonAsync(
            "https://api.resend.com/emails",
            new
            {
                from = "onboarding@resend.dev",
                to = new[] { to },
                subject,
                html
            });

        var content = await response.Content.ReadAsStringAsync();

        Console.WriteLine(content);

        response.EnsureSuccessStatusCode();
    }
}

//public class SmtpEmailSender : IEmailSender
//{
//    private readonly IConfiguration _cfg;
//    private readonly ILogger<SmtpEmailSender> _log;

//    public SmtpEmailSender(
//        IConfiguration cfg,
//        ILogger<SmtpEmailSender> log)
//    {
//        _cfg = cfg;
//        _log = log;
//    }

//    public async Task SendAsync(string to, string subject, string html)
//    {
//        var host = _cfg["Smtp:Host"];
//        var port = int.TryParse(_cfg["Smtp:Port"], out var p) ? p : 587;
//        var enableSsl = _cfg.GetValue("Smtp:EnableSsl", true);
//        var user = _cfg["Smtp:User"];
//        var password = _cfg["Smtp:Password"];
//        var from = _cfg["Smtp:From"] ?? user;

//        _log.LogInformation(
//            "SMTP SETTINGS => Host={Host}, Port={Port}, SSL={Ssl}, User={User}, From={From}",
//            host,
//            port,
//            enableSsl,
//            user,
//            from);

//        if (string.IsNullOrWhiteSpace(host))
//        {
//            _log.LogError("SMTP HOST IS NULL OR EMPTY");
//            return;
//        }

//        try
//        {
//            _log.LogInformation(
//                "ATTEMPTING TO SEND EMAIL => To={To}, Subject={Subject}",
//                to,
//                subject);

//            using var client = new SmtpClient(host, port)
//            {
//                EnableSsl = enableSsl,
//                DeliveryMethod = SmtpDeliveryMethod.Network,
//                UseDefaultCredentials = false,
//                Credentials = new NetworkCredential(user, password),
//                Timeout = 30000
//            };

//            using var message = new MailMessage
//            {
//                From = new MailAddress(from!, "StockPeak"),
//                Subject = subject,
//                Body = html,
//                IsBodyHtml = true
//            };

//            message.To.Add(to);

//            await client.SendMailAsync(message);

//            _log.LogInformation(
//                "EMAIL SENT SUCCESSFULLY => To={To}",
//                to);
//        }
//        catch (SmtpException ex)
//        {
//            _log.LogError(
//                ex,
//                "SMTP ERROR => To={To}, Host={Host}, User={User}",
//                to,
//                host,
//                user);

//            throw;
//        }
//        catch (Exception ex)
//        {
//            _log.LogError(
//                ex,
//                "GENERAL EMAIL ERROR => To={To}",
//                to);

//            throw;
//        }
//    }
//}

public class TokenService(IConfiguration c)
{
    public string Create(User u, Guid workspaceId, Role role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(c["Jwt:Key"]!));
        var claims = new[] { new Claim("sub", u.Id.ToString()), new Claim("email", u.Email), new Claim("role", role.ToString()), new Claim("wid", workspaceId.ToString()) };
        var t = new JwtSecurityToken(claims: claims, expires: DateTime.UtcNow.AddDays(7), signingCredentials: new(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(t);
    }
}

/// <summary>Builds the sign-in response: the user's workspaces plus a token scoped to one of them.</summary>
public class AuthService(AppDbContext db, TokenService tokens)
{
    public async Task<AuthResponse?> IssueAsync(User u, Guid? workspaceId = null)
    {
        var ms = await db.Memberships.Where(m => m.UserId == u.Id).Join(db.Workspaces, m => m.WorkspaceId, w => w.Id, (m, w) => new WorkspaceInfo(w.Id, w.Name, m.Role)).ToListAsync();
        var cur = ms.FirstOrDefault(x => workspaceId == null || x.Id == workspaceId);
        return cur is null ? null : new AuthResponse(tokens.Create(u, cur.Id, cur.Role), u.Name, u.Email, u.EffectivePlan, cur.Role, cur.Id, ms);
    }
}

/// <summary>Six-digit email codes: hashed at rest, 10 minute expiry, 5 attempts, one per minute per purpose.</summary>
public class OtpService(AppDbContext db, IEmailSender mail, IConfiguration cfg)
{
    string Hash(string email, string code) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!), Encoding.UTF8.GetBytes(email + ":" + code)));

    public async Task<bool> SendAsync(string email, OtpPurpose purpose)
    {
        email = email.Trim().ToLowerInvariant(); var since = DateTime.UtcNow.AddSeconds(-60);
        if (await db.OtpCodes.AnyAsync(o => o.Email == email && o.Purpose == purpose && o.CreatedAt > since)) return false;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        code = "123456";
        db.OtpCodes.Add(new() { Email = email, Purpose = purpose, CodeHash = Hash(email, code), ExpiresAt = DateTime.UtcNow.AddMinutes(10) });
        await db.SaveChangesAsync();
        var what = purpose == OtpPurpose.EmailVerification ? "verification" : "password reset";
        try
        {
            await mail.SendAsync(
                email,
                $"Your StockPeak {what} code",
                $"""
        <h2>StockPeak Verification Code</h2>
        <p>Your {what} code is:</p>

        <h1>{code}</h1>

        <p>This code expires in 10 minutes.</p>

        <p>If you did not request this code, please ignore this email.</p>
        """);

            Console.WriteLine($"OTP EMAIL SENT TO {email}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"OTP EMAIL FAILED: {ex}");
            throw;
        }
        return true;
    }

    public async Task<bool> ConfirmAsync(string email, string code, OtpPurpose purpose)
    {
        email = email.Trim().ToLowerInvariant(); var now = DateTime.UtcNow;
        var o = await db.OtpCodes.Where(x => x.Email == email && x.Purpose == purpose && !x.Used && x.ExpiresAt > now).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync();
        if (o is null || o.Attempts >= 5) return false;
        o.Attempts++;
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(o.CodeHash), Encoding.UTF8.GetBytes(Hash(email, code.Trim())));
        if (ok) o.Used = true;
        await db.SaveChangesAsync();
        return ok;
    }
}

/// <summary>Verifies a Flutterwave payment server-side, then activates 30 days of the purchased plan.</summary>
public class BillingService(AppDbContext db, IPaymentGateway gw, IEmailSender mail)
{
    public async Task<bool> ActivateAsync(string txRef, string txId)
    {
        Console.WriteLine(
        $"ACTIVATE START txRef={txRef} txId={txId}");
        var p = await db.Payments.FirstOrDefaultAsync(x => x.TxRef == txRef);
        Console.WriteLine(
        $"PAYMENT FOUND={p != null}");
        if (p is null) return false;
        if (p.Status == "paid") return true;
        if (!await gw.VerifyAsync(txId, txRef, p.Amount)) { p.Status = "failed"; await db.SaveChangesAsync(); return false; }
        Console.WriteLine(
        $"VERIFY RESULT={p.Status}");
        var u = await db.Users.FindAsync(p.UserId); if (u is null) return false;
        var from = u.PlanExpiresAt > DateTime.UtcNow ? u.PlanExpiresAt.Value : DateTime.UtcNow;
        p.Status = "paid"; u.Plan = p.Plan; u.PlanExpiresAt = from.AddDays(30);
        db.Notifications.Add(new() { UserId = u.Id, Title = "Subscription active", Body = $"Your {p.Plan} plan runs until {u.PlanExpiresAt:d MMM yyyy}." });
        await db.SaveChangesAsync();
        try { await mail.SendAsync(u.Email, "Your StockPeak plan is active", $"<p>Hi {WebUtility.HtmlEncode(u.Name)}, your <b>{p.Plan}</b> plan is active until {u.PlanExpiresAt:d MMM yyyy}.</p>"); } catch { }
        return true;
    }
}

/// <summary>Every 5 minutes: notifies watchers when a stock hits a PEAK signal (email for paid plans).</summary>
public class PeakAlertWorker(IServiceScopeFactory sf) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var s = sf.CreateScope(); var db = s.ServiceProvider.GetRequiredService<AppDbContext>();
                var mail = s.ServiceProvider.GetRequiredService<IEmailSender>(); var today = DateTime.UtcNow.Date;
                foreach (var q in (await s.ServiceProvider.GetRequiredService<IMarketDataService>().GetQuotesAsync()).Where(q => q.Signal == "PEAK"))
                    foreach (var uid in await db.WatchItems.Where(w => w.Symbol == q.Symbol).Select(w => w.UserId).ToListAsync(ct))
                    {
                        var title = $"{q.Symbol} is at a peak";
                        if (await db.Notifications.AnyAsync(n => n.UserId == uid && n.Title == title && n.CreatedAt >= today, ct)) continue;
                        var body = $"RSI {q.Rsi:F0}, price {q.Price}. Consider taking profit.";
                        db.Notifications.Add(new() { UserId = uid, Title = title, Body = body });
                        var u = await db.Users.FindAsync([uid], ct);
                        if (u is not null && u.EffectivePlan != Plan.Free) try { await mail.SendAsync(u.Email, title, $"<p>{body}</p>"); } catch { }
                    }
                await db.SaveChangesAsync(ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested) { }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration c)
    {
        s.AddDbContext<AppDbContext>(o => o.UseNpgsql(c.GetConnectionString("Default")));
        s.AddSingleton<IMarketDataService, SimulatedMarketData>(); s.AddHttpClient<IEmailSender, ResendEmailSender>(); s.AddSingleton<TokenService>();
        s.AddHttpClient<IAiPredictor, GeminiPredictor>(); s.AddHttpClient<IPaymentGateway, FlutterwaveGateway>();
        s.AddScoped<BillingService>(); s.AddScoped<AuthService>(); s.AddScoped<OtpService>(); s.AddHostedService<PeakAlertWorker>();
        return s;
    }
}
