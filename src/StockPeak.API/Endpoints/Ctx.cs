using System.Security.Claims;
namespace StockPeak.API.Endpoints;

public static class Ctx
{
    public static Guid Uid(this ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("sub")!);
    public static Guid Wid(this ClaimsPrincipal p) => Guid.Parse(p.FindFirstValue("wid")!);
    public static string BaseUrl(this HttpContext c) => c.RequestServices.GetRequiredService<IConfiguration>()["App:BaseUrl"]?.TrimEnd('/') ?? $"{c.Request.Scheme}://{c.Request.Host}";
    public static IResult Fail(string error, int status = 400) => Results.Json(new { error }, statusCode: status);
}
