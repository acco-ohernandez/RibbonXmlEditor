using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using RibbonXmlEditor.ViewModels.Fields;

namespace RibbonXmlEditor.Views;

/// <summary>
/// Attached property that focuses a control when its <see cref="FieldViewModel"/> raises FocusRequested
/// (used when the user double-clicks an issue in the issues list).
/// </summary>
public static class FocusBehavior
{
    public static readonly DependencyProperty FieldProperty = DependencyProperty.RegisterAttached(
        "Field", typeof(FieldViewModel), typeof(FocusBehavior), new PropertyMetadata(null, OnFieldChanged));

    public static void SetField(DependencyObject element, FieldViewModel? value) => element.SetValue(FieldProperty, value);
    public static FieldViewModel? GetField(DependencyObject element) => (FieldViewModel?)element.GetValue(FieldProperty);

    private static void OnFieldChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        if (e.OldValue is FieldViewModel old)
            old.FocusRequested -= Handler(element);
        if (e.NewValue is FieldViewModel field)
        {
            var handler = Handler(element);
            field.FocusRequested += handler;
            element.Unloaded += (_, _) => field.FocusRequested -= handler;
        }
    }

    private static readonly Dictionary<FrameworkElement, EventHandler> Handlers = new();

    private static EventHandler Handler(FrameworkElement element)
    {
        if (!Handlers.TryGetValue(element, out var h))
        {
            h = (_, _) => element.Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                element.BringIntoView();
                if (element is TextBoxBase tb)
                {
                    tb.Focus();
                    if (tb is TextBox t) t.CaretIndex = t.Text.Length;
                }
                else if (element is ComboBox cb)
                {
                    cb.Focus();
                }
                else
                {
                    element.Focus();
                }
            });
            Handlers[element] = h;
        }
        return h;
    }
}
