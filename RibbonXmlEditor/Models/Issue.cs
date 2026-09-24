namespace RibbonXmlEditor.Models;

public enum IssueSeverity
{
    Warning,
    Error,
}

/// <summary>A validation or load problem tied to a node and, optionally, one of its attributes.</summary>
public sealed record Issue(
    IssueSeverity Severity,
    string RuleId,
    string Message,
    RibbonNode? Node = null,
    string? AttributeName = null)
{
    public bool IsError => Severity == IssueSeverity.Error;
}
