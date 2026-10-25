using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using StockPeak.API.Endpoints;
using StockPeak.Infrastructure;
using StockPeak.Shared;

var b = WebApplication.CreateBuilder(args);
var jwtKey = b.Configuration["Jwt:Key"];
if (string.IsNullOrEmpty(jwtKey) || jwtKey.Length < 32) throw new InvalidOperationException("Jwt:Key must be set to a random value of at least 32 characters.");

b.Services.AddInfrastructure(b.Configuration);
b.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
b.Services.AddHealthChecks();

b.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    { IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ValidateIssuer = false, ValidateAudience = false, NameClaimType = "sub", RoleClaimType = "role" };
});
b.Services.AddAuthorizationBuilder()
    .AddPolicy("Admin", p => p.RequireRole("Admin"))
    .AddPolicy("Staff", p => p.RequireRole("Admin", "Manager"))
    .AddPolicy("Team", p => p.RequireRole("Admin", "Manager", "Agent"))
    .AddPolicy("Merchant", p => p.RequireRole("Admin", "Manager", "Merchant"));

b.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

b.Services.AddOpenApi(o => o.AddDocumentTransformer((doc, _, _) =>
{
    doc.Info = new OpenApiInfo { Title = "StockPeak API", Version = "v1", Description = "Stock peak detection, AI forecasts, workspaces, roles, invites, trade journal and subscriptions." };
    doc.Components ??= new OpenApiComponents();
    doc.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme> { ["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" } };
    doc.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] }];
    return Task.CompletedTask;
}));

var app = b.Build();

using (var s = app.Services.CreateScope()) s.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.MapOpenApi();
    app.UseSwaggerUI(o => { o.SwaggerEndpoint("/openapi/v1.json", "StockPeak API v1"); o.RoutePrefix = "swagger"; });
}

app.UseDefaultFiles(); app.UseStaticFiles();
app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization();
app.MapHealthChecks("/health");

AuthEndpoints.Map(app.MapGroup("/api/auth").WithTags("Auth").RequireRateLimiting("auth"));
WorkspaceEndpoints.Map(app.MapGroup("/api/workspaces").WithTags("Workspaces and roles").RequireAuthorization());
InviteEndpoints.Map(app.MapGroup("/api/invites").WithTags("Invites").RequireAuthorization());
TradeEndpoints.Map(app.MapGroup("/api/trades").WithTags("Trade journal").RequireAuthorization());
MarketEndpoints.Map(app.MapGroup("/api").WithTags("Market, watchlist and alerts"));
BillingEndpoints.Map(app.MapGroup("/api/billing").WithTags("Billing"));

var roleInfo = new[]
{
    new { role = "User", selfRegister = true, description = "Tracks stocks, keeps a trade journal and gets peak alerts." },
    new { role = "Merchant", selfRegister = true, description = "Runs a business workspace and subscribes for the team." },
    new { role = "Agent", selfRegister = false, description = "Supports clients and can view all trades in the workspace." },
    new { role = "Manager", selfRegister = false, description = "Invites people and manages User, Merchant and Agent roles." },
    new { role = "Admin", selfRegister = false, description = "Full control of the workspace, including managers." }
};
app.MapGet("/api/roles", () => roleInfo).WithTags("Workspaces and roles");

app.Run();
