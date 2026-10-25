using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using StockPeak.Application;
using StockPeak.Domain;
using StockPeak.Infrastructure;
using StockPeak.Shared;
namespace StockPeak.API.Endpoints;

public static class AuthEndpoints
{
    const string Sent = "If that email is registered, a code is on its way. Codes expire in 10 minutes. Wait a minute before asking for another.";
    static string Norm(string? e) => (e ?? "").Trim().ToLowerInvariant();

    static async Task<IResult> SendCode(string email, OtpPurpose purpose, AppDbContext db, OtpService otp)
    {
        var u = await db.Users.FirstOrDefaultAsync(x => x.Email == email);
        if (u is not null && (purpose == OtpPurpose.PasswordReset || !u.EmailVerified)) { try { await otp.SendAsync(email, purpose); } catch { } }
        return Results.Ok(new { message = Sent });
    }

    public static void Map(RouteGroupBuilder g)
    {
        // Sign up as a User or Merchant. Other roles are granted by invitation.
        g.MapPost("/register", async (RegisterRequest r, AppDbContext db, OtpService otp, IConfiguration cfg) =>
        {
            var email = Norm(r.Email);
            if (string.IsNullOrWhiteSpace(r.Name) || !email.Contains('@') || (r.Password?.Length ?? 0) < 8) return Ctx.Fail("Enter your name, a valid email and a password of at least 8 characters.");
            if (r.Role is not (Role.User or Role.Merchant)) return Ctx.Fail("You can sign up as a User or a Merchant. Ask a workspace admin to invite you for any other role.");
            if (await db.Users.AnyAsync(x => x.Email == email)) return Ctx.Fail("That email is already registered. Sign in, then create another workspace from the Team page.", 409);
            var admins = cfg.GetSection("Admin:Emails").Get<string[]>() ?? [];
            var role = admins.Contains(email, StringComparer.OrdinalIgnoreCase) ? Role.Admin : r.Role;
            var u = new User { Name = r.Name.Trim(), Email = email, PasswordHash = BCrypt.Net.BCrypt.HashPassword(r.Password) };
            var w = new Workspace { Name = string.IsNullOrWhiteSpace(r.WorkspaceName) ? $"{u.Name}'s workspace" : r.WorkspaceName.Trim(), OwnerId = u.Id };
            db.Users.Add(u); db.Workspaces.Add(w); db.Memberships.Add(new() { UserId = u.Id, WorkspaceId = w.Id, Role = role });
            db.Notifications.Add(new() { UserId = u.Id, Title = "Welcome to StockPeak", Body = "Add stocks to your watchlist and we will alert you when they peak." });
            await db.SaveChangesAsync();
            try { await otp.SendAsync(email, OtpPurpose.EmailVerification); } catch { }
            return Results.Ok(new { message = "Account created. Enter the 6-digit code we emailed you.", email, requiresVerification = true });
        });

        // Send (or resend) the email verification OTP.
        g.MapPost("/verify-email-otp", (EmailRequest r, AppDbContext db, OtpService otp) => SendCode(Norm(r.Email), OtpPurpose.EmailVerification, db, otp));

        // Confirm the OTP, mark the email verified and sign the user in.
        g.MapPost("/confirm-email-otp", async (ConfirmOtpRequest r, AppDbContext db, OtpService otp, AuthService auth) =>
        {
            var email = Norm(r.Email);
            if (!await otp.ConfirmAsync(email, r.Code ?? "", OtpPurpose.EmailVerification)) return Ctx.Fail("That code is wrong or has expired. Request a new one.");
            var u = await db.Users.FirstOrDefaultAsync(x => x.Email == email); if (u is null) return Ctx.Fail("Account not found.", 404);
            u.EmailVerified = true; await db.SaveChangesAsync();
            return Results.Ok(await auth.IssueAsync(u));
        });

        // Generic verification code for either purpose (email verification or password reset).
        g.MapPost("/verification-code", (VerificationCodeRequest r, AppDbContext db, OtpService otp) => SendCode(Norm(r.Email), r.Purpose, db, otp));

        g.MapPost("/login", async (LoginRequest r, AppDbContext db, AuthService auth, OtpService otp) =>
        {
            var email = Norm(r.Email);
            var u = await db.Users.FirstOrDefaultAsync(x => x.Email == email);
            if (u is null || !BCrypt.Net.BCrypt.Verify(r.Password ?? "", u.PasswordHash)) return Ctx.Fail("Email or password is incorrect.", 401);
            if (!u.EmailVerified)
            {
                try { await otp.SendAsync(email, OtpPurpose.EmailVerification); } catch { }
                return Results.Json(new { error = "Verify your email to continue. We sent you a code.", code = "EMAIL_NOT_VERIFIED" }, statusCode: 403);
            }
            var res = await auth.IssueAsync(u, r.WorkspaceId);
            return res is null ? Ctx.Fail("You are not a member of that workspace.", 403) : Results.Ok(res);
        });

        g.MapPost("/forgot-password", (EmailRequest r, AppDbContext db, OtpService otp) => SendCode(Norm(r.Email), OtpPurpose.PasswordReset, db, otp));

        g.MapPost("/reset-password", async (ResetPasswordRequest r, AppDbContext db, OtpService otp) =>
        {
            var email = Norm(r.Email);
            if ((r.NewPassword?.Length ?? 0) < 8) return Ctx.Fail("Choose a password of at least 8 characters.");
            if (!await otp.ConfirmAsync(email, r.Code ?? "", OtpPurpose.PasswordReset)) return Ctx.Fail("That code is wrong or has expired. Request a new one.");
            var u = await db.Users.FirstOrDefaultAsync(x => x.Email == email); if (u is null) return Ctx.Fail("Account not found.", 404);
            u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(r.NewPassword); u.EmailVerified = true; await db.SaveChangesAsync();
            return Results.Ok(new { message = "Password updated. Sign in with your new password." });
        });

        g.MapGet("/me", async (ClaimsPrincipal p, AppDbContext db, AuthService auth) =>
        {
            var uid = p.Uid(); var wid = p.Wid();
            var u = await db.Users.FindAsync(uid); var res = u is null ? null : await auth.IssueAsync(u, wid);
            return res is null ? Results.Unauthorized() : Results.Ok(res);
        }).RequireAuthorization();
    }
}
