using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RibbonXmlEditor.ViewModels;

namespace RibbonXmlEditor.Views;

public partial class RibbonPreview : UserControl
{
    public RibbonPreview()
    {
        InitializeComponent();
    }

    /// <summary>The ribbon is a horizontal strip; let the mouse wheel scroll it sideways.</summary>
    private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        Scroller.ScrollToHorizontalOffset(Scroller.HorizontalOffset - e.Delta);
        e.Handled = true;
    }

    /// <summary>Popups are separate windows; close them when the preview tab is hidden.</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && DataContext is EditorViewModel { Document: { } doc })
            doc.ClosePreviewPopups();
    }
}
