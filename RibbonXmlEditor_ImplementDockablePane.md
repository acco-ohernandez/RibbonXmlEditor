# RibbonXmlEditor — implement the `<dockablepane>` element (RibbonBuilder 3.1)

Brief for a fresh Claude Code session in `C:\Visual Studio Files\RibbonXmlEditor` (branch `Dev_01_Improvements`,
HEAD `7cce916`). Written 2026-10-08 from the BTT session that added the element to RibbonBuilder.
**Do not commit until the user says "Checkpoint".**

## 1. Why

RibbonBuilder (the runtime parser that turns a `.ribbon` file into a Revit tab) is now **3.1.0** in the production
solution and understands a new element, `<dockablepane>`, directly under `<tab>`. It was added for the ACCO Docs
(Link Library) dockable pane on the ENG Mechanical tab, so that panes — like buttons — are declared in the `.ribbon`
file instead of in per-tab C# code.

The editor's schema table (`RibbonXmlEditor\Schema\RibbonSchema.cs`) is a transcription of the parser's
`XmlNames` constants, and `RibbonXmlEditor.Tests\SchemaSyncTests.cs` fails the moment the two disagree. Right now:

- `ProductionCopy_IsIdenticalToCanonicalCopy` fails: the BTT copy of `RibbonBuilder.cs` is 3.1.0, the canonical
  copy in this repo (`RibbonXmlEditor\RibbonBuilder\RibbonBuilder.cs`) is still 3.0.0.
- Once the canonical copy is refreshed, `BuilderElementNames_MatchSchema` and `BuilderAttributeNames_MatchSchema`
  fail until the schema table learns `dockablepane`, `guid`, `title`, `startshidden`.

The user wants the editor to fully support the new element: tree, property panel, validation, preview, docs.

## 2. The element, exactly as RibbonBuilder 3.1 implements it

Source of truth: `C:\Visual Studio Files\BTT_ACCORevit-Ribbons\RevitRibbon_MainSourceCode\Ribbon Builder\RibbonBuilder.cs`
(method `Session.BuildDockablePane`, helpers `CreateProvider`, `ResolveType`, `SubscribeStartupHide`,
`OnFirstViewActivated`; public API `RibbonBuilder.DockablePaneRegistration`, `TryGetDockablePane`,
`GetDockablePane`, `GetDockablePaneProvider`). Read it before coding. Handbook text: BTT `Solution_ReadMe.md` §5.2.

```xml
<tab name="ENG Mechanical">
  <dockablepane name="ACCODocsLibrary"
                guid="ACC0D0C5-11B2-4A2B-9E77-3F1A6C5B2D41"
                title="ACCO Link Library"
                classname="RevitRibbon_MainSourceCode_Resources.Forms.LinkLibrary_Pane"
                startshidden="true" />
  <panel name="...">...</panel>
</tab>
```

| Attribute | Required | Parser behaviour |
|---|---|---|
| `name` | yes | Registry key; commands call `RibbonBuilder.TryGetDockablePane(name, out id)`. Compared case-insensitively. A second `<dockablepane>` with the same name in the file → reported ("a pane with this name was already built; the duplicate was ignored"), skipped. |
| `guid` | yes | `Guid.TryParse` (any standard format, braces/hyphens optional). Invalid → reported, pane skipped. Becomes the `DockablePaneId`. |
| `classname` | yes | Full type name of a **public class implementing `Autodesk.Revit.UI.IDockablePaneProvider`** with a public constructor taking `UIControlledApplication` (preferred) or no parameters. Resolved by reflection: the tab DLL first, then every assembly the tab DLL references (that is how a class in `RevitRibbon_MainSourceCode_Resources.dll` is found), then anything already loaded. Not found / wrong interface / no usable ctor / ctor threw / `RegisterDockablePane` threw → reported, pane skipped. |
| `title` | no | Caption of the pane in Revit (View > User Interface list, pane header). Empty → defaults to `name`. |
| `startshidden` | no | `"true"` (case-insensitive) → RibbonBuilder hides the pane once, on the first `ViewActivated` of the session (Revit shows a newly registered pane, and the first document's layout undoes an earlier `Hide`). Anything else = false. Same "true or empty" convention as `showimage`. |

Other parser facts that matter for the editor:
- `<dockablepane>` is allowed **only directly under `<tab>`**, at any position among the `<panel>`s; it has no
  children (children would be ignored like any unexpected element — the parser never looks at them).
- A pane that another add-in already registered under the same guid is **reused** (`DockablePane.PaneExists`):
  id recorded, no second registration, no problem reported. So the same `<dockablepane>` may legitimately
  appear in several tabs' `.ribbon` files with the same guid.
- All `<dockablepane>` problems go into the same "Ribbon XML problems" TaskDialog as everything else; the rest of
  the ribbon still builds. The pane class is created during `BuildRibbon` (inside `IExternalApplication.OnStartup`).
- Like every other element, the parser reads attributes with `GetAttribute` (missing = empty), iterates
  `ChildNodes.OfType<XmlElement>()` (comments never count), and keeps raw newlines in attribute values.

`XmlNames` as it now stands in 3.1 (the sync test's regex reads these):

```csharp
// Elements
... public const string ToggleButton = "togglebutton";
public const string DockablePane = "dockablepane";
// Attributes
... public const string Classname = "classname";
public const string Guid = "guid";
public const string Title = "title";
public const string StartsHidden = "startshidden";
```

Header comment is `// Version 3.1.0 2026-10-08`, constant `Version = "3.1.0"`, and the footer changelog gained a
3.1.0 line. New `using System.Reflection;`. The public registry API (`DockablePaneRegistration` class and the three
static methods) sits between `XmlNames` and `private sealed class Session` — it contains no `public const string`,
so the sync test's attribute scan is unaffected.

## 3. What to change in this repo

Work through these in order; `dotnet test RibbonXmlEditor.Tests` is the gate after each step (77 tests today).

1. **Refresh the canonical parser copy.** Copy
   `C:\Visual Studio Files\BTT_ACCORevit-Ribbons\RevitRibbon_MainSourceCode\Ribbon Builder\RibbonBuilder.cs`
   over `RibbonXmlEditor\RibbonBuilder\RibbonBuilder.cs` byte-for-byte (the sync test normalises CRLF/LF and trailing
   whitespace only). Do not edit the BTT file from this repo — if the review finds a parser bug, note it for the BTT
   session instead. (It is excluded from the editor build via `<Compile Remove="RibbonBuilder\**" />` and embedded
   into the test assembly as `RibbonBuilder.cs`.)

2. **Schema** (`Schema\`):
   - `ElementKind`: add `DockablePane`.
   - `FieldKind`: add `Guid` (validated with `Guid.TryParse`; editor = text box, optional "New GUID" button) and
     decide how to represent the pane class. Recommendation: a new `FieldKind.PaneClassName` so the property panel
     and validator can treat it differently from `ClassName` (which is scanned/validated against the tab DLL's
     `IExternalCommand` list — a pane class is in the *Resources* DLL and implements `IDockablePaneProvider`, so
     reusing `ClassName` would raise a false `classname.unknown` warning and offer the wrong picker list).
   - `RibbonSchema.All`: add the `ElementDef` (XmlName `dockablepane`, DisplayName "Dockable pane", description
     from §2), attributes in this order: `name` (required, hint: "Key that commands use to find the pane
     (RibbonBuilder.TryGetDockablePane). Case-insensitive, unique per file."), `guid` (required, FieldKind.Guid),
     `title` (optional, Text, hint: "Caption shown in Revit; defaults to the name."), `classname` (required,
     PaneClassName, hint: "Full name of a public class implementing IDockablePaneProvider with a constructor taking
     UIControlledApplication or no parameters. May live in a referenced DLL such as RevitRibbon_MainSourceCode_Resources.dll."),
     `startshidden` (optional, TrueOrEmpty, label "Start hidden", hint: "Hide the pane on the first view activation;
     the ribbon button then shows it."). No children (`0, 0`).
   - `Tab` ElementDef: `AllowedChildren` becomes `{ Panel, DockablePane }`.
   - `IsPanelItem`: unchanged (a pane is not a RibbonItem).
   - Writer/reader are schema-driven (all attributes always written, "" when empty) — no code change expected, but
     add a round-trip test.

3. **Validator** (`Services\RibbonValidator.cs`):
   - `FieldKind.Guid`: error `guid.invalid` when `Guid.TryParse` fails ("Revit skips the pane"). Optional warning
     when the value is not in the canonical `D` format (upper/lower is fine).
   - `FieldKind.PaneClassName`: same `classname.format` regex warning as `ClassName`; **no** `classname.unknown`
     check against the tab-DLL command list. If step 5 adds a pane-class scan, warn `paneclass.unknown` only when a
     scan result exists.
   - Tab-level duplicates: extend the `ElementKind.Tab` case so `dockablepane` names are checked for duplicates among
     dockable panes, **case-insensitively** (parser uses `OrdinalIgnoreCase`), rule `name.duplicate`, message
     "Dockable pane name "X" is used more than once in this tab; Revit builds only the first." Keep the panel check as is.
   - `child.notallowed` already covers a `<dockablepane>` placed inside a panel (now that Tab allows it, a pane under
     a panel is still flagged). Add a test for both placements.
   - `container.empty` must not fire for a pane (MinChildren 0).

4. **Property panel** (`ViewModels\Fields\FieldViewModels.cs`, `Views\FieldTemplates.xaml`): factory cases for the
   two new kinds. `GuidField`: text + "New" button (`Guid.NewGuid().ToString("D").ToUpperInvariant()`) + inline
   validity. `PaneClassNameField`: plain text with the hint; if step 5 is done, a picker like `ClassNameField`'s.

5. **Class picker for panes (optional but recommended)** — `Services\CommandClassScanner.cs` reads metadata only
   (`System.Reflection.Metadata`, no Assembly.Load). Generalise it (or add `PaneClassScanner`) to list public
   concrete classes implementing `Autodesk.Revit.UI.IDockablePaneProvider`, and scan **both** the tab DLL and
   `RevitRibbon_MainSourceCode_Resources.dll` in the same folder (the deployed folder
   `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\<Tab>\<year>\` holds both). `EditorViewModel`
   already auto-detects the single `*_Tab.dll` in the ribbon's folder (`AutoDetectDll`); derive the Resources DLL
   path from it. Expose the result as a second known-class set in `ValidationContext` (e.g. `KnownPaneClasses`).
   Note: `IDockablePaneProvider` is an interface in `Autodesk.Revit.UI`; the existing interface-walk already handles
   TypeReference vs TypeDefinition and same-module base classes.

6. **Tree** (`ViewModels\NodeViewModel.cs`): `Glyph` → `"DP"`, `GlyphBrush` → something distinct (e.g.
   `Brushes.DarkMagenta`); check the caption logic (`Node.Kind == ElementKind.Button && ...` around line 130) shows
   `title` or `name` for a pane; `AddChildOptions` picks it up automatically from `AllowedChildren`, so "Add →
   Dockable pane" appears on the tab node. Check the switch statements near lines 227–233 and 278–312 (what counts
   as "large", what a node's parent may be) don't throw on the new kind — add a default where needed.

7. **Preview** (`Views\RibbonPreview.xaml`, `Views\PreviewTemplateSelector.cs`): the tab row binds
   `Root.Children` through the selector, so a pane node currently yields no template (renders as `ToString()`).
   Either add a `Preview.DockablePane` template (a small dashed chip "⧉ Dockable pane: {title}" at the end of the
   tab row, or in a separate strip under the panels — it is not a ribbon item, so do not draw it as a panel) or
   filter it out of the panel row and show it in a strip. Keep selection two-way like the other templates.
   `PreviewViewModelTests` may need an expectation update.

8. **Docs**:
   - `RibbonXmlEditor\RibbonBuilder\README.md` (the README other add-ins get with the drop-in): add the element to the
     format table (§"The `.ribbon` format"), a short "Dockable panes" subsection (attributes, class contract,
     `TryGetDockablePane` usage from a command, `startshidden` rationale, reuse of an existing guid), the error row in
     "What happens on errors", and a 3.1.0 changelog line.
   - Repo `README.md` (editor handbook): mention the element and the pane-class picker.
   - `Resources\ButtonStructureTemplate.ribbon` ("New from template"): optionally add a **disabled** (commented)
     `<dockablepane>` example so new files show how to declare one — only if the Disable round-trip handles a
     childless element under `<tab>` (write a test).

9. **Tests** (`RibbonXmlEditor.Tests`): schema sync passes; round-trip of a file with a pane (attribute order, raw
   newline in `title`, disabled pane as comment); validator: required attrs, bad guid, duplicate pane names
   (case-insensitive), pane inside a panel = `child.notallowed`, pane class not checked against the command list;
   scanner test for `IDockablePaneProvider` if step 5 is done (there is no Revit DLL in the test tree — follow
   `ScannerTests.cs`'s existing approach); preview VM test for the new kind.

10. **Publish** is unchanged (`.\publish.ps1`), but rebuild with `-c Release` if the user's own Debug instance of
    the editor is running (file lock).

## 4. Real-world file to test against

`C:\Visual Studio Files\BTT_ACCORevit-Ribbons\03.Engineering_Mechanical_Tab\03.Engineering_Mechanical_Tab.ribbon`
(the user adds the `<dockablepane>` above plus a button `btn_ACCODocs` →
`RevitRibbon_MainSourceCode.Unique_Button_Classes.Mechanical.Cmd_ACCODocs` by hand; it may or may not be there yet
when this session runs). Deployed copy: `C:\ACCORevit\ACCO\ACCORevit ADDINS\02-ACCORevit Ribbons\Engineering\Mechanical\2025\`
(tab DLL `03.Engineering_Mechanical_Tab.dll` + `RevitRibbon_MainSourceCode_Resources.dll`, where
`RevitRibbon_MainSourceCode_Resources.Forms.LinkLibrary_Pane` implements `IDockablePaneProvider` with a
`(UIControlledApplication)` constructor). The BTT side's README for the feature:
`BTT_ACCORevit-Ribbons\RevitRibbon_MainSourceCode\Unique Button Classes\Mechanical\ACCODocsLibrary\ACCODocsLibrary_READ_ME.md`.

## 5. Known pitfalls in this repo (from earlier sessions)

- Reader uses `XmlDocument` (keeps raw newlines in attributes); writer uses `NewLineHandling.None` +
  `NewLineOnAttributes`, UTF-8 without BOM, CRLF. Never switch to `XDocument`.
- The parser reads every attribute unconditionally, so the writer always emits every schema attribute.
- A TabControl with ItemsSource + fixed ContentTemplate keeps one live content view; per-tab UI state lives in the VM.
- `BitmapImage` with `StreamSource` + `IgnoreImageCache` throws in `EndInit` — don't reintroduce it.
- WPF projects drop `System.IO` from implicit usings; both csproj carry `<Using Include="System.IO" />`.
- UI verification recipe: run the exe, drive it with PowerShell UI Automation (`pwsh -STA`, `SelectionItemPattern`
  on TabItems, `InvokePattern` on buttons named by `AutomationProperties.Name`), screen-capture with
  `Graphics.CopyFromScreen`, then view the PNGs.

## 6. Done when

- `dotnet test RibbonXmlEditor.Tests` is green, including all five `SchemaSyncTests`.
- Opening the Mechanical `.ribbon` (with the pane declared) shows a "DP" node under the tab, the five fields with
  hints, no false warnings, a sensible preview, and Save round-trips the file unchanged.
- Adding a pane via "Add → Dockable pane" on the tab node, filling it in, and saving produces XML the parser accepts.
- README files updated; the user says "Checkpoint" before any commit.
