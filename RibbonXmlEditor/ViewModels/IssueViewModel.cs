using RibbonXmlEditor.Models;

namespace RibbonXmlEditor.ViewModels;

public sealed class IssueViewModel
{
    public IssueViewModel(Issue issue, NodeViewModel? node)
    {
        Issue = issue;
        Node = node;
    }

    public Issue Issue { get; }
    public NodeViewModel? Node { get; }

    public bool IsError => Issue.IsError;
    public string SeverityText => IsError ? "Error" : "Warning";
    public string SeverityGlyph => IsError ? "⛔" : "⚠";
    public string Message => Issue.Message;
    public string? AttributeName => Issue.AttributeName;

    public string Location
    {
        get
        {
            if (Node is null)
                return "(file)";
            var path = Node.Node.PathText();
            return AttributeName is null ? path : $"{path} @ {AttributeName}";
        }
    }
}
