# Ribbon XML Editor

A Windows desktop editor for the `.ribbon` XML files that define the ACCO Revit ribbon tabs
(ConTech, Engineering BIM Team, Engineering Mechanical). It replaces hand-editing the XML with a
tree + property panel, and checks the file against what Revit's `RibbonBuilder` will actually do
with it before you save.

| | |
|---|---|
| **Users** | ACCO IT developers, and admin managers who configure per-department deployments |
| **Tech** | WPF, .NET 10 (`net10.0-windows`), no NuGet dependencies in the app |
| **Publish** | single self-contained `RibbonXmlEditor.exe`, runs without a .NET install |
| **Schema source of truth** | `BTT_ACCORevit-Ribbons\RevitRibbon_MainSourceCode\Ribbon Builder\RibbonBuilder.cs` |

## Where the files live

| What | Path |
|---|---|
| Source-of-truth `.ribbon` (edit these to make a permanent change) | `BTT_ACCORevit-Ribbons\0N.<Tab>_Tab\0N.<Tab>_Tab.ribbon` |
| Deployed copy Revit actually reads | `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\<Tab>\<year>\<Tab>.ribbon` (next to the tab DLL) |
| Button images | `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\Images\Name_16x16.png` / `_32x32` / `_192x192` |
| Editor backups (last 20 per file) | `%LocalAppData%\RibbonXmlEditor\Backups\` |
| Editor settings (recent files, DLL per ribbon, images folder) | `%LocalAppData%\RibbonXmlEditor\settings.json` |

> **Deployed copies are overwritten.** Every add-in deployment (MSI install or a developer build of the
> tab project) copies the repo's `.ribbon` over the deployed one. Edits made to a deployed copy are
> temporary until a developer carries them into the repo. The editor shows a yellow notice and a bold
> status-bar path whenever the open file is under `C:\ACCORevit\`.

## Using the editor

1. **File → Open** a `.ribbon` (or drag files onto the window, or use *Open with* on the file).
   Each file opens in its own **tab** with its own tree, property panel, issues and preview, so several
   ribbons can be edited side by side. Opening a file that is already open switches to its tab.
   Close a tab with its ✕, a middle-click, `Ctrl+W` (**File → Close Loaded Ribbon**), or right-click
   the tab for *Close Others* / *Close All*. **File → Save All** saves every tab with changes.
2. The **tree** on the left shows Tab → Panel → structure (Stacked items, Split button, Slide-out,
   Radio group, Separator) → items (Button, Pulldown, Combo box, Text box, …).
   Right-click for **Add / Duplicate / Disable / Move Up / Move Down / Delete** (`Del`, `Alt+↑/↓`,
   `Ctrl+D`, `Ctrl+E`). The *Add* menu only offers what Revit accepts there and disables at the limit
   (3 stacked items). **Disable** keeps an item in the file as an XML comment so Revit skips it; the
   tree shows it grey, the preview shows it ghosted, and **Enable** brings it back.
3. The **property panel** on the right edits the selected item. Every attribute Revit reads is shown;
   required ones are marked `*`. Captions and tooltips accept `Enter` for a line break, exactly as
   the production files do.
   - **Image fields**: Browse (defaults to the Images folder), a thumbnail, and a red `?` when the
     file is missing. Picking `Name_16x16.png` offers to fill the `_32x32` / `_192x192` siblings.
   - **Command class**: an editable drop-down. **Tools → Select Tab DLL…** (auto-detected when a
     `*_Tab.dll` sits next to the ribbon) lists every `IExternalCommand` class in the DLL. The DLL is
     read as metadata only, so it works on machines without Revit and with 2023–2027 builds alike.
   - **Tab node**: also edits the file header (`<!-- Version x.y.z yyyy-mm-dd -->`).
4. The **Issues** tab at the bottom updates as you type. Double-click an issue to jump to the field.
   *Errors* mean Revit will throw, crash, or silently truncate the panel; *warnings* mean it works
   but looks wrong. Saving with errors asks for confirmation.
5. The **Ribbon preview** tab draws a schematic ribbon: panels left to right, a single stacked item
   large, two or three stacked items as small rows, pulldown / split / combo arrows that drop their
   lists on click, and a chevron on the panel title for slide-outs. Click any item to select it in the
   tree; the tree selection is outlined in blue. Red and orange outlines follow the validator, and a
   coloured glyph stands in for an empty or missing image.
6. **Save** (`Ctrl+S`) backs the previous file up, then writes atomically (temp file + rename).
   Closing the window prompts once for each tab with unsaved changes.

The first save of a hand-written production file re-indents it (one attribute per line, two-space
indent). Content is unchanged; the round-trip test proves it.

## What the validator checks

Errors: required attribute empty · stacked items not 1–3 (Revit stops reading the rest of the panel)
· element not allowed in its parent · image path relative or file missing · help URL not http(s)
· duplicate item name within a panel or container · duplicate panel name · more than one split
button per panel.

Warnings: class name malformed or not found in the selected DLL · empty pulldown / combo / radio /
split / slide-out · image size does not match the field (`image` = 16x16, `largeimage` = 32x32,
`tooltipimage` = 192x192) · `showimage` not `"true"` · a second `*.ribbon` in the target folder
(Revit loads only the first one it finds) · comments or unknown elements dropped on load.

## Build, test, publish

```powershell
dotnet test RibbonXmlEditor.Tests\RibbonXmlEditor.Tests.csproj     # round-trip, validator, scanner tests
dotnet run --project RibbonXmlEditor\RibbonXmlEditor.csproj        # run from source
.\publish.ps1                                                       # -> publish\win-x64\RibbonXmlEditor.exe
.\publish.ps1 -Sign                                                 # also sign with the ACCO certificate
```

Requires the .NET 10 SDK (Visual Studio 2026 / `dotnet --list-sdks`). The published exe needs only
Windows 10/11 x64. WPF cannot be trimmed and the profile pre-compiles (ReadyToRun) for fast startup,
so the exe is about 140 MB; set `PublishReadyToRun` to `false` in the publish profile for a ~70 MB
build that starts a little slower. On first run it extracts its native libraries to `%TEMP%\.net\`.

## Project layout

```
RibbonXmlEditor/
  Schema/        RibbonSchema.cs = the one table of elements, attributes and allowed children
  Models/        RibbonNode (generic element), RibbonDocument (header + tab), Issue
  Services/      RibbonXmlReader/Writer, RibbonValidator, CommandClassScanner, ImageCatalog,
                 BackupService, AppSettings, Dialogs, TemplateProvider
  ViewModels/    MainViewModel, DocumentViewModel, NodeViewModel, Fields/*, IssueViewModel
  Views/         MainWindow, TreeTemplates.xaml, FieldTemplates.xaml, FocusBehavior, Converters
  Resources/     ButtonStructureTemplate.ribbon (embedded; File -> New from Template)
RibbonXmlEditor.Tests/   xUnit; production-file tests skip when the repo / deployed folders are absent
```

## Keeping the schema in sync

The `.ribbon` format has no XSD; it is whatever `RibbonBuilder.cs` parses. The canonical copy of that
parser lives in this repository at `RibbonXmlEditor\RibbonBuilder\RibbonBuilder.cs` (with its own
README on how to use it in other add-ins); `BTT_ACCORevit-Ribbons` carries a copy. Facts the editor
relies on:

- Builders before 3.0 read every attribute unconditionally, so **the writer always emits every
  attribute**, empty if unset. Version 3.0 tolerates a missing one, but older DLLs are still deployed.
- The parser loads with `XmlDocument`, which keeps raw line breaks inside attribute values. The
  editor reads with `XmlDocument` too (an `XDocument`/`XmlReader` load would collapse them to spaces).
- Disabled items are XML comments; version 3.0 ignores comments, older builders count them as stack
  items (the validator warns when that matters).

Two tests enforce the sync: `SchemaSyncTests` compares `RibbonBuilder.XmlNames` with
`Schema\RibbonSchema.cs`, and checks that the BTT copy is identical to the canonical one. To change
the format, edit the builder's `XmlNames` and code, update `RibbonSchema.cs`, run the tests, then
copy the file into every add-in solution that uses it.
