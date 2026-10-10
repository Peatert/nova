using System.ComponentModel;
using Nova.App.ViewModels;

namespace Nova.App.Views;

public partial class TransferPage : ContentPage
{
    private readonly TransferViewModel _vm;
    private CancellationTokenSource? _rings;

    public TransferPage(TransferViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        _vm.PropertyChanged += OnVmPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _rings?.Cancel();
    }

    private async void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TransferViewModel.IsDone)) return;
        if (_vm.IsDone) await PlaySuccessAsync();
        else _rings?.Cancel();
    }

    /// <summary>Галочка «выстреливает» с пружинкой, от неё расходятся красные кольца.</summary>
    private async Task PlaySuccessAsync()
    {
        _rings?.Cancel();
        _rings = new CancellationTokenSource();
        var token = _rings.Token;

        Check.Scale = 0.3;
        Check.Opacity = 0;
        await Task.WhenAll(
            Check.FadeTo(1, 200),
            Check.ScaleTo(1.12, 300, Easing.CubicOut));
        await Check.ScaleTo(1.0, 150, Easing.CubicIn);

        _ = PulseAsync(Ring1, 0, token);
        _ = PulseAsync(Ring2, 600, token);
    }

    private static async Task PulseAsync(VisualElement ring, int delayMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(delayMs, token);
            while (!token.IsCancellationRequested)
            {
                ring.Scale = 0.6;
                ring.Opacity = 0.9;
                await Task.WhenAll(
                    ring.ScaleTo(1.3, 1200, Easing.CubicOut),
                    ring.FadeTo(0, 1200, Easing.CubicOut));
            }
        }
        catch (TaskCanceledException)
        {
            // страница ушла с экрана — анимация больше не нужна
        }
    }
}
