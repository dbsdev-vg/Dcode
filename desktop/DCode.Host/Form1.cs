using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Text.Json.Serialization;

namespace DCode.Host;

public partial class Form1 : Form
{
    private readonly WebView2 _webView;

    public Form1()
    {
        InitializeComponent();

        Text = "DCode";
        Width = 1400;
        Height = 900;
        StartPosition = FormStartPosition.CenterScreen;

        _webView = new WebView2
        {
            Dock = DockStyle.Fill
        };

        Controls.Add(_webView);

        Load += Form1_Load;
    }

    private async void Form1_Load(object? sender, EventArgs e)
    {
        await _webView.EnsureCoreWebView2Async();

        _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;

        _webView.CoreWebView2.WebMessageReceived += WebMessageReceived;

        _webView.Source = new Uri("http://localhost:3000");
    }

    private void WebMessageReceived(
    object? sender,
    CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.WebMessageAsJson;

            Console.WriteLine($"WebView message: {json}");

            var message = JsonSerializer.Deserialize<HostMessage>(json);

            Console.WriteLine($"Message type: {message?.Type}");

            if (message is null)
            {
                return;
            }

            switch (message.Type)
            {
                case "open-project":
                    Console.WriteLine("Opening project picker...");
                    OpenProject();
                    break;

                case "activate-browser-window":
                    _ = ActivateEdgeWindowAsync();
                    break;

                default:
                    Console.WriteLine($"Unknown message: {message.Type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);

            SendMessage(new
            {
                type = "host-error",
                message = ex.Message
            });
        }
    }

    private async Task ActivateEdgeWindowAsync()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var edgeWindow = Process.GetProcessesByName("msedge")
                .Where(process => process.MainWindowHandle != IntPtr.Zero)
                .OrderByDescending(GetStartTimeOrMinimum)
                .FirstOrDefault();

            if (edgeWindow is not null)
            {
                var window = edgeWindow.MainWindowHandle;
                ShowWindow(window, ShowWindowRestore);
                BringWindowToTop(window);
                SetForegroundWindow(window);
                return;
            }

            await Task.Delay(250);
        }

        SendMessage(new
        {
            type = "host-error",
            message = "The Edge login window could not be brought to the foreground."
        });
    }

    private static DateTime GetStartTimeOrMinimum(Process process)
    {
        try { return process.StartTime; }
        catch { return DateTime.MinValue; }
    }

    private const int ShowWindowRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    private void OpenProject()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a DCode project",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        var result = dialog.ShowDialog(this);

        if (result != DialogResult.OK)
        {
            SendMessage(new
            {
                type = "open-project-cancelled"
            });

            return;
        }

        SendMessage(new
        {
            type = "project-selected",
            path = dialog.SelectedPath
        });
    }

    private void SendMessage(object message)
    {
        var json = JsonSerializer.Serialize(message);

        _webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private sealed class HostMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "";
    }
}
