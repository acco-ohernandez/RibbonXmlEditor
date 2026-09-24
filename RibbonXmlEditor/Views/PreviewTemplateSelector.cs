using System.Windows;
using System.Windows.Controls;
using RibbonXmlEditor.Schema;
using RibbonXmlEditor.ViewModels;

namespace RibbonXmlEditor.Views;

/// <summary>
/// Picks the ribbon-preview template for a node by its (immutable) element kind:
/// resource key <c>Preview.&lt;Kind&gt;</c>. Everything that can change at runtime
/// (large/small, first child, open state) is handled by bindings inside the templates.
/// </summary>
public sealed class PreviewTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (item is not NodeViewModel vm || container is not FrameworkElement fe)
            return null;

        var key = vm.Node.Kind switch
        {
            ElementKind.ToggleButton => "Preview.Button",
            ElementKind.ComboBoxMember => "Preview.ListItem",
            var k => "Preview." + k,
        };
        return fe.TryFindResource(key) as DataTemplate;
    }
}
