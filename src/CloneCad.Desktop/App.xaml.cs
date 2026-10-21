using System.Windows;
namespace CloneCad.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloneCad", "logs");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "errors.log"), $"{DateTimeOffset.Now:O} {args.Exception.GetType().Name}: {args.Exception.Message}\n");
            MessageBox.Show("작업을 완료하지 못했습니다. 도면을 저장한 뒤 다시 시도하세요.\n" + args.Exception.Message, "Clone by Codex");
            args.Handled = true;
        };
        var smoke = e.Args.Length == 2 && e.Args[0] == "--smoke-test";
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
