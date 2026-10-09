using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace RibbonXmlEditor.Services;

public sealed record ScanResult(IReadOnlyList<string> ClassNames, string? Error);

/// <summary>
/// Lists the public, concrete classes in a Revit add-in DLL that implement an <c>Autodesk.Revit.UI</c>
/// interface, by reading metadata only. The DLL is never loaded, so RevitAPIUI.dll does not need to be
/// present and net48 / net8 / net10 builds all work.
/// <see cref="Scan"/> finds <c>IExternalCommand</c> classes (ribbon buttons);
/// <see cref="ScanPaneProviders"/> finds <c>IDockablePaneProvider</c> classes (dockable panes).
/// </summary>
public static class CommandClassScanner
{
    private const string CommandInterface = "IExternalCommand";
    private const string PaneProviderInterface = "IDockablePaneProvider";
    private const string InterfaceNamespace = "Autodesk.Revit.UI";

    /// <summary>File name of the shared class library that sits next to every ACCO tab DLL and holds the pane classes.</summary>
    public const string ResourcesDllName = "RevitRibbon_MainSourceCode_Resources.dll";

    /// <summary>Public concrete <c>IExternalCommand</c> classes, sorted.</summary>
    public static ScanResult Scan(string dllPath) => ScanFor(dllPath, CommandInterface);

    /// <summary>Public concrete <c>IDockablePaneProvider</c> classes, sorted.</summary>
    public static ScanResult ScanPaneProviders(string dllPath) => ScanFor(dllPath, PaneProviderInterface);

    /// <summary>
    /// The DLLs that may hold a <c>&lt;dockablepane classname&gt;</c>: the tab DLL itself and, when present,
    /// <see cref="ResourcesDllName"/> in the same folder (the parser resolves the class through the tab DLL's references).
    /// </summary>
    public static IReadOnlyList<string> PaneDllsFor(string tabDllPath)
    {
        var list = new List<string> { tabDllPath };
        var dir = Path.GetDirectoryName(tabDllPath);
        if (dir is not null)
        {
            var resources = Path.Combine(dir, ResourcesDllName);
            if (File.Exists(resources) && !string.Equals(resources, tabDllPath, StringComparison.OrdinalIgnoreCase))
                list.Add(resources);
        }
        return list;
    }

    private static ScanResult ScanFor(string dllPath, string interfaceName)
    {
        try
        {
            byte[] bytes;
            // Revit may hold the DLL open; share for read/write/delete and copy it into memory.
            using (var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                bytes = new byte[fs.Length];
                fs.ReadExactly(bytes);
            }

            using var pe = new PEReader(new MemoryStream(bytes));
            if (!pe.HasMetadata)
                return new ScanResult(Array.Empty<string>(), "The file is not a .NET assembly.");

            var md = pe.GetMetadataReader();
            var names = new List<string>();

            foreach (var handle in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(handle);
                var attrs = type.Attributes;

                if ((attrs & TypeAttributes.Interface) != 0 || (attrs & TypeAttributes.Abstract) != 0)
                    continue;

                var visibility = attrs & TypeAttributes.VisibilityMask;
                if (visibility != TypeAttributes.Public && visibility != TypeAttributes.NestedPublic)
                    continue;

                if (md.GetString(type.Name).Contains('`'))
                    continue; // generic

                if (Implements(md, type, interfaceName, depth: 0))
                    names.Add(FullName(md, type));
            }

            names.Sort(StringComparer.Ordinal);
            return new ScanResult(names, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
        {
            return new ScanResult(Array.Empty<string>(), ex.Message);
        }
    }

    private static bool Implements(MetadataReader md, TypeDefinition type, string interfaceName, int depth)
    {
        foreach (var implHandle in type.GetInterfaceImplementations())
        {
            var iface = md.GetInterfaceImplementation(implHandle).Interface;
            if (iface.Kind == HandleKind.TypeReference)
            {
                var tr = md.GetTypeReference((TypeReferenceHandle)iface);
                if (md.GetString(tr.Name) == interfaceName && md.GetString(tr.Namespace) == InterfaceNamespace)
                    return true;
            }
            else if (iface.Kind == HandleKind.TypeDefinition)
            {
                var td = md.GetTypeDefinition((TypeDefinitionHandle)iface);
                if (md.GetString(td.Name) == interfaceName && md.GetString(td.Namespace) == InterfaceNamespace)
                    return true;
            }
        }

        // Follow a base class defined in the same module (Cmd_X : Cmd_Base). Cross-assembly bases are not followed.
        if (depth < 8 && !type.BaseType.IsNil && type.BaseType.Kind == HandleKind.TypeDefinition)
        {
            var baseType = md.GetTypeDefinition((TypeDefinitionHandle)type.BaseType);
            return Implements(md, baseType, interfaceName, depth + 1);
        }
        return false;
    }

    private static string FullName(MetadataReader md, TypeDefinition type)
    {
        var name = md.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
            return FullName(md, md.GetTypeDefinition(declaring)) + "+" + name;

        var ns = md.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
