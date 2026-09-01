using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections;
using Microsoft.Extensions.Logging.Abstractions;
using Nudge.App.Controls;
using Nudge.App.ViewModels;
using Nudge.App.Views;
using Nudge.Core.Models;

// Headless UI harness. Run it with:  dotnet run --project tools/Nudge.UiHarness
//
// None of these is something a clean build proves anything about, and every one exists because the
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
//
//   4. Every focusable control shows a focus ring. Seven settings switches had no focus visual at
//      all, so the pad could sit on one with nothing on screen saying so.
//
//   5. An open dropdown moves, clamps and closes - it could be opened but not used.
//
//   6. Every tile template carries a selection ring. The List layout's rows are their own template
//      and never got one, so its selection moved invisibly.
//
//   7. No button renders with WPF's default black foreground, which is what a style gets by simply
//      not mentioning Foreground - and stays black on a dark palette.
//
//   8. The play-history phrasing, at boundaries nobody sees until they have played a table for
//      exactly that long.

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        // Without this the default OnLastWindowClose applies: the first check closes its host window,
        // that is the last window, and the Application shuts down - so every later Show() silently
        // builds no visual tree and every subsequent check passes over an empty page.
        Application app = new() { ShutdownMode = ShutdownMode.OnExplicitShutdown };

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
        failed += CheckEveryStopShowsFocus();
        failed += CheckOpenDropDownNavigation();
        failed += CheckEveryLayoutHasASelectionRing();
        failed += CheckNoButtonRendersBlack();
        failed += CheckPlayHistoryFormatting();
        failed += CheckPlayHistorySorting();
        failed += CheckFocusRingsContrastWithTheirButtons();
        failed += CheckPlayHistoryIsOnTheDetailsPage();

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
    /// The play-history phrasing, at every boundary that reads wrong if it is off a little - and
    /// none of which is visible until someone happens to have played a table for exactly that long.
    /// </summary>
    private static int CheckPlayHistoryFormatting()
    {
        DateTime today = new(2026, 8, 31);

        (string Actual, string Expected, string What)[] cases =
        [
            (PlayHistoryFormat.Duration(0), PlayHistoryFormat.NoData, "no time at all"),
            (PlayHistoryFormat.Duration(59), "Under a minute", "under a minute"),
            (PlayHistoryFormat.Duration(60), "1 min", "exactly one minute"),
            (PlayHistoryFormat.Duration(3599), "59 min", "one second short of an hour"),
            (PlayHistoryFormat.Duration(3600), "1 hr", "exactly an hour - no trailing 0 min"),
            (PlayHistoryFormat.Duration(3660), "1 hr 1 min", "an hour and a minute"),
            (PlayHistoryFormat.Duration(12345), "3 hr 25 min", "several hours"),

            (PlayHistoryFormat.TimesPlayed(0), "Never", "never played"),
            (PlayHistoryFormat.TimesPlayed(1), "Once", "played once"),
            (PlayHistoryFormat.TimesPlayed(2), "Twice", "played twice"),
            (PlayHistoryFormat.TimesPlayed(3), "3 times", "played several times"),

            // Built at the local offset, not UTC: When() reads LocalDateTime, so a UTC fixture would
            // be checking the machine's time zone rather than the formatting.
            (PlayHistoryFormat.When(Local(today.AddHours(21)), today),
                "Today, 21:00", "earlier today"),
            (PlayHistoryFormat.When(Local(today.AddDays(-1).AddHours(23)), today),
                "Yesterday, 23:00", "late yesterday, not '2 hours ago'"),
            (PlayHistoryFormat.When(Local(today.AddDays(-3)), today),
                "3 days ago", "a few days ago"),
            (PlayHistoryFormat.When(Local(today.AddDays(-9)), today),
                "Last week", "last week"),
            (PlayHistoryFormat.When(Local(new DateTime(2026, 3, 4)), today),
                "4 March 2026", "long enough ago to want a date")
        ];

        static DateTimeOffset Local(DateTime at) => new(at, TimeZoneInfo.Local.GetUtcOffset(at));

        int failed = 0;
        foreach ((string actual, string expected, string what) in cases)
        {
            if (actual != expected)
            {
                failed++;
                Console.WriteLine($"FAIL  play history ({what}): got \"{actual}\", expected \"{expected}\"");
            }
        }

        if (failed == 0)
        {
            Console.WriteLine($"Play history: all {cases.Length} phrasings correct");
        }

        return failed;
    }

    /// <summary>
    /// The play-history block has to be ON the details page, VISIBLE, and visible with no data.
    ///
    /// All three failed at once in the first attempt: it was bound to a "has been played" flag, so a
    /// table you had never played showed nothing at all - which is indistinguishable from the feature
    /// not working, and is exactly how it was reported. Checked with no DataContext, which is the
    /// never-played case as far as the layout is concerned.
    /// </summary>
    private static int CheckPlayHistoryIsOnTheDetailsPage()
    {
        TableDetailsView view = new();
        Window host = new() { Width = 1920, Height = 1080, Content = view };
        host.Show();
        host.UpdateLayout();

        string[] required = ["PLAY HISTORY", "PLAYED", "TOTAL TIME", "LAST PLAYED"];
        List<string> missing = [];

        foreach (string label in required)
        {
            if (!HasVisibleText(host, label))
            {
                missing.Add(label);
            }
        }

        host.Close();

        if (missing.Count > 0)
        {
            Console.WriteLine($"FAIL  details page: play history not visible - missing {string.Join(", ", missing)}");
            return 1;
        }

        Console.WriteLine("Play history: visible on the details page with no data at all");
        return 0;

        static bool HasVisibleText(DependencyObject node, string text)
        {
            if (node is TextBlock { IsVisible: true } block
                && string.Equals(block.Text, text, StringComparison.Ordinal))
            {
                return true;
            }

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                if (HasVisibleText(VisualTreeHelper.GetChild(node, i), text))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// A focus ring has to be a different colour from the thing it is drawn on. Brush.Focus is
    /// literally the same colour as Brush.Accent in every palette, so on an accent-filled button -
    /// Play, on the details page - the ring was invisible and there was no way to tell it was
    /// selected. Checked in every palette, since a theme could pair them badly on its own.
    /// </summary>
    private static int CheckFocusRingsContrastWithTheirButtons()
    {
        Application app = Application.Current;
        string root = AppContext.BaseDirectory;
        while (!Directory.Exists(Path.Combine(root, "src", "Nudge.App", "Themes")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        string[] themes = Directory
            .GetFiles(Path.Combine(root, "src", "Nudge.App", "Themes"), "Colors.*.xaml")
            .Select(Path.GetFileName).OfType<string>().Order().ToArray();

        // One of each button style, on the styles that actually fill themselves with a colour.
        (string Style, string Label)[] styles =
        [
            ("Button.Primary", "primary"),
            ("Button.Secondary", "secondary"),
            ("Button.Subtle", "subtle"),
            ("Button.Icon", "icon")
        ];

        int failed = 0;
        List<string> clashes = [];

        foreach (string theme in themes)
        {
            app.Resources.MergedDictionaries[0] = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Nudge;component/Themes/{theme}")
            };

            foreach ((string styleKey, string label) in styles)
            {
                Button button = new()
                {
                    Style = (Style)app.Resources[styleKey],
                    Content = "X"
                };

                Window host = new() { Width = 300, Height = 200, Content = button };
                host.Show();
                host.UpdateLayout();
                button.Focus();
                host.UpdateLayout();

                Color? ring = SolidColorOf(button.BorderBrush);
                Color[] face = ColorsOf(button.Background);

                if (ring is { } ringColor && face.Any(f => Close(f, ringColor)))
                {
                    clashes.Add($"{theme}/{label} (ring {ringColor}, face {string.Join('/', face)})");
                }

                host.Close();
            }
        }

        if (clashes.Count > 0)
        {
            failed++;
            Console.WriteLine($"FAIL  focus ring invisible against its own button in {clashes.Count} case(s): " +
                              string.Join(", ", clashes.Take(6)) + (clashes.Count > 6 ? ", ..." : ""));
        }
        else
        {
            Console.WriteLine($"Focus contrast: {styles.Length} button styles x {themes.Length} themes - every ring differs from its button");
        }

        return failed;

        static Color? SolidColorOf(Brush? brush) => brush is SolidColorBrush solid ? solid.Color : null;

        // Transparent stops are dropped: WPF's Transparent is #00FFFFFF - white with zero alpha - so
        // comparing it by RGB says "this button is white" about a button you can see straight
        // through. A see-through button has no face of its own; its ring is drawn over the page
        // behind it, which is a different comparison and not this one.
        static Color[] ColorsOf(Brush? brush) => brush switch
        {
            SolidColorBrush solid => solid.Color.A == 0 ? [] : [solid.Color],
            GradientBrush gradient => gradient.GradientStops
                .Select(s => s.Color).Where(c => c.A != 0).ToArray(),
            _ => []
        };

        // Not exact equality: two colours a few units apart are the same colour to look at, and that
        // is the thing being checked.
        static bool Close(Color a, Color b) =>
            Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 24;
    }

    /// <summary>
    /// The play-history orderings. The interesting part is not "more comes first" - it is that
    /// never-played tables sink to the bottom in title order rather than sitting wherever the scan
    /// left them, and that ties break the same way every time.
    /// </summary>
    private static int CheckPlayHistorySorting()
    {
        Dictionary<string, TablePlayStats> stats = new(StringComparer.OrdinalIgnoreCase)
        {
            [@"C:\a.vpx"] = new() { TimesPlayed = 2, TotalPlaySeconds = 100, LastPlayedAt = When(1) },
            [@"C:\b.vpx"] = new() { TimesPlayed = 9, TotalPlaySeconds = 900, LastPlayedAt = When(30) },
            [@"C:\c.vpx"] = new() { TimesPlayed = 2, TotalPlaySeconds = 500, LastPlayedAt = When(10) }

            // d.vpx and e.vpx have no entry at all - never played.
        };

        TableTileViewModel[] tiles =
        [
            Tile(@"C:\e.vpx", "Empire"),
            Tile(@"C:\a.vpx", "Attack"),
            Tile(@"C:\d.vpx", "Dracula"),
            Tile(@"C:\c.vpx", "Cyclone"),
            Tile(@"C:\b.vpx", "Banzai")
        ];

        int failed = 0;

        // Most played: 9, then the two on 2 split by total time (500 beats 100), then the unplayed
        // pair alphabetically.
        failed += CheckOrder("most played", new TablePlayComparer(Lookup, byRecency: false),
            ["Banzai", "Cyclone", "Attack", "Dracula", "Empire"]);

        // Recently played: newest timestamp first, unplayed last and alphabetical.
        failed += CheckOrder("recently played", new TablePlayComparer(Lookup, byRecency: true),
            ["Attack", "Cyclone", "Banzai", "Dracula", "Empire"]);

        return failed;

        int CheckOrder(string what, IComparer comparer, string[] expected)
        {
            List<TableTileViewModel> sorted = [.. tiles];
            sorted.Sort((l, r) => comparer.Compare(l, r));

            string[] actual = sorted.Select(t => t.DisplayTitle).ToArray();
            if (actual.SequenceEqual(expected))
            {
                Console.WriteLine($"Play sort ({what}): {string.Join(" > ", actual)}");
                return 0;
            }

            Console.WriteLine($"FAIL  play sort ({what}): got {string.Join(" > ", actual)}, " +
                              $"expected {string.Join(" > ", expected)}");
            return 1;
        }

        TablePlayStats? Lookup(string path) => stats.GetValueOrDefault(path);

        static DateTimeOffset When(int daysAgo) => DateTimeOffset.Now.AddDays(-daysAgo);

        static TableTileViewModel Tile(string path, string title) => new(
            new VpxTableFile
            {
                Path = path,
                FileName = Path.GetFileName(path),
                FileSizeBytes = 1,
                TableInfo = new TableInfoMetadata(),
                FilenameHints = new FilenameHints(),
                DisplayTitle = title,
                Confidence = Confidence.High,
                Evidence = []
            },
            artworkProvider: null!,
            logger: NullLogger.Instance);
    }

    /// <summary>
    /// Every layout must be able to show the controller selection. The pad drives the library by an
    /// explicit selection index rather than by focus, so the ring IS the cursor - a layout whose card
    /// has no ring lets the selection move invisibly. The List layout was exactly that: its rows are
    /// a template of their own rather than the shared card, and it never got one.
    ///
    /// Checked against the templates directly rather than by rendering each layout, since only one
    /// layout is ever visible at a time.
    /// </summary>
    private static int CheckEveryLayoutHasASelectionRing()
    {
        LibraryView view = new();
        Window host = new() { Width = 1920, Height = 1080, Content = view };
        host.Show();
        host.UpdateLayout();

        // Grid, Compact and Carousel all instantiate the shared TableCard; List has its own row.
        string[] templateKeys = ["TableTileTemplate", "TableTileCompactTemplate", "TableListRowTemplate"];

        int failed = 0;
        foreach (string key in templateKeys)
        {
            if (view.TryFindResource(key) is not DataTemplate template)
            {
                failed++;
                Console.WriteLine($"FAIL  selection ring: template {key} not found");
                continue;
            }

            ContentPresenter probe = new()
            {
                ContentTemplate = template,
                Content = null
            };

            Window probeHost = new() { Width = 600, Height = 600, Content = probe };
            probeHost.Show();
            probeHost.UpdateLayout();

            if (!HasNamed(probe, "SelectionRing"))
            {
                failed++;
                Console.WriteLine($"FAIL  selection ring: {key} has no SelectionRing");
            }

            probeHost.Close();
        }

        if (failed == 0)
        {
            Console.WriteLine($"Selection ring: all {templateKeys.Length} tile templates carry one");
        }

        host.Close();
        return failed;

        static bool HasNamed(DependencyObject node, string name)
        {
            if (node is FrameworkElement element && element.Name == name)
            {
                return true;
            }

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                if (HasNamed(VisualTreeHelper.GetChild(node, i), name))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// No button may render with WPF's default foreground. It is black, it stays black on a dark
    /// palette, and it is what a style gets by simply not mentioning Foreground - which is how the
    /// Rebind and "Reset all to defaults" buttons ended up unreadable.
    /// </summary>
    private static int CheckNoButtonRendersBlack()
    {
        int failed = 0;

        foreach (UserControl page in new UserControl[] { new SettingsView(), new LibraryView() })
        {
            Window host = new() { Width = 1920, Height = 1080, Content = page };
            host.Show();
            host.UpdateLayout();

            List<Button> black = [];
            Collect(host);

            if (black.Count > 0)
            {
                failed++;
                Console.WriteLine($"FAIL  {page.GetType().Name}: {black.Count} button(s) with a black foreground: " +
                                  string.Join(", ", black.Select(b => b.Content?.ToString() ?? "<no content>").Distinct()));
            }
            else
            {
                Console.WriteLine($"Button foreground: {page.GetType().Name} - no button renders black");
            }

            host.Close();

            void Collect(DependencyObject node)
            {
                if (node is Button button
                    && button.Foreground is SolidColorBrush { Color.R: 0, Color.G: 0, Color.B: 0, Color.A: 255 })
                {
                    black.Add(button);
                }

                int count = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < count; i++)
                {
                    Collect(VisualTreeHelper.GetChild(node, i));
                }
            }
        }

        return failed;
    }

    /// <summary>
    /// An open dropdown must be navigable. Both the header and the form pages route directions into
    /// the open list through the same helper - before that, opening the sort list and pressing down
    /// moved the page behind it instead, so the list could be opened but never used to choose.
    /// </summary>
    private static int CheckOpenDropDownNavigation()
    {
        ComboBox box = new() { ItemsSource = new[] { "one", "two", "three" }, SelectedIndex = 0 };
        Window host = new() { Width = 400, Height = 200, Content = box };
        host.Show();
        host.UpdateLayout();

        box.IsDropDownOpen = true;

        int failed = 0;

        FormControllerNavigation.ApplyToOpenDropDown(box, ControllerAction.Down);
        FormControllerNavigation.ApplyToOpenDropDown(box, ControllerAction.Down);
        if (box.SelectedIndex != 2)
        {
            failed++;
            Console.WriteLine($"FAIL  open dropdown: two downs left SelectedIndex at {box.SelectedIndex}, expected 2");
        }

        FormControllerNavigation.ApplyToOpenDropDown(box, ControllerAction.Up);
        if (box.SelectedIndex != 1)
        {
            failed++;
            Console.WriteLine($"FAIL  open dropdown: up left SelectedIndex at {box.SelectedIndex}, expected 1");
        }

        // Past the end must clamp, not wrap - running off a list of options and reappearing at the
        // top is how you pick the wrong one without noticing.
        FormControllerNavigation.ApplyToOpenDropDown(box, ControllerAction.Down);
        FormControllerNavigation.ApplyToOpenDropDown(box, ControllerAction.Down);
        FormControllerNavigation.ApplyToOpenDropDown(box, ControllerAction.Down);
        if (box.SelectedIndex != 2)
        {
            failed++;
            Console.WriteLine($"FAIL  open dropdown: ran past the end to {box.SelectedIndex}, expected to clamp at 2");
        }

        FormControllerNavigation.ApplyToOpenDropDown(box, ControllerAction.Activate);
        if (box.IsDropDownOpen)
        {
            failed++;
            Console.WriteLine("FAIL  open dropdown: A did not close the list");
        }

        if (failed == 0)
        {
            Console.WriteLine("Open dropdown: up/down move the selection, clamped at both ends, A closes");
        }

        host.Close();
        return failed;
    }

    /// <summary>
    /// Every control a controller can land on must be able to say so. A focusable control whose
    /// template has no focus ring leaves the pad sitting on something with nothing on screen to show
    /// it - "invisible, but it's over a button". The settings switches were exactly this: seven of
    /// them, none with any focus visual at all.
    ///
    /// The rule checked is structural - a focusable control's template must contain an element named
    /// FocusRing - which is what makes it enforceable rather than a matter of remembering.
    /// </summary>
    private static int CheckEveryStopShowsFocus()
    {
        int failed = 0;

        foreach (UserControl page in new UserControl[] { new SettingsView(), new LibraryView() })
        {
            Window host = new() { Width = 1920, Height = 1080, Content = page };
            host.Show();
            host.UpdateLayout();

            List<UIElement> stops = [];
            Collect(host);

            List<string> missing = stops
                .Where(s => !HasFocusRing(s))
                .Select(s => s.GetType().Name)
                .ToList();

            if (missing.Count > 0)
            {
                failed++;
                Console.WriteLine($"FAIL  {page.GetType().Name}: {missing.Count} focusable control(s) " +
                                  $"with no focus ring: {string.Join(", ", missing.Distinct())}");
            }
            else
            {
                Console.WriteLine($"Focus visuals: {page.GetType().Name} - all {stops.Count} stops show a focus ring");
            }

            host.Close();

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

        return failed;

        static bool HasFocusRing(DependencyObject node)
        {
            if (node is FrameworkElement { Name: "FocusRing" })
            {
                return true;
            }

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                if (HasFocusRing(VisualTreeHelper.GetChild(node, i)))
                {
                    return true;
                }
            }

            return false;
        }
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
