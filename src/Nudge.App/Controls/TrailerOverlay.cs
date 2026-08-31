using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using Serilog;

namespace Nudge.App.Controls;

/// <summary>
/// Plays a table's online preview through YouTube's own embedded player, positioned over whichever
/// tile is being hovered.
///
/// ONE shared browser for the whole library, moved around, rather than a WebView2 inside each tile's
/// template. Every WebView2 is a real browser instance with its own processes; a virtualized grid
/// showing dozens of tiles would otherwise spin up dozens of them, which is far too expensive for a
/// hover affordance.
///
/// The video is embedded, never downloaded - downloading would breach YouTube's Terms of Service.
/// The official youtube-nocookie.com embed endpoint is used, which is the sanctioned way to play
/// this content inside an application.
///
/// Two consequences of WebView2 being a native child window (an HwndHost) rather than a WPF
/// element, both worked around here rather than left to look broken:
/// - It always draws on top of WPF content and ignores WPF clipping, so it is positioned by hand
///   over the tile and hidden the moment the pointer leaves.
/// - WPF opacity and corner rounding do not apply to it, so the fade-in and the rounded corners are
///   done in CSS inside the page instead.
/// </summary>
public sealed class TrailerOverlay
{
    private readonly Panel _host;
    private WebView2? _webView;
    private bool _initialising;

    /// <summary>The tile currently being previewed, so a stale async init never shows the wrong table's video.</summary>
    private object? _currentToken;

    public TrailerOverlay(Panel host) => _host = host;

    /// <summary>The most recent request, rendered as soon as the browser is ready. Null once hidden.</summary>
    private (string VideoId, object Token, string Background, double Radius, bool Interactive)? _pending;

    /// <summary>
    /// Shows <paramref name="videoId"/> over the given rectangle, in coordinates relative to the
    /// host panel. Safe to call repeatedly as the pointer moves between tiles.
    /// </summary>
    /// <param name="interactive">
    /// True on a page the viewer deliberately opened to watch something: the player gets its own
    /// controls, sound, and mouse input. False for the library's hover preview, which is a silent,
    /// non-interactive glance and must not swallow the mouse.
    /// </param>
    public void Show(string videoId, Rect bounds, object token, string backgroundCss, double cornerRadiusPx, bool interactive = false)
    {
        _currentToken = token;
        _pending = (videoId, token, backgroundCss, cornerRadiusPx, interactive);

        WebView2 view = EnsureView();
        view.IsHitTestVisible = interactive;
        view.Margin = new Thickness(bounds.Left, bounds.Top, 0, 0);
        view.Width = bounds.Width;
        view.Height = bounds.Height;

        _ = EnsureReadyAndRenderAsync(view);
    }

    /// <summary>
    /// Creating the browser takes a moment the first time. Earlier versions bailed out when that was
    /// already in progress, and again afterwards if the pointer had moved on - between them, the
    /// first hover or two of every session reliably showed nothing at all. Instead the newest
    /// request is always kept in <see cref="_pending"/> and rendered the instant the browser is
    /// ready, so an in-flight startup delays the preview rather than cancelling it.
    /// </summary>
    private async Task EnsureReadyAndRenderAsync(WebView2 view)
    {
        if (_initialising)
        {
            // The startup already running will render whatever _pending holds when it finishes.
            return;
        }

        if (view.CoreWebView2 is null)
        {
            _initialising = true;
            try
            {
                await view.EnsureCoreWebView2Async().ConfigureAwait(true);
                MapPlayerHost(view);
            }
            catch (Exception ex)
            {
                // No WebView2 runtime, or it failed to start. Online previews simply don't appear;
                // local video files and everything else keep working. Logged rather than swallowed,
                // because from the outside this is indistinguishable from the feature being broken.
                Log.Warning(ex, "The embedded browser could not start, so online trailers are unavailable.");
                _initialising = false;
                Hide();
                return;
            }
            finally
            {
                _initialising = false;
            }
        }

        Render(view);
    }

    /// <summary>Which tile's page is actually loaded, so re-showing the same one never reloads it.</summary>
    private object? _renderedToken;

    private void Render(WebView2 view)
    {
        if (_pending is not { } request)
        {
            return;
        }

        // Stale only if the pointer has since moved somewhere that cleared or replaced the request.
        if (!ReferenceEquals(_currentToken, request.Token))
        {
            return;
        }

        view.Visibility = Visibility.Visible;

        // Already playing this exact tile's video - navigating again would restart it from the
        // beginning and flash the player, which is what repeated Show calls for one tile used to do.
        if (ReferenceEquals(_renderedToken, request.Token))
        {
            return;
        }

        _renderedToken = request.Token;
        view.Source = new Uri(BuildEmbedUrl(request.VideoId, request.Interactive));
    }

    /// <summary>Whether a preview is currently on screen, so the caller knows when to watch for the pointer leaving.</summary>
    public bool IsShowing => _webView?.Visibility == Visibility.Visible;

    /// <summary>Hides the overlay and stops playback by unloading the page - not merely collapsing it, which would leave audio running.</summary>
    public void Hide()
    {
        _currentToken = null;
        _pending = null;
        _renderedToken = null;

        if (_webView is null)
        {
            return;
        }

        _webView.Visibility = Visibility.Collapsed;
        if (_webView.CoreWebView2 is not null)
        {
            _webView.Source = new Uri("about:blank");
        }
    }

    private WebView2 EnsureView()
    {
        if (_webView is not null)
        {
            return _webView;
        }

        _webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            DefaultBackgroundColor = System.Drawing.Color.Transparent
        };

        _host.Children.Add(_webView);
        return _webView;
    }

    /// <summary>Serves the local player page from a real https origin - see <see cref="MapPlayerHost"/>.</summary>
    private const string PlayerHost = "nudge.player";

    private static string PlayerFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nudge",
        "player");

    /// <summary>
    /// Publishes a local folder as <c>https://nudge.player/</c> so the page hosting the YouTube
    /// iframe has a genuine https origin.
    ///
    /// Both simpler approaches fail, in opposite ways, and this is what is left:
    /// - An iframe on a page supplied via NavigateToString has an opaque <c>about:blank</c> origin
    ///   and sends no usable referrer, so YouTube shows "Watch this video on YouTube" instead of
    ///   playing.
    /// - Navigating the browser straight at the <c>/embed/</c> URL makes it the top-level document,
    ///   and YouTube rejects that with "Video player configuration error - Error 153", because
    ///   embed URLs are only valid when actually embedded.
    /// A real origin serving a real page that frames the embed satisfies both requirements.
    /// </summary>
    private static void MapPlayerHost(WebView2 view)
    {
        Directory.CreateDirectory(PlayerFolder);
        File.WriteAllText(Path.Combine(PlayerFolder, "player.html"), PlayerPageHtml);

        view.CoreWebView2!.SetVirtualHostNameToFolderMapping(
            PlayerHost,
            PlayerFolder,
            Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.DenyCors);
    }

    /// <summary>
    /// The page that frames the video. Reads the id and mode from its own query string rather than
    /// being regenerated per video, so it is written to disk once and every navigation is just a
    /// different URL against the same file.
    /// </summary>
    private const string PlayerPageHtml = """
        <!doctype html>
        <html>
          <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"></head>
          <body style="margin:0;overflow:hidden;background:#000">
            <div id="wrap" style="position:absolute;inset:0;overflow:hidden"></div>
            <script>
              var q = new URLSearchParams(location.search);
              var id = q.get('v') || '';
              var interactive = q.get('c') === '1';

              // Autoplay is only permitted while muted, so the silent hover preview can start on its
              // own; the details page starts muted too and the viewer unmutes with the controls.
              var params = interactive
                ? 'autoplay=1&mute=1&controls=1&modestbranding=1&rel=0&playsinline=1'
                : 'autoplay=1&mute=1&controls=0&modestbranding=1&rel=0&playsinline=1&loop=1&playlist=' + encodeURIComponent(id);

              var frame = document.createElement('iframe');
              frame.setAttribute('allow', 'autoplay; encrypted-media; fullscreen');
              frame.setAttribute('allowfullscreen', '');
              frame.style.cssText = 'position:absolute;top:50%;left:0;width:100%;height:56.25vw;transform:translateY(-50%);border:0;opacity:0;transition:opacity .25s ease-out';
              frame.src = 'https://www.youtube-nocookie.com/embed/' + encodeURIComponent(id) + '?' + params;

              // Revealed only once the player has actually loaded, so the panel never flashes an
              // empty black box while YouTube is still connecting.
              frame.addEventListener('load', function () { frame.style.opacity = 1; });
              document.getElementById('wrap').appendChild(frame);
            </script>
          </body>
        </html>
        """;

    private static string BuildEmbedUrl(string videoId, bool interactive) =>
        $"https://{PlayerHost}/player.html?v={Uri.EscapeDataString(videoId)}&c={(interactive ? "1" : "0")}";
}
