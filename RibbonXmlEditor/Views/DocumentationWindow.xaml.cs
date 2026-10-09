using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace RibbonXmlEditor.Views;

/// <summary>
/// The built-in manual: a topic list on the left and a FlowDocument on the right. Each top-level
/// <see cref="Section"/> of the document is one topic; its <c>Tag</c> is the title shown in the list.
/// Non-modal and single-instance (see <see cref="ShowOrActivate"/>).
/// </summary>
public partial class DocumentationWindow : Window
{
    private static DocumentationWindow? _instance;
    private bool _syncingSelection;

    public DocumentationWindow()
    {
        InitializeComponent();
        Topics.ItemsSource = Sections.Select(s => new Topic((string)s.Tag, s)).ToList();
        Viewer.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Viewer_ScrollChanged));
        Loaded += (_, _) => { if (Topics.Items.Count > 0 && Topics.SelectedIndex < 0) Topics.SelectedIndex = 0; };
    }

    /// <summary>The topics, in document order.</summary>
    public IReadOnlyList<Section> Sections => Doc.Blocks.OfType<Section>().Where(s => s.Tag is string).ToList();

    public IReadOnlyList<string> TopicTitles => Sections.Select(s => (string)s.Tag).ToList();

    /// <summary>The entries of the topic list, in display order.</summary>
    public IReadOnlyList<Topic> TopicList => Topics.Items.OfType<Topic>().ToList();

    /// <summary>Opens the manual, or brings the open one to the front. Optionally jumps to a topic.</summary>
    public static void ShowOrActivate(Window? owner, string? topic = null)
    {
        if (_instance is null)
        {
            _instance = new DocumentationWindow { Owner = owner };
            _instance.Closed += (_, _) => _instance = null;
            _instance.Show();
        }
        else
        {
            if (_instance.WindowState == WindowState.Minimized)
                _instance.WindowState = WindowState.Normal;
            _instance.Activate();
        }
        if (topic is not null)
            _instance.GoTo(topic);
    }

    /// <summary>Scrolls to the topic with this title (case-insensitive). Returns false when there is none.</summary>
    public bool GoTo(string title)
    {
        var t = Topics.Items.OfType<Topic>().FirstOrDefault(x => string.Equals(x.Title, title, StringComparison.OrdinalIgnoreCase));
        if (t is null)
            return false;
        Topics.SelectedItem = t;
        return true;
    }

    private void Topics_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || Topics.SelectedItem is not Topic t)
            return;
        // Sections are not UIElements: bring their first block into view, which scrolls the viewer.
        t.Section.Blocks.FirstBlock?.BringIntoView();
    }

    /// <summary>Keeps the topic list in step with manual scrolling: the topmost visible section is highlighted.</summary>
    private void Viewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Topics.Items.Count == 0)
            return;
        Topic? current = null;
        foreach (var t in Topics.Items.OfType<Topic>())
        {
            var first = t.Section.Blocks.FirstBlock;
            if (first is null)
                continue;
            var rect = first.ContentStart.GetCharacterRect(LogicalDirection.Forward);
            if (rect.Top <= 8)
                current = t;
            else
                break;
        }
        current ??= Topics.Items.OfType<Topic>().First();
        if (ReferenceEquals(Topics.SelectedItem, current))
            return;
        _syncingSelection = true;
        try { Topics.SelectedItem = current; }
        finally { _syncingSelection = false; }
    }

    private void Close_Executed(object sender, ExecutedRoutedEventArgs e) => Close();

    public sealed record Topic(string Title, Section Section)
    {
        public override string ToString() => Title;
    }
}
