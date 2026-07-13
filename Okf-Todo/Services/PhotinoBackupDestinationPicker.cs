using Photino.NET;

namespace Photino.Okf_Todo.Services;

public interface IBackupDestinationPicker
{
    Task<string?> PickAsync(string suggestedFileName, CancellationToken cancellationToken);
}

public sealed class PhotinoBackupDestinationPicker : IBackupDestinationPicker
{
    private PhotinoWindow? window;

    public void Attach(PhotinoWindow photinoWindow)
    {
        window = photinoWindow;
    }

    public async Task<string?> PickAsync(string suggestedFileName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var activeWindow = window
            ?? throw new InvalidOperationException("The native file dialog is not ready.");
        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var selectedPath = await activeWindow.ShowSaveFileAsync(
            $"Back up OKF Todo database as {suggestedFileName}",
            documentsPath,
            [("SQLite database", ["db"])]);

        cancellationToken.ThrowIfCancellationRequested();
        return string.IsNullOrWhiteSpace(selectedPath) ? null : selectedPath;
    }
}
