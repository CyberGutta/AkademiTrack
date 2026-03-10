using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AkademiTrack;

public partial class ExportLogsDialog : Window
{
    public ExportLogsDialog()
    {
        InitializeComponent();
    }

    public class ExportLogsResult
    {
        public string Format { get; set; } = "text"; // "text", "json", "report"
        public string Destination { get; set; } = "desktop"; // "desktop", "documents", "clipboard", "custom"
        public bool Cancelled { get; set; } = false;
    }

    private void OnExportClick(object? sender, RoutedEventArgs e)
    {
        var result = new ExportLogsResult
        {
            Cancelled = false
        };

        if (JsonFormatRadio?.IsChecked ?? false)
            result.Format = "json";
        else if (ReportFormatRadio?.IsChecked ?? false)
            result.Format = "report";
        else
            result.Format = "text";

        if (DocumentsRadio?.IsChecked ?? false)
            result.Destination = "documents";
        else if (ClipboardRadio?.IsChecked ?? false)
            result.Destination = "clipboard";
        else if (CustomRadio?.IsChecked ?? false)
            result.Destination = "custom";
        else
            result.Destination = "desktop";

        Close(result);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(new ExportLogsResult { Cancelled = true });
    }
}