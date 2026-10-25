using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TextBlock = System.Windows.Controls.TextBlock;
using Size = System.Windows.Size;

namespace VoltPulse.App.Behaviors;

/// <summary>
/// Attached behavior: clicking an element copies attached <see cref="TextProperty"/> to the
/// clipboard and shows a short "Copied" adorner. Clipboard open failures are retried briefly.
/// </summary>
public static class ClickToCopy
{
    public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
        "Enable", typeof(bool), typeof(ClickToCopy), new PropertyMetadata(false, OnEnableChanged));

    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(ClickToCopy), new PropertyMetadata(string.Empty));

    public static bool GetEnable(DependencyObject obj) => (bool)obj.GetValue(EnableProperty);
    public static void SetEnable(DependencyObject obj, bool value) => obj.SetValue(EnableProperty, value);

    public static string GetText(DependencyObject obj) => (string)obj.GetValue(TextProperty);
    public static void SetText(DependencyObject obj, string value) => obj.SetValue(TextProperty, value);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.MouseDown -= OnMouseDown;
        if ((bool)e.NewValue)
        {
            element.MouseDown += OnMouseDown;
        }
    }

    private static void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not UIElement element || !GetEnable(element)) return;
        string text = GetText(element);
        if (string.IsNullOrEmpty(text)) return;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                break;
            }
            catch (COMException)
            {
                Thread.Sleep(20);
            }
        }

        ShowCopiedHint(element);
    }

    private static void ShowCopiedHint(UIElement element)
    {
        try
        {
            var layer = AdornerLayer.GetAdornerLayer(element);
            if (layer is null) return;

            var adorned = element as FrameworkElement;
            var adorner = new CopiedAdorner(adorned ?? element);
            layer.Add(adorner);
            var fade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(900)))
            {
                BeginTime = TimeSpan.FromMilliseconds(350),
                FillBehavior = FillBehavior.Stop,
            };
            fade.Completed += (_, _) => layer.Remove(adorner);
            adorner.BeginAnimation(System.Windows.UIElement.OpacityProperty, fade);
        }
        catch
        {
            // decorations must never break the click
        }
    }

    private sealed class CopiedAdorner : Adorner
    {
        private readonly TextBlock _text;

        public CopiedAdorner(UIElement adorned)
            : base(adorned)
        {
            _text = new TextBlock
            {
                Text = "Copied",
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = TryFindBrush("BrushAccent"),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AddVisualChild(_text);
        }

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _text;

        protected override Size MeasureOverride(Size constraint)
        {
            _text.Measure(constraint);
            return base.MeasureOverride(constraint);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double width = _text.DesiredSize.Width + 10;
            _text.Arrange(new Rect(Math.Max(0, finalSize.Width - width), 0, width, finalSize.Height));
            return finalSize;
        }

        private static Brush TryFindBrush(string key) =>
            System.Windows.Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}
