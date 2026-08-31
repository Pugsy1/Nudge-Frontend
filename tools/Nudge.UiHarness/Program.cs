using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nudge.App.Views;

// Headless UI harness. Run it with:  dotnet run --project tools/Nudge.UiHarness
//
// Three checks, none of which a clean build proves anything about. Each one exists because the
// corresponding bug shipped at least once.
//
//   1. Every view constructs, in every palette. StaticResource misses throw at XAML *parse* time,
//      so a key that exists in one theme and not another only fails when that theme is live. This
//      is what the details page's ~40 runtime errors were.
//
//   2. The header exposes exactly the controller stops it should. A control marked
//      Focusable="False" (the density slider was) is skipped entirely by focus traversal, and a
//      composite control whose children are each focusable (the 2D/VR switch was) becomes several
//      stops instead of one. Neither is visible to the compiler.
//
//   3. Focus moves nothing. Any focus visual that changes a control's SIZE - a BorderThickness
//      trigger being the usual culprit - shoves everything after it along the header. Measuring
//      positions before and after is the only way to catch that, since it looks like a rendering
//      quirk rather than a layout one.

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Application app = new();
        foreach (string src in new[]
                 {
                     "Colors.Dark.xaml", "Typography.xaml", "Layout.xaml", "Effects.xaml",
                     "Style.Pin.xaml", "Controls.xaml"
                 })
        {
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Nudge;component/Themes/{src}")
            });
        }

        int failed = 0;
        failed += LoadEveryViewInEveryTheme(app);
        failed += CheckHeaderStops();

        Console.WriteLine();
        Console.WriteLine(failed == 0 ? "ALL CHECKS PASSED" : $"{failed} CHECK(S) FAILED");
        return failed == 0 ? 0 : 1;
    }

    private static int LoadEveryViewInEveryTheme(Application app)
    {
        Assembly asm = typeof(LibraryView).Assembly;

        Type[] views = asm.GetTypes()
            .Where(t => t.Namespace == "Nudge.App.Views"
                        && !t.IsAbstract
                        && typeof(UserControl).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.Name)
            .ToArray();

        string root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "src", "Nudge.App", "Themes")))
        {
            root = Path.GetDirectoryName(root)
                   ?? throw new DirectoryNotFoundException("Could not find the Themes folder above the harness output.");
        }

        string[] themes = Directory
            .GetFiles(Path.Combine(root, "src", "Nudge.App", "Themes"), "Colors.*.xaml")
            .Select(Path.GetFileName).OfType<string>().Order().ToArray();

        int checks = 0, failed = 0;

        foreach (string theme in themes)
        {
            // Swap the palette the way ThemeService does: replace entry 0 in the merged list.
            app.Resources.MergedDictionaries[0] = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Nudge;component/Themes/{theme}")
            };

            foreach (Type view in views)
            {
                checks++;
                try
                {
                    Activator.CreateInstance(view);
                }
                catch (Exception ex)
                {
                    failed++;
                    Exception cause = ex;
                    while (cause.InnerException is not null)
                    {
                        cause = cause.InnerException;
                    }

                    Console.WriteLine($"FAIL  {theme}  {view.Name}: {cause.Message}");
                }
            }
        }

        Console.WriteLine($"View load: {views.Length} views x {themes.Length} themes = {checks} checks, {failed} failed");
        return failed;
    }

    /// <summary>
    /// Lays the library out for real, then walks the header exactly as LibraryView does, and checks
    /// the result against what the header is supposed to offer a controller.
    /// </summary>
    private static int CheckHeaderStops()
    {
        LibraryView view = new();
        Window host = new() { Width = 1920, Height = 1080, Content = view };
        host.Show();
        host.UpdateLayout();

        StackPanel header = (StackPanel)view.FindName("HeaderBar")!;

        List<UIElement> stops = [];
        Collect(header);

        // The order is left to right across the header, and each entry is ONE stop - the 2D/VR
        // switch in particular has two buttons inside it that must not be stops of their own.
        string[] expected =
        [
            "Button",   // the 2D/VR switch, as a whole
            "TextBox",  // search
            "ComboBox", // sort
            "ComboBox", // layout
            "Slider",   // tiles per row
            "Button",   // pick a table for me
            "Button",   // rescan
            "Button"    // settings
        ];

        string[] actual = stops.Select(s => s.GetType().Name).ToArray();

        Console.WriteLine($"Header stops: {string.Join(", ", actual)}");

        int failed = 0;
        if (!actual.SequenceEqual(expected))
        {
            failed++;
            Console.WriteLine($"FAIL  header stops: expected {string.Join(", ", expected)}");
        }

        // Every stop must actually take focus when asked. Focusable being true is not the same
        // thing - a control inside a collapsed or disabled parent silently refuses.
        foreach (UIElement stop in stops)
        {
            if (!stop.Focus())
            {
                failed++;
                Console.WriteLine($"FAIL  header stop {stop.GetType().Name} refused focus");
            }
        }

        // Nothing may move when focus lands. This is the "search shifts toward settings and slightly
        // down" bug, and it is a layout question, not a visual one - so measure it. Focus each stop
        // in turn and check that every control in the header is still exactly where it started.
        Point[] resting = stops.Select(s => s.TranslatePoint(new Point(0, 0), host)).ToArray();

        foreach (UIElement stop in stops)
        {
            stop.Focus();
            host.UpdateLayout();

            for (int i = 0; i < stops.Count; i++)
            {
                Point now = stops[i].TranslatePoint(new Point(0, 0), host);
                if (Math.Abs(now.X - resting[i].X) > 0.01 || Math.Abs(now.Y - resting[i].Y) > 0.01)
                {
                    failed++;
                    Console.WriteLine(
                        $"FAIL  focusing {stop.GetType().Name} moved {stops[i].GetType().Name} " +
                        $"from {resting[i]} to {now}");
                }
            }
        }

        Console.WriteLine($"Layout stability: {stops.Count} stops focused, no control moved (tolerance 0.01px)");

        host.Close();
        return failed;

        void Collect(DependencyObject node)
        {
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(node, i);
                if (child is UIElement { Focusable: true, IsEnabled: true, IsVisible: true } stop)
                {
                    stops.Add(stop);
                    continue;
                }

                Collect(child);
            }
        }
    }
}
