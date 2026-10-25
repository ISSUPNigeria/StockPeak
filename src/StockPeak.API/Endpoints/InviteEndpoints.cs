using System.Security.Claims;
using System.Net;
using Microsoft.EntityFrameworkCore;
using StockPeak.Application;
using StockPeak.Domain;
using StockPeak.Infrastructure;
using StockPeak.Shared;
namespace StockPeak.API.Endpoints;

public static class InviteEndpoints
{
    public static void Map(RouteGroupBuilder g)
    {
        g.MapPost("/", async (InviteRequest r, ClaimsPrincipal p, HttpContext ctx, AppDbContext db, IEmailSender mail) =>
        {
            var email = (r.Email ?? "").Trim().ToLowerInvariant(); var wid = p.Wid(); var uid = p.Uid();
            if (!email.Contains('@')) return Ctx.Fail("Enter a valid email address.");
            var me = await db.Memberships.FirstOrDefaultAsync(m => m.WorkspaceId == wid && m.UserId == uid);
            if (me is null || !RoleRules.CanAssign(me.Role, r.Role)) return Ctx.Fail("Your role cannot invite someone as " + r.Role + ".", 403);
            var member = await (from m in db.Memberships join u in db.Users on m.UserId equals u.Id where m.WorkspaceId == wid && u.Email == email select m.Id).AnyAsync();
            if (member) return Ctx.Fail("That person is already in this workspace.", 409);
            db.Invites.RemoveRange(await db.Invites.Where(i => i.WorkspaceId == wid && i.Email == email && i.AcceptedAt == null).ToListAsync());
            var token = Crypto.NewToken();
            db.Invites.Add(new() { WorkspaceId = wid, Email = email, Role = r.Role, TokenHash = Crypto.Sha256(token), InvitedById = uid, ExpiresAt = DateTime.UtcNow.AddDays(7) });
            await db.SaveChangesAsync();
            var ws = await db.Workspaces.FindAsync(wid); var link = $"{ctx.BaseUrl()}/?invite={token}";
            try { await mail.SendAsync(email, $"You are invited to {ws?.Name} on StockPeak", $"<p>You have been invited to join <b>{WebUtility.HtmlEncode(ws?.Name)}</b> as <b>{r.Role}</b>.</p><p><a href=\"{link}\">Accept the invitation</a>. It expires in 7 days.</p>"); } catch { }
            return Results.Ok(new { message = "Invitation sent.", inviteLink = link });
        }).RequireAuthorization("Staff");

        g.MapGet("/", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            var wid = p.Wid(); var now = DateTime.UtcNow;
            return await db.Invites.Where(i => i.WorkspaceId == wid && i.AcceptedAt == null && i.ExpiresAt > now).OrderByDescending(i => i.CreatedAt).Select(i => new { i.Id, i.Email, i.Role, i.ExpiresAt }).ToListAsync();
        }).RequireAuthorization("Staff");

        g.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal p, AppDbContext db) =>
        {
            var wid = p.Wid(); await db.Invites.Where(i => i.Id == id && i.WorkspaceId == wid).ExecuteDeleteAsync(); return Results.Ok();
        }).RequireAuthorization("Staff");

        // Public: show who invited you before you sign up.
        g.MapGet("/preview", async (string token, AppDbContext db) =>
        {
            var h = Crypto.Sha256(token ?? ""); var now = DateTime.UtcNow;
            var i = await db.Invites.FirstOrDefaultAsync(x => x.TokenHash == h && x.AcceptedAt == null && x.ExpiresAt > now);
            if (i is null) return Ctx.Fail("This invitation is invalid or has expired.", 404);
            var ws = await db.Workspaces.FindAsync(i.WorkspaceId);
            return Results.Ok(new { workspace = ws?.Name, email = i.Email, role = i.Role, hasAccount = await db.Users.AnyAsync(u => u.Email == i.Email) });
        }).AllowAnonymous().RequireRateLimiting("auth");

        // Public: accept with a name and password (new person) or your password (existing account).
        g.MapPost("/accept", async (AcceptInviteRequest r, AppDbContext db, AuthService auth) =>
        {
            var h = Crypto.Sha256(r.Token ?? ""); var now = DateTime.UtcNow;
            var inv = await db.Invites.FirstOrDefaultAsync(x => x.TokenHash == h && x.AcceptedAt == null && x.ExpiresAt > now);
            if (inv is null) return Ctx.Fail("This invitation is invalid or has expired.", 404);
            var u = await db.Users.FirstOrDefaultAsync(x => x.Email == inv.Email);
            if (u is null)
            {
                if (string.IsNullOrWhiteSpace(r.Name) || (r.Password?.Length ?? 0) < 8) return Ctx.Fail("Enter your name and a password of at least 8 characters.");
                u = new User { Name = r.Name.Trim(), Email = inv.Email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(r.Password), EmailVerified = true };
                db.Users.Add(u);
            }
            else if (!BCrypt.Net.BCrypt.Verify(r.Password ?? "", u.PasswordHash)) return Ctx.Fail("Password is incorrect.", 401);
            else u.EmailVerified = true;
            if (!await db.Memberships.AnyAsync(m => m.UserId == u.Id && m.WorkspaceId == inv.WorkspaceId)) db.Memberships.Add(new() { UserId = u.Id, WorkspaceId = inv.WorkspaceId, Role = inv.Role });
            inv.AcceptedAt = DateTime.UtcNow; await db.SaveChangesAsync();
            return Results.Ok(await auth.IssueAsync(u, inv.WorkspaceId));
        }).AllowAnonymous().RequireRateLimiting("auth");
    }
}
