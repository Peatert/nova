namespace Nova.Seeder;

/// <summary>
/// Реалистичное распределение переводов во времени.
/// Каждому часу истории назначается вес, по весам строится накопленная сумма,
/// и доля u ∈ [0;1) переводится во время обратной функцией распределения.
/// Результат монотонен по u — генератор остаётся хронологическим, а значит
/// баланс «после операции» считается последовательно, как и раньше.
/// </summary>
public sealed class ActivityTimeline
{
    private static readonly TimeSpan Msk = TimeSpan.FromHours(3);

    // Доля активности по часам суток (МСК): ночью почти тишина, пики в обед и вечером.
    private static readonly double[] HourProfile =
    [
        0.15, 0.08, 0.05, 0.04, 0.04, 0.07, 0.20, 0.45, 0.75, 0.95, 1.00, 1.05,
        1.20, 1.15, 1.00, 0.95, 1.00, 1.10, 1.30, 1.40, 1.30, 1.05, 0.70, 0.35,
    ];

    private readonly DateTime _start;
    private readonly double[] _cumulative; // накопленный вес на конец каждого часа

    public ActivityTimeline(DateTime startUtc, DateTime endUtc)
    {
        _start = startUtc;
        var hours = Math.Max(1, (int)Math.Ceiling((endUtc - startUtc).TotalHours));
        _cumulative = new double[hours];

        double sum = 0;
        for (var h = 0; h < hours; h++)
        {
            var utc = startUtc.AddHours(h);
            sum += Weight(utc, (double)h / hours);
            _cumulative[h] = sum;
        }
        for (var h = 0; h < hours; h++) _cumulative[h] /= sum;
    }

    public static double Weight(DateTime utc, double progress)
    {
        var msk = utc + Msk;

        var weekday = msk.DayOfWeek switch
        {
            DayOfWeek.Friday => 1.12,
            DayOfWeek.Saturday => 0.78,
            DayOfWeek.Sunday => 0.62,
            _ => 1.0,
        };

        // Аванс 20-го и зарплата 5-го: всплеск в день выплаты и хвост на следующий день.
        var payday = msk.Day switch
        {
            5 or 20 => 1.55,
            6 or 21 => 1.20,
            _ => 1.0,
        };

        // Конец года — подарки и премии.
        var season = msk is { Month: 12, Day: >= 20 } ? 1.35 : 1.0;

        // Банк растёт: в начале истории активность ~65% от текущей.
        var growth = 0.65 + 0.35 * progress;

        return HourProfile[msk.Hour] * weekday * payday * season * growth;
    }

    /// <summary>Время, к которому накоплена доля u всей активности.</summary>
    public DateTime At(double u)
    {
        var i = Array.BinarySearch(_cumulative, u);
        if (i < 0) i = ~i;
        if (i >= _cumulative.Length) i = _cumulative.Length - 1;

        var before = i == 0 ? 0 : _cumulative[i - 1];
        var width = _cumulative[i] - before;
        var inside = width > 0 ? (u - before) / width : 0;
        return _start.AddHours(i + inside);
    }
}
