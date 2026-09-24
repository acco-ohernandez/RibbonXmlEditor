# RibbonXmlEditor — Implementation Plan

## Context

`C:\Visual Studio Files\RibbonXmlEditor` is a WPF scaffold created 2026-09-23 (net10.0-windows, empty window, not a git repo). Its purpose is a GUI editor for the `.ribbon` XML files that define ACCO's Revit ribbon tabs, so that the developer and **admin managers configuring per-department deployments** can add, remove, and edit buttons without hand-editing XML.

Today a hand-edit mistake is expensive: the runtime parser `BTT_ACCORevit-Ribbons\RevitRibbon_MainSourceCode\Ribbon Builder\RibbonBuilder.cs` reads every attribute unconditionally (missing attribute = NullReferenceException at Revit startup), throws on bad image paths, and silently truncates a panel when a `stackeditems` has 0 or >3 children. The editor makes those states impossible or visible.

Decisions already made with the user:
- **UI**: tree (tab → panel → structure → item) + property panel. XML generated on save, never hand-edited.
- **Schema**: full RibbonBuilder schema (all 13 element kinds).
- **classname**: editable ComboBox populated by scanning the tab DLL when one is chosen; free text + warning otherwise.
- **Audience**: developer + admin managers → must publish as a self-contained single exe. Keep net10.0-windows.

Verified facts that shape the design (do not re-derive):
- `XmlDocument.Load` (what Revit uses) preserves raw CRLF inside attribute values; `XDocument.Load` collapses them to spaces. Production captions rely on raw newlines (`text="Connect` / `Two Elements"`). **Reader must use `XmlDocument`.** Confirmed 2026-09-23 with both in-memory and file loads.
- Production `.ribbon` files: no BOM, CRLF, UTF-8, 1–2 leading comments (`<!-- Copied from: … -->` optional, then `<!-- Version x.y.z yyyy-MM-dd -->`), non-ASCII present (•, —, °).
- Runtime: `Directory.GetFiles(dir, "*.ribbon")[0]` → exactly one `.ribbon` next to the tab DLL. Deployed layout `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\<Tab>\<year>\`; images in `…\02-ACCORevit Ribbons\Images\` (124 PNGs, `<Name>_16x16/_32x32/_192x192.png`). Repo copies are the source of truth and redeploys overwrite deployed copies.
- Tab DLLs contain all `Cmd_*` classes (shared project), reference RevitAPIUI.dll (not on admin machines), are net48 for 2023/2024 and net8/net10 otherwise. All commands are public, non-nested, implement `Autodesk.Revit.UI.IExternalCommand` directly.
- Test DLL paths exist: `…\Engineering\Mechanical\2025\03.Engineering_Mechanical_Tab.dll`, `…\2024\…dll`, and `BTT_ACCORevit-Ribbons\03.Engineering_Mechanical_Tab\bin\Release\2025\03.Engineering_Mechanical_Tab.dll`.

## Architecture

MVVM-lite, **no NuGet in the app project** (hand-rolled `ObservableObject` + `RelayCommand`). One generic `RibbonNode` (kind + ordered attributes + children) driven by a single static **`RibbonSchema` table** that reader, writer, validator, and property panel all consume — the attribute list lives in exactly one place. Property panel uses **per-field-kind** DataTemplates, not per-node-type, so XAML never repeats the attribute list.

### Project layout (`RibbonXmlEditor\RibbonXmlEditor\`)

```
App.xaml(.cs)                      build MainViewModel; open e.Args[0] ("Open with"); global exception → MessageBox
Schema/
  ElementKind.cs                   Tab, Panel, Separator, StackedItems, SplitButtons, SlideoutPanel, RadioButtons,
                                   Button, PulldownButtons, ComboBox, TextBox, ComboBoxMember, ToggleButton
  FieldKind.cs                     Text, MultilineText, ImagePath, ClassName, Url, TrueOrEmpty
  AttributeDef.cs / ElementDef.cs  records (XmlName, Label, FieldKind, IsRequired) / (Kind, XmlName, Attributes, AllowedChildren, Min/MaxChildren)
  RibbonSchema.cs                  THE table (below) + ByXmlName/ByKind lookups
Models/
  RibbonDocument.cs                FilePath, LeadingComments, Version, VersionDate, Tabs
  RibbonNode.cs                    Kind, Def, Attributes (ordered), ExtraAttributes, Children, Parent, DeepClone()
Services/
  RibbonXmlReader.cs               XmlDocument → RibbonDocument + load-time Issues
  RibbonXmlWriter.cs               RibbonDocument → file (temp + atomic move), backup first
  RibbonValidator.cs               rules below → List<Issue>
  CommandClassScanner.cs           System.Reflection.Metadata → sorted class names
  ImageCatalog.cs                  Images-folder enumeration, _16x16 → _32x32/_192x192 sibling lookup, non-locking thumbnails
  AppSettings.cs                   %LocalAppData%\RibbonXmlEditor\settings.json: recent files, last DLL per ribbon, images folder
ViewModels/
  ObservableObject.cs, RelayCommand.cs
  MainViewModel.cs                 file commands, Document, SelectedNode, Issues, DllPath, ScannedClasses, status
  DocumentViewModel.cs             tree root; version/date/comments; IsDirty
  NodeViewModel.cs                 wraps RibbonNode; IsSelected/IsExpanded; Fields; Add/Delete/MoveUp/MoveDown/Duplicate; AddChildOptions
  Fields/FieldViewModel.cs (+ TextField, MultilineTextField, ImagePathField, ClassNameField, UrlField, TrueOrEmptyField)
  IssueViewModel.cs                Severity, Message, Node, FieldName
Views/
  MainWindow.xaml(.cs)             rename of RibbonXmlEditor_Window.xaml; layout only
  TreeTemplates.xaml               HierarchicalDataTemplate, TreeViewItem style (IsSelected/IsExpanded TwoWay), ContextMenu
  FieldTemplates.xaml              implicit DataTemplates per field VM type
  IssuesPanel.xaml                 ListView; double-click navigates to node/field
Resources/
  ButtonStructureTemplate.ribbon   EmbeddedResource (moved from Samples\) for "New from Template"
  Icons\*.png, app.ico
Properties/PublishProfiles/SelfContained-win-x64.pubxml
```
Solution root: `README.md`, `publish.ps1`, `.gitignore`, new `RibbonXmlEditor.Tests\` (xUnit) added to `RibbonXmlEditor.slnx`. Delete `Samples\RibbonBuilder.cs` (drifting copy; README points at the real one) and the `Samples` folder; drop the `<Compile Remove>` line.

### RibbonSchema table (transcribed from RibbonBuilder.cs; `*` = required)

| Element | Attributes | Allowed children | Count |
|---|---|---|---|
| tab | name* | panel | any |
| panel | name* | separator, stackeditems, splitbuttons, slideoutpanel, radiobuttons | any |
| separator | — | — | 0 |
| stackeditems | — | button, pulldownbuttons, combobox, textbox | **1..3** |
| splitbuttons | — | button | 1+ |
| slideoutpanel | name (ignored by parser) | button | 1+ |
| radiobuttons | name* | togglebutton | 1+ |
| button | name*, classname*, text*, tooltip, image, largeimage, tooltipimage, contexthelp | — | 0 |
| pulldownbuttons | name*, image* (→ LargeImage) | button | 1+ |
| combobox | name*, itemtext, longdescription, tooltip, tooltipimage | comboboxmember | 1+ |
| comboboxmember | name*, text*, image, groupname | — | 0 |
| textbox | name*, prompttext, longdescription, image, showimage, tooltip, tooltipimage | — | 0 |
| togglebutton | name*, text*, tooltip, largeimage | — | 0 |

Field kinds: text/tooltip/longdescription/prompttext/itemtext → MultilineText; image/largeimage/tooltipimage → ImagePath; classname → ClassName; contexthelp → Url; showimage → TrueOrEmpty; else Text. **Writer always emits every attribute in the table** (empty string when unset) because the parser reads them unconditionally.

## UX

- **Layout**: DockPanel — Menu top, StatusBar bottom, Grid: tree | property panel, issues list spanning below, GridSplitters.
- **File menu**: New (tab+panel+stackeditems+button), New from Template…, Open… (`*.ribbon`), Open Recent (10), Save (Ctrl+S), Save As…, Open Backups Folder, Exit. **Tools**: Select Tab DLL…, Rescan DLL, Set Images Folder…. **Help**: About (version + schema source-of-truth note).
- **Tree**: icon + `<kind>: <name>`; stackeditems shows "(2/3)". Context menu: Add ▸ (from `AllowedChildren`, disabled at MaxChildren so a 4th stacked child is impossible), Duplicate (deep clone, name + " Copy"), Move Up/Down (Alt+Up/Down), Delete (confirm if it has children). New nodes get all schema attributes as "" and are selected.
- **Property panel** field templates:
  - Text: label (+ red * if required) + TextBox.
  - MultilineText: `AcceptsReturn` TextBox, hint "Enter = line break in Revit".
  - ImagePath: TextBox + Browse (PNG filter, InitialDirectory = Images folder) + 32px thumbnail (`BitmapCacheOption.OnLoad` + `IgnoreImageCache`, never locks the PNG) + "file not found" flag. Nice-to-have: picking `_16x16` offers to fill `_32x32`/`_192x192` siblings.
  - ClassName: editable ComboBox (`IsEditable`, `IsTextSearchEnabled`, ItemsSource = ScannedClasses) + "Tab DLL: <path>" + Select button. Auto-detect on open: `Path.ChangeExtension(ribbon, ".dll")` → single `*_Tab.dll` in folder → AppSettings memory → none.
  - Url: TextBox + Open link. TrueOrEmpty: CheckBox ↔ "true"/"".
  - Tab node: name, Version, Date (DatePicker + Today), read-only other comments. Nice-to-have: "also update About tooltip 'Latest Version: …'".
- **Issues panel**: severity, message, node path; double-click expands ancestors, selects node, focuses field. Revalidate on change, debounced 250 ms.
- **Status bar**: full path (bold if under `C:\ACCORevit\` = editing a deployed copy), dirty `*`, issue counts, DLL state. One-time info bar on opening a deployed copy: "redeploys overwrite this file; source of truth is the repo".

## Validation rules (RibbonValidator)

Error = Revit throws/crashes/truncates; Warning = works but suspicious. Save with errors → "N errors, save anyway?"; warnings never block.

1. E required attribute empty (name, classname, text, pulldown image, radio name).
2. E stackeditems child count ∉ 1..3 — message: "Revit stops reading the rest of panel '<p>' here (parser `break`)".
3. E child type not allowed for container.
4. E image non-empty and file missing (`new BitmapImage(new Uri())` throws at startup).
5. E image non-empty and not `Path.IsPathFullyQualified`.
6. E contexthelp non-empty and not absolute http/https URI.
7. E duplicate item `name` within a panel (all stacked items + split/slideout buttons); duplicate within pulldown/combobox/radio.
8. E more than one splitbuttons per panel (parser hardcodes `SplitButtonData("SplitButton", …)`).
9. E duplicate panel name in tab; duplicate tab name; empty tab/panel name.
10. W classname not in scanned DLL (only when a DLL is loaded).
11. W classname fails `^[A-Za-z_]\w*([.+][A-Za-z_]\w*)+$`.
12. W container (pulldown/combobox/radio/split/slideout) with zero children.
13. W multiple `<tab>` roots; comments found inside containers on load (dropped).
14. W showimage not "" or "true".
15. W size convention: image has `_32x32`/`_192x192`, largeimage has `_16x16`, tooltipimage has `_16x16`/`_32x32`, pulldown image has `_16x16`.
16. W Images folder missing on this machine → downgrade rule 4 to warnings (admin box without add-in).
17. W (save-time) another `*.ribbon` already in target folder — Revit loads only `GetFiles()[0]`.

## XML I/O

**Reader**: `new XmlDocument().Load(path)` (identical to Revit's code path; preserves attribute newlines; comment nodes visible). Collect leading comments; parse `^\s*Version\s+(\d+\.\d+\.\d+)\s+(\d{4}-\d{2}-\d{2})\s*$`. Map via `Schema.ByXmlName`. Missing attribute → "" + Issue "file had missing attribute X on <button name=…>; Revit would crash; fixed on save" + mark dirty. Unknown attributes → `ExtraAttributes` (written back, warned). Unknown elements / comments inside containers → dropped with warning. Normalize values to `\n` internally.

**Writer**: `XmlWriterSettings { Indent=true, IndentChars="  ", NewLineChars="\r\n", NewLineOnAttributes=true, NewLineHandling=None, Encoding=new UTF8Encoding(false) }`. `NewLineHandling.None` writes raw CRLF inside attribute quotes exactly like production (readable in Notepad, line-oriented diffs; Revit reads raw and `&#xA;` identically — verified). Attribute values `\n` → `\r\n` on write. Write leading comments (version comment regenerated; appended if absent), then tabs; every schema attribute in table order, then extras. Write to temp file in same folder, then `File.Move(overwrite:true)` so a half-written ribbon never sits next to the DLL.

**Backup**: before overwrite, copy to `%LocalAppData%\RibbonXmlEditor\Backups\<file>.<yyyyMMdd-HHmmss>.bak`, keep 20. Not next to the ribbon (keeps deployed folders clean; nothing can match `*.ribbon`).

**First-save diff impact** on a production file: every line changes once (indent normalization, `name=` on its own line for tab/panel, blank lines gone). Semantics identical — proven by the round-trip test. Later diffs are attribute-per-line minimal.

## DLL scanner (CommandClassScanner)

`System.Reflection.Metadata` (in-box): open with `FileShare.ReadWrite | FileShare.Delete`, read bytes to memory, close; `PEReader` → `MetadataReader`; for each non-abstract, non-interface, public/nested-public, non-generic `TypeDefinition`, check `GetInterfaceImplementations()` for a `TypeReference` named `IExternalCommand` in namespace `Autodesk.Revit.UI`; follow `BaseType` when it is a same-module `TypeDefinitionHandle` (depth ≤ 8). Full name = `Namespace.Name`, or `Outer+Inner` via `GetDeclaringType()`. Runs on a background Task; IO/BadImageFormat → Issue, not crash. Works for net48 and net8/net10 DLLs alike and never needs RevitAPIUI.dll (the reason `MetadataLoadContext` is rejected: it needs a NuGet, a per-flavor core-lib resolver, and `GetInterfaces()` throws without RevitAPIUI).

## Publishing (admins)

`Properties\PublishProfiles\SelfContained-win-x64.pubxml`: Release, `win-x64`, `SelfContained`, `PublishSingleFile`, `IncludeNativeLibrariesForSelfExtract`, `PublishReadyToRun`, `PublishTrimmed=false` (WPF unsupported), `DebugType=none`, `PublishDir=..\publish\win-x64\`. Keep RID out of the main PropertyGroup so F5 stays framework-dependent. csproj adds `Version`/`AssemblyVersion`/`FileVersion` 1.0.0, `ApplicationIcon`, `SatelliteResourceLanguages=en`, EmbeddedResource template. `publish.ps1`: `dotnet publish .\RibbonXmlEditor\RibbonXmlEditor.csproj -p:PublishProfile=SelfContained-win-x64`. Nice-to-have: sign the exe with the ACCO cert (`C:\Visual Studio Files\Sectigo\Sign-File.bat`) to avoid SmartScreen.

## Milestones (each builds and runs)

0. `git init` + `.gitignore` (bin/obj/.vs/publish) in `C:\Visual Studio Files\RibbonXmlEditor`.
1. **M1** Schema, Models, Reader, Writer, xUnit `RibbonXmlEditor.Tests` (net10.0-windows, UseWPF, xunit + runner + Test.Sdk). Tests: round-trip the 4 repo ribbons + template (read → write → read → structural equality; skip if repo absent); `btnConnectTo.text` contains `\n` after load and round-trip; output has no BOM, CRLF, `encoding="utf-8"`; missing-attribute file loads with Issue and writes attribute back.
2. **M2** Tree + property panel + open/save, AppSettings/recent, backup, dirty tracking, close prompt.
3. **M3** Validator + issues panel + debounce + save gate + navigation; a test per rule with in-memory docs.
4. **M4** ImageCatalog + ImagePath template + sibling fill; CommandClassScanner + ClassName ComboBox + DLL auto-detect + rule 10. Test: scan the 2025 and 2024 Mechanical DLLs; both contain `RevitRibbon_MainSourceCode.Unique_Button_Classes.Mechanical.Cmd_Parallel`.
5. **M5** pubxml, publish.ps1, csproj metadata, icon, README (purpose, audience, file locations, redeploy caveat, schema source of truth, publish steps), delete Samples.

## Verification (end to end)

1. `dotnet test` green.
2. Run app → Open `BTT_ACCORevit-Ribbons\03.Engineering_Mechanical_Tab\03.Engineering_Mechanical_Tab.ribbon`. Expect 4 panels / 8 stackeditems / 8 buttons, version 2.2.0 / 2026-07-14, "Copied from" comment listed, zero issues. Tools → Select Tab DLL → `03.Engineering_Mechanical_Tab\bin\Release\2025\03.Engineering_Mechanical_Tab.dll`; all classnames resolve; ComboBox lists Cmd_* classes.
3. In "Duct Tools": Add ▸ Stacked items ▸ Button; name `btnTest`, classname from picker, two-line text, image `A_16x16.png` + sibling fill. Issues go from errors to zero while typing.
4. Save As → `%TEMP%\RibbonEditorScratch\03.Engineering_Mechanical_Tab.ribbon`; `git diff --no-index` vs original: only whitespace restructuring + new button + version comment; `text="Connect` still spans two lines; no BOM; CRLF.
5. Re-open scratch file → identical tree.
6. Revit smoke test on a **throwaway copy only**: copy `…\Engineering\Mechanical\2025\` to `%TEMP%\RibbonEditorScratch\Mechanical2025\`, drop the saved ribbon in, point a temporary copy of the `.addin` at it, start Revit 2025, confirm the new two-line button. Never overwrite the real deployed or repo files during verification.
7. `publish.ps1` → copy single exe to a clean machine/Windows Sandbox without .NET → open a ribbon, thumbnails, DLL scan, save all work.

## Risks / pitfalls

- **XDocument flattens attribute newlines** → XmlDocument only; writer `NewLineHandling.None`; VM setters normalize WPF `\r\n`.
- **TreeView selection**: bind IsSelected/IsExpanded in a TreeViewItem style; navigate = expand ancestors, set IsSelected, `Dispatcher.BeginInvoke` focus.
- **DLL locked by Revit** → FileShare.ReadWrite, read into memory.
- **Redeploy overwrites admin edits** → status bar, info bar, README, LocalAppData backups.
- **stackeditems `break`** → rule 2 Error + Add menu disabled at 3.
- **Comments inside containers count as children at runtime** → never written; dropped on load with warning.
- **Duplicate names / double splitbuttons** throw in Revit → Errors 7–9.
- **Two `.ribbon` in one folder** → rule 17 at save.
- **WPF cannot be trimmed**; `IncludeNativeLibrariesForSelfExtract` needed for true single exe.
- **Thumbnail file locks** → OnLoad + IgnoreImageCache.
- **Schema drift**: a change to RibbonBuilder.cs must be mirrored in `RibbonSchema.cs` (two-place edit, documented in README).
