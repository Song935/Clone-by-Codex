using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CloneCad.Desktop;

internal static class Dialogs
{
    public static bool PrintPreview(Window owner, byte[] png)
    {
        var image = new System.Windows.Media.Imaging.BitmapImage(); image.BeginInit(); image.StreamSource = new System.IO.MemoryStream(png); image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; image.EndInit(); image.Freeze();
        var window = new Window { Title = "PDF 출력 미리보기", Owner = owner, Width = 1000, Height = 760, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new DockPanel { Margin = new Thickness(12) }; var button = new Button { Content = "이 설정으로 PDF 저장", IsDefault = true };
        button.Click += (_, _) => window.DialogResult = true; DockPanel.SetDock(button, Dock.Bottom); panel.Children.Add(button);
        panel.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform }); window.Content = panel; return window.ShowDialog() == true;
    }
    public static Dictionary<string, string>? Form(Window owner, string title, params (string Name, string Value)[] fields)
    {
        var window = new Window { Title = title, Owner = owner, Width = 440, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize };
        var panel = new StackPanel { Margin = new Thickness(20) }; var inputs = new Dictionary<string, TextBox>();
        foreach (var field in fields)
        {
            panel.Children.Add(new TextBlock { Text = field.Name, Margin = new Thickness(0, 8, 0, 4) });
            var box = new TextBox { Text = field.Value }; panel.Children.Add(box); inputs.Add(field.Name, box);
        }
        var button = new Button { Content = "확인", IsDefault = true, Margin = new Thickness(0, 18, 0, 4) };
        button.Click += (_, _) => window.DialogResult = true; panel.Children.Add(button); window.Content = panel;
        return window.ShowDialog() == true ? inputs.ToDictionary(x => x.Key, x => x.Value.Text) : null;
    }
}
