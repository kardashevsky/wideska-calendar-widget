using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Wideska.Infrastructure;

public static class AppearanceAnimation
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(AppearanceAnimation), new PropertyMetadata(false, OnEnabledChanged));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.IsVisibleChanged -= OnVisibilityChanged;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if ((bool)e.NewValue) element.IsVisibleChanged += OnVisibilityChanged;
    }

    private static void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        if (!(bool)e.NewValue || !SystemParameters.ClientAreaAnimation || RenderCapability.Tier == 0) return;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.25, 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }
}
