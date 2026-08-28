using Microsoft.Win32;

namespace Nudge.App.Services;

/// <summary>
/// Shows the Windows folder chooser.
///
/// This sits behind an interface so that view models never open a dialog themselves, which keeps
/// them free of anything that needs a running UI.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>Returns the chosen folder, or null when the user cancelled.</summary>
    string? PickFolder(string title, string? initialDirectory = null);
}

public sealed class FolderPickerService : IFolderPickerService
{
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
