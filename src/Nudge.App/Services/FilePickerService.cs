using Microsoft.Win32;

namespace Nudge.App.Services;

/// <summary>
/// Shows the Windows file chooser, filtered to image files - used by the table customization page
/// to let the user pick a local image as a table's artwork override. Sits behind an interface for
/// the same reason <see cref="IFolderPickerService"/> does: view models never open a dialog
/// themselves.
/// </summary>
public interface IFilePickerService
{
    /// <summary>Returns the chosen image file's full path, or null when the user cancelled.</summary>
    string? PickImageFile(string title);

    /// <summary>Returns the chosen video file's full path, or null when the user cancelled.</summary>
    string? PickVideoFile(string title);
}

public sealed class FilePickerService : IFilePickerService
{
    public string? PickImageFile(string title) =>
        Pick(title, "Image files|*.png;*.jpg;*.jpeg;*.webp;*.bmp|All files|*.*");

    // The formats listed are the ones WPF's MediaElement can actually play, which is whatever the
    // machine's installed codecs support (it is a Media Foundation wrapper, not a bundled decoder) -
    // mp4/wmv/avi/mkv covers the overwhelming majority of gameplay captures in practice.
    public string? PickVideoFile(string title) =>
        Pick(title, "Video files|*.mp4;*.wmv;*.avi;*.mkv;*.mov;*.m4v|All files|*.*");

    private static string? Pick(string title, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
