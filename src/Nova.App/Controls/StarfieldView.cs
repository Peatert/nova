using System.Diagnostics;

namespace Nova.App.Controls;

/// <summary>
/// Живой фон «Nova»: мерцающие звёзды, пульсирующая красная сверхновая и редкие метеоры.
/// Рисуется кодом через GraphicsView: никаких картинок и видео, работает одинаково на Android и Windows.
/// Анимация останавливается, когда страница скрыта, чтобы не тратить батарею.
/// </summary>
public sealed class StarfieldView : GraphicsView
{
    private readonly StarfieldDrawable _drawable = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private IDispatcherTimer? _timer;

    public StarfieldView()
    {
        Drawable = _drawable;
        InputTransparent = true;
        BackgroundColor = Colors.Transparent;
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
    }

    private void Start()
    {
        if (_timer is not null) return;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(33); // ~30 кадров/с — плавно и экономно
        _timer.Tick += (_, _) =>
        {
            _drawable.Time = (float)_clock.Elapsed.TotalSeconds;
            Invalidate();
        };
        _timer.Start();
    }

    private void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }
}

internal sealed class StarfieldDrawable : IDrawable
{
    private readonly record struct Star(float X, float Y, float Radius, float Speed, float Phase);

    private static readonly Color Space = Color.FromArgb("#06060C");
    private static readonly Star[] Stars = CreateStars(70);

    public float Time { get; set; }

    public void Draw(ICanvas canvas, RectF rect)
    {
        canvas.FillColor = Space;
        canvas.FillRectangle(rect);

        DrawNova(canvas, rect);

        foreach (var s in Stars)
        {
            // Каждая звезда мерцает со своей скоростью и фазой.
            var alpha = 0.15f + 0.85f * (0.5f + 0.5f * MathF.Sin(Time * s.Speed + s.Phase));
            canvas.FillColor = Colors.White.WithAlpha(alpha);
            canvas.FillCircle(rect.Left + s.X * rect.Width, rect.Top + s.Y * rect.Height, s.Radius);
        }

        DrawMeteor(canvas, rect, period: 9f, offset: 0f, startX: 1.05f, startY: 0.08f);
        DrawMeteor(canvas, rect, period: 13f, offset: 5f, startX: 1.1f, startY: 0.4f);
    }

    /// <summary>Сверхновая в правом верхнем углу: радиальное красное свечение, медленно «дышит».</summary>
    private void DrawNova(ICanvas canvas, RectF rect)
    {
        var pulse = 0.92f + 0.08f * MathF.Sin(Time * MathF.PI * 2f / 6f);
        var radius = rect.Width * 0.6f * pulse;
        var center = new PointF(rect.Right - rect.Width * 0.12f, rect.Top + rect.Height * 0.05f);
        var bounds = new RectF(center.X - radius, center.Y - radius, radius * 2, radius * 2);

        var paint = new RadialGradientPaint
        {
            Center = new Point(0.5, 0.5),
            Radius = 0.5,
            GradientStops =
            [
                new PaintGradientStop(0f, Color.FromRgba(255, 120, 130, 150)),
                new PaintGradientStop(0.25f, Color.FromRgba(229, 48, 63, 90)),
                new PaintGradientStop(1f, Color.FromRgba(229, 48, 63, 0)),
            ],
        };
        canvas.SetFillPaint(paint, bounds);
        canvas.FillEllipse(bounds);
    }

    /// <summary>Метеор пролетает по диагонали за ~1,2 с и пропадает до следующего периода.</summary>
    private void DrawMeteor(ICanvas canvas, RectF rect, float period, float offset, float startX, float startY)
    {
        var p = ((Time + offset) % period) / period;
        const float flight = 0.14f;
        if (p > flight) return;

        var k = p / flight;
        var x = rect.Left + rect.Width * (startX - k * 0.9f);
        var y = rect.Top + rect.Height * (startY + k * 0.25f);
        var fade = 1f - k;

        canvas.StrokeColor = Colors.White.WithAlpha(0.8f * fade);
        canvas.StrokeSize = 2f;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawLine(x, y, x + 90f, y - 25f);
    }

    private static Star[] CreateStars(int count)
    {
        var rng = new Random(7); // одно и то же небо при каждом запуске
        var stars = new Star[count];
        for (var i = 0; i < count; i++)
        {
            var big = rng.NextDouble() < 0.15;
            stars[i] = new Star(
                X: (float)rng.NextDouble(),
                Y: (float)rng.NextDouble(),
                Radius: big ? 1.6f : 0.6f + (float)rng.NextDouble() * 0.7f,
                Speed: 0.8f + (float)rng.NextDouble() * 2.2f,
                Phase: (float)(rng.NextDouble() * Math.PI * 2));
        }
        return stars;
    }
}
