namespace PkhexMobile;

// Button.jsx / states-and-edge-cases.html: press feedback is `transform: scale(0.97)`
// over `--dur-fast` (120ms), no bounce/spring easing anywhere in the system.
public class PressScaleBehavior : Behavior<Button>
{
    const uint DurationMs = 120;
    const double PressedScale = 0.97;

    protected override void OnAttachedTo(Button bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.Pressed += OnPressed;
        bindable.Released += OnReleased;
    }

    protected override void OnDetachingFrom(Button bindable)
    {
        bindable.Pressed -= OnPressed;
        bindable.Released -= OnReleased;
        base.OnDetachingFrom(bindable);
    }

    static async void OnPressed(object? sender, EventArgs e)
    {
        if (sender is VisualElement v)
            await v.ScaleToAsync(PressedScale, DurationMs, Easing.CubicOut);
    }

    static async void OnReleased(object? sender, EventArgs e)
    {
        if (sender is VisualElement v)
            await v.ScaleToAsync(1.0, DurationMs, Easing.CubicOut);
    }
}
