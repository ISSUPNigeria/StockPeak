using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using StockPeak.Application;
using StockPeak.Domain;
using StockPeak.Infrastructure;
using StockPeak.Shared;
namespace StockPeak.API.Endpoints;

public static class WorkspaceEndpoints
{
    public static void Map(RouteGroupBuilder g)
    {
        g.MapGet("/", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            var uid = p.Uid();
            return await db.Memberships.Where(m => m.UserId == uid).Join(db.Workspaces, m => m.WorkspaceId, w => w.Id, (m, w) => new WorkspaceInfo(w.Id, w.Name, m.Role)).ToListAsync();
        });

        // The same email can create or join as many workspaces as it needs.
        g.MapPost("/", async (CreateWorkspaceRequest r, ClaimsPrincipal p, AppDbContext db, AuthService auth) =>
        {
            if (string.IsNullOrWhiteSpace(r.Name)) return Ctx.Fail("Enter a workspace name.");
            var u = await db.Users.FindAsync(p.Uid()); if (u is null) return Results.Unauthorized();
            var w = new Workspace { Name = r.Name.Trim(), OwnerId = u.Id };
            db.Workspaces.Add(w); db.Memberships.Add(new() { UserId = u.Id, WorkspaceId = w.Id, Role = Role.Admin });
            await db.SaveChangesAsync();
            return Results.Ok(await auth.IssueAsync(u, w.Id));
        });

        g.MapPost("/{id:guid}/switch", async (Guid id, ClaimsPrincipal p, AppDbContext db, AuthService auth) =>
        {
            var u = await db.Users.FindAsync(p.Uid()); var res = u is null ? null : await auth.IssueAsync(u, id);
            return res is null ? Ctx.Fail("You are not a member of that workspace.", 403) : Results.Ok(res);
        });

        g.MapGet("/current/members", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            var wid = p.Wid();
            return await db.Memberships.Where(m => m.WorkspaceId == wid).Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { u.Id, u.Name, u.Email, m.Role }).ToListAsync();
        }).RequireAuthorization("Team");

        g.MapPut("/current/members/{userId:guid}/role", async (Guid userId, ChangeRoleRequest r, ClaimsPrincipal p, AppDbContext db) =>
        {
            var wid = p.Wid(); var uid = p.Uid();
            var me = await db.Memberships.FirstOrDefaultAsync(m => m.WorkspaceId == wid && m.UserId == uid);
            var t = await db.Memberships.FirstOrDefaultAsync(m => m.WorkspaceId == wid && m.UserId == userId);
            if (me is null || t is null) return Ctx.Fail("Member not found.", 404);
            if (userId == uid) return Ctx.Fail("You cannot change your own role.");
            if (!RoleRules.CanAssign(me.Role, r.Role) || !RoleRules.CanAssign(me.Role, t.Role)) return Ctx.Fail("Your role cannot make that change.", 403);
            t.Role = r.Role; await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization("Staff");

        g.MapDelete("/current/members/{userId:guid}", async (Guid userId, ClaimsPrincipal p, AppDbContext db) =>
        {
            var wid = p.Wid(); var uid = p.Uid();
            var me = await db.Memberships.FirstOrDefaultAsync(m => m.WorkspaceId == wid && m.UserId == uid);
            var t = await db.Memberships.FirstOrDefaultAsync(m => m.WorkspaceId == wid && m.UserId == userId);
            if (me is null || t is null) return Ctx.Fail("Member not found.", 404);
            if (userId == uid) return Ctx.Fail("You cannot remove yourself.");
            if (!RoleRules.CanAssign(me.Role, t.Role)) return Ctx.Fail("Your role cannot remove this member.", 403);
            db.Memberships.Remove(t); await db.SaveChangesAsync();
            return Results.Ok();
        }).RequireAuthorization("Staff");

        g.MapGet("/current/overview", async (ClaimsPrincipal p, AppDbContext db) =>
        {
            var wid = p.Wid();
            var closed = await db.Trades.Where(t => t.WorkspaceId == wid && t.ClosedAt != null).ToListAsync();
            return new
            {
                members = await db.Memberships.CountAsync(m => m.WorkspaceId == wid),
                openTrades = await db.Trades.CountAsync(t => t.WorkspaceId == wid && t.ClosedAt == null),
                closedTrades = closed.Count,
                totalPnl = closed.Sum(t => t.Pnl ?? 0m)
            };
        }).RequireAuthorization("Staff");
    }
}
