using System.Windows;
namespace CloneCad.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            ShowFailure(args.Exception, false);
            args.Handled = true;
        };
        var smoke = e.Args.Length == 2 && e.Args[0] == "--smoke-test";
        try { OpenMainWindow(e, smoke); }
        catch (Exception ex)
        {
            if (smoke)
            {
                System.IO.Directory.CreateDirectory(e.Args[1]);
                System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1], "smoke-error.txt"), ex.ToString());
            }
            else ShowFailure(ex, true);
            Shutdown(1);
        }
    }
    private static void ShowFailure(Exception exception, bool startup)
    {
        var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloneCad", "logs", "errors.log");
        string logNotice;
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var version = typeof(App).Assembly.GetName().Version;
            System.IO.File.AppendAllText(path, $"{DateTimeOffset.Now:O} CloneCad {version} startup={startup}\n{exception}\n\n");
            logNotice = "오류 기록: " + path;
        }
        catch (Exception logError) when (logError is System.IO.IOException or UnauthorizedAccessException)
        { logNotice = "오류 기록을 저장할 수 없습니다: " + logError.Message; }
        MessageBox.Show((startup ? "CAD를 시작하지 못했습니다." : "작업을 완료하지 못했습니다. 도면을 저장한 뒤 다시 시도하세요.") +
            "\n\n" + exception.Message + "\n\n" + logNotice, "Clone by Codex", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    private void OpenMainWindow(StartupEventArgs e, bool smoke)
    {
        var main = new MainWindow(smoke);
        if (smoke)
        {
            main.Left = -20000; main.Top = -20000; main.ShowInTaskbar = false;
            main.Loaded += async (_, _) =>
            {
                try { await main.RunSmokeTest(e.Args[1]); main.Close(); Shutdown(0); }
                catch (Exception ex)
                {
                    System.IO.Directory.CreateDirectory(e.Args[1]); System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[1], "smoke-error.txt"), ex.ToString());
                    main.Close(); Shutdown(1);
                }
            };
        }
        main.Show();
    }
}
