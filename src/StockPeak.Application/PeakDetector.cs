using StockPeak.Shared;
namespace StockPeak.Application;
/// <summary>Core peak-finding logic: local maxima/minima, RSI and a PEAK / BOTTOM / HOLD signal.</summary>
public static class PeakDetector
{
    public static double Rsi(IReadOnlyList<Candle> c, int period = 14)
    {
        if (c.Count <= period) return 50;
        double gain = 0, loss = 0;
        for (int i = c.Count - period; i < c.Count; i++)
        {
            var d = (double)(c[i].Close - c[i - 1].Close);
            if (d >= 0) gain += d; else loss -= d;
        }
        return loss == 0 ? 100 : 100 - 100 / (1 + gain / loss);
    }

    public static (List<PeakPoint> Peaks, List<PeakPoint> Troughs) Find(IReadOnlyList<Candle> c, int w = 5)
    {
        var peaks = new List<PeakPoint>(); var troughs = new List<PeakPoint>();
        for (int i = w; i < c.Count - w; i++)
        {
            var win = c.Skip(i - w).Take(2 * w + 1).Select(x => x.Close).ToList();
            var p = c[i].Close;
            if (p == win.Max()) peaks.Add(new(i, c[i].Date, p, (double)((p - win.Min()) / p * 100)));
            else if (p == win.Min()) troughs.Add(new(i, c[i].Date, p, (double)((win.Max() - p) / p * 100)));
        }
        return (peaks, troughs);
    }

    public static string Signal(IReadOnlyList<Candle> c, double rsi)
    {
        var recent = c.TakeLast(60).Select(x => x.Close).ToList(); var last = c[^1].Close;
        if (rsi >= 70 && last >= recent.Max() * 0.98m) return "PEAK";
        if (rsi <= 30 && last <= recent.Min() * 1.02m) return "BOTTOM";
        return "HOLD";
    }
}
