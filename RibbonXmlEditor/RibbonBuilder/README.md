# RibbonBuilder

`RibbonBuilder.cs` is a single-file helper for Revit add-ins. At startup it reads a `.ribbon` XML file
that sits next to the add-in DLL and creates the matching ribbon tab, panels and buttons. The
add-in's `IExternalApplication` becomes three lines, and the ribbon layout lives in a file that can be
edited without recompiling (and without a developer, using the Ribbon XML Editor).

| | |
|---|---|
| **Version** | 3.0.0 (2026-10-01) |
| **Revit** | 2023, 2024, 2025, 2026, 2027 |
| **Frameworks** | net48, net8.0-windows, net10.0-windows (C# latest; no features missing on .NET Framework 4.8) |
| **Dependencies** | `RevitAPIUI.dll` (the `Revit_All_Main_Versions_API_x64` NuGet) and WPF (`BitmapImage`) |
| **Namespace / class** | `RevitRibbon_MainSourceCode.RibbonBuilder` |
| **Canonical copy** | this folder; `BTT_ACCORevit-Ribbons\RevitRibbon_MainSourceCode\Ribbon Builder\RibbonBuilder.cs` is a copy of it |

## Folder convention

```
<wherever the .addin points>\
    MyTab.dll              the add-in (IExternalApplication + IExternalCommand classes)
    MyTab.ribbon           exactly ONE .ribbon file next to the DLL
    ...other DLLs
```

- One `.ribbon` per folder. If several exist the first one in alphabetical order is used and a
  problem is reported.
- Image attributes are absolute paths (ACCO keeps them in
  `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\Images\`, named `Name_16x16.png`,
  `Name_32x32.png`, `Name_192x192.png`). Since 3.0 a relative path is resolved against the ribbon
  folder.

## Adopt it in another add-in project

1. Copy `RibbonBuilder.cs` into the project. Keep the `RevitRibbon_MainSourceCode` namespace or change
   it consistently; nothing else references it.
2. Make sure the project references the Revit API and WPF:
   ```xml
   <UseWPF>true</UseWPF>
   <PackageReference Include="Revit_All_Main_Versions_API_x64" Version="2025.*" IncludeAssets="build; compile" PrivateAssets="All" />
   ```
   (or direct references to `RevitAPI.dll` / `RevitAPIUI.dll`).
3. Call it from `OnStartup`:
   ```csharp
   using System.IO;
   using System.Reflection;
   using Autodesk.Revit.UI;
   using RevitRibbon_MainSourceCode;

   public class App : IExternalApplication
   {
       public Result OnStartup(UIControlledApplication application)
       {
           string dll = Assembly.GetExecutingAssembly().Location;
           RibbonBuilder.BuildRibbon(application, dll, Path.GetDirectoryName(dll));
           return Result.Succeeded;
       }

       public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;
   }
   ```
   The old `RibbonBuilder.build_ribbon(app, dllPath, folder)` still compiles (marked obsolete).
4. Optional settings and event hooks:
   ```csharp
   RibbonBuilder.BuildRibbon(application, dll, Path.GetDirectoryName(dll), new RibbonBuildOptions
   {
       RibbonFile = "MyTab.ribbon",                 // instead of "first *.ribbon in the folder"
       ShowProblemsDialog = true,                   // one TaskDialog listing problems (default)
       Log = msg => Trace.WriteLine(msg),           // every problem + the summary
       ComboBoxCurrentChanged = (s, e) => { /* read ((ComboBox)s).Current.Name */ },
       TextBoxEnterPressed    = (s, e) => { /* read ((TextBox)s).Value */ },
   });
   ```
   The combo-box and text-box handlers are attached to every combo box / text box the file defines.
5. Put a `.ribbon` file next to the DLL (start from `ButtonStructureTemplate.ribbon` in the editor's
   Resources folder, or open the editor and use *File → New from Template*). The `.addin` manifest
   points Revit at the DLL as usual; the ribbon file needs no registration.

## The `.ribbon` format

A `.ribbon` file is XML. Root `<tab>`, then `<panel>`s, then one of the panel structures below.
Attribute names are lower-case. **Every attribute an element supports should be present**, empty if
unused (`tooltip=""`): version 3.0 tolerates missing attributes, but builders before 3.0, still
deployed on some machines, crash on a missing one. The Ribbon XML Editor always writes all of them.

| Element | Attributes (`*` required) | Allowed children | Notes |
|---|---|---|---|
| `tab` | name* | panel | one per file |
| `panel` | name* | separator, stackeditems, splitbuttons, slideoutpanel, radiobuttons | |
| `separator` | | | vertical divider |
| `stackeditems` | | button, pulldownbuttons, combobox, textbox | 1 item = large; 2 or 3 = stacked small rows |
| `splitbuttons` | | button | large button whose lower half lists all its buttons |
| `slideoutpanel` | name | button | buttons hidden behind the panel title |
| `radiobuttons` | name* | togglebutton | one toggle active at a time |
| `button` | name*, classname*, text*, tooltip, image, largeimage, tooltipimage, contexthelp | | `classname` = full name of the `IExternalCommand`; `contexthelp` = http(s) URL |
| `pulldownbuttons` | name*, image* | button | `image` is shown as the large image |
| `combobox` | name*, itemtext, longdescription, tooltip, tooltipimage | comboboxmember | |
| `comboboxmember` | name*, text*, image, groupname | | members with the same `groupname` are listed together |
| `textbox` | name*, prompttext, longdescription, image, showimage, tooltip, tooltipimage | | `showimage="true"` makes the image a clickable button |
| `togglebutton` | name*, text*, tooltip, largeimage | | |

Captions and tooltips may contain real line breaks inside the quotes; Revit shows them as line breaks.
Item names must be unique within a panel (Revit rejects duplicates).

Minimal example:

```xml
<?xml version="1.0" encoding="utf-8"?>
<!-- Version 1.0.0 2026-10-01 -->
<tab name="My Tab">
  <panel name="Tools">
    <stackeditems>
      <button name="About"
              classname="MyAddin.Cmd_About"
              text="About"
              tooltip="Shows the version."
              image="C:\MyAddin\Images\About_16x16.png"
              largeimage="C:\MyAddin\Images\About_32x32.png"
              tooltipimage=""
              contexthelp="" />
    </stackeditems>
  </panel>
</tab>
```

### Internal names of pulldown and split buttons

Revit builds the command ID of a button inside a pulldown or split button from the parent's *internal*
name, and those IDs are what keyboard shortcuts and Quick Access Toolbar pins refer to. The builder
therefore keeps the literal internal names earlier versions used: the first pulldown in a panel is
`PullDown` and the first split button is `SplitButton`; a second one in the same panel gets
`PullDown2` / `SplitButton2`, and so on. Existing shortcuts keep working; a second pulldown per panel
now works too (before 3.0 it threw a duplicate-name exception).

### Disabling an item

An item can be switched off without deleting it by turning its XML block into a comment:

```xml
<stackeditems>
  <!--
  <button name="Old Tool" classname="..." text="Old Tool" tooltip="" image="" largeimage="" tooltipimage="" contexthelp="" />
  -->
</stackeditems>
```

The Ribbon XML Editor does this with *Disable* / *Enable* (right-click the item, or Ctrl+E). Version
3.0 skips comments entirely. Builders before 3.0 also skip them, but they count a comment as one of the
1–3 stacked items, so with an older builder keep enabled + disabled items in a stack at three or fewer.

## What happens on errors

Nothing in the file can abort the ribbon. Problems are collected and shown once, in a
"Ribbon XML problems" dialog after the tab is built (set `ShowProblemsDialog = false` to suppress it;
every message still goes to `Debug.WriteLine` and the `Log` callback).

| Condition | Result |
|---|---|
| No `.ribbon` file, unreadable XML, root is not `<tab>`, tab has no name | nothing built, one dialog |
| Tab or panel already exists (another add-in created it) | reported; panels are added to the existing tab |
| Item missing a required attribute | that item is skipped |
| Missing optional attribute | treated as empty |
| `stackeditems` with 0 or more than 3 items | that stack is skipped; the rest of the panel is still built |
| Image file missing or unreadable | reported; the item is created without the image |
| Duplicate item names, invalid URL, other Revit rejections | reported; that item is skipped |
| Unknown element in the wrong place | reported and ignored |

Before 3.0 a missing attribute threw at startup (Revit's generic "add-in failed" dialog), and a stack
with the wrong item count silently dropped the rest of the panel.

## Relationship to the Ribbon XML Editor

- The editor (this repository) edits and validates `.ribbon` files against exactly this format. Its
  schema table, `RibbonXmlEditor\Schema\RibbonSchema.cs`, mirrors the `RibbonBuilder.XmlNames`
  constants, and the test `SchemaSyncTests` fails when they drift. A second test checks that the copy
  in `BTT_ACCORevit-Ribbons` is byte-identical to this one.
- To change the format: edit `XmlNames` and the matching code here, update `RibbonSchema.cs`, run the
  editor tests, then copy the file into every add-in solution that uses it.

## Changelog

| Version | Date | Change |
|---|---|---|
| 1.0.0 | 2021-06-01 | Created by Alana Bianes |
| 2.0.0 | 2024-08-22 | Combo box, text box, radio group, split and slide-out support |
| 3.0.0 | 2026-10-01 | Rewritten as a hardened single-file drop-in: `BuildRibbon` + `RibbonBuildOptions` (file override, logging, combo/text-box events), tolerant attribute reading, per-item error reporting in one dialog, comments never count as items, indexed `PullDown2`/`SplitButton2` names, images loaded with `OnLoad` + `Freeze` so PNGs are not kept locked, relative image paths. `build_ribbon` kept as an obsolete alias. |
