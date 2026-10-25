using System.Security.Cryptography;
using System.Text;
using StockPeak.Shared;
namespace StockPeak.Application;

public static class Crypto
{
    public static string Sha256(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
    public static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
}
public static class RoleRules
{
    /// <summary>Admin can assign any role; Manager can assign User, Merchant and Agent.</summary>
    public static bool CanAssign(Role actor, Role target) => actor == Role.Admin || (actor == Role.Manager && target is Role.User or Role.Merchant or Role.Agent);
}
