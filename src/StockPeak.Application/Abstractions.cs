using StockPeak.Shared;
namespace StockPeak.Application;
public interface IMarketDataService { Task<IReadOnlyList<Quote>> GetQuotesAsync(); Task<StockHistory?> GetHistoryAsync(string symbol, int days); }
public interface IAiPredictor { Task<Prediction> PredictAsync(string symbol, IReadOnlyList<Candle> history, double rsi); }
public interface IEmailSender { Task SendAsync(string to, string subject, string html); }
public interface IPaymentGateway
{
    Task<string> CreateCheckoutLinkAsync(string txRef, decimal amount, string email, string name, string redirectUrl);
    Task<bool> VerifyAsync(string transactionId, string txRef, decimal expectedAmount);
}
