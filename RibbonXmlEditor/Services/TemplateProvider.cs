namespace RibbonXmlEditor.Services;

/// <summary>Provides the embedded ButtonStructureTemplate.ribbon used by "New from template".</summary>
public static class TemplateProvider
{
    private const string ResourceName = "RibbonXmlEditor.Resources.ButtonStructureTemplate.ribbon";

    public static RibbonReadResult LoadButtonStructureTemplate()
    {
        using var stream = typeof(TemplateProvider).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource {ResourceName} is missing.");
        using var reader = new StreamReader(stream);
        return RibbonXmlReader.Parse(reader.ReadToEnd());
    }
}
