using System.Text.Json;

namespace MUClientStudio.Core.Workspace;

public sealed class ClientWorkspaceService
{
    private readonly string _stateFile;

    public ClientWorkspaceService()
    {
        var stateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MUClientStudio");
        Directory.CreateDirectory(stateDir);
        _stateFile = Path.Combine(stateDir, "workspace.json");
    }

    public ClientWorkspace Open(string selectedRoot)
    {
        var (full, dataRoot) = ValidateAndResolve(selectedRoot);
        var fileCount = CountFiles(dataRoot, CancellationToken.None);
        var workspace = new ClientWorkspace(full, dataRoot, fileCount, DateTime.UtcNow);
        Save(workspace);
        return workspace;
    }

    public async Task<ClientWorkspace> OpenAsync(string selectedRoot, CancellationToken cancellationToken = default)
    {
        var (full, dataRoot) = ValidateAndResolve(selectedRoot);
        var fileCount = await Task.Run(() => CountFiles(dataRoot, cancellationToken), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var workspace = new ClientWorkspace(full, dataRoot, fileCount, DateTime.UtcNow);
        await SaveAsync(workspace, cancellationToken).ConfigureAwait(false);
        return workspace;
    }

    public ClientWorkspace? Restore()
    {
        if (!File.Exists(_stateFile)) return null;
        try
        {
            var state = JsonSerializer.Deserialize<WorkspaceState>(File.ReadAllText(_stateFile));
            return state is null || !Directory.Exists(state.SelectedRoot) ? null : Open(state.SelectedRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public async Task<ClientWorkspace?> RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_stateFile)) return null;

        try
        {
            var json = await File.ReadAllTextAsync(_stateFile, cancellationToken).ConfigureAwait(false);
            var state = JsonSerializer.Deserialize<WorkspaceState>(json);
            if (state is null || !Directory.Exists(state.SelectedRoot)) return null;
            return await OpenAsync(state.SelectedRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static (string FullRoot, string DataRoot) ValidateAndResolve(string selectedRoot)
    {
        if (string.IsNullOrWhiteSpace(selectedRoot) || !Directory.Exists(selectedRoot))
            throw new DirectoryNotFoundException(selectedRoot);

        var full = Path.GetFullPath(selectedRoot);
        return (full, ResolveDataRoot(full));
    }

    private static int CountFiles(string dataRoot, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var _ in Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            checked { count++; }
        }

        return count;
    }

    private static string ResolveDataRoot(string selectedRoot)
    {
        if (string.Equals(Path.GetFileName(selectedRoot.TrimEnd(Path.DirectorySeparatorChar)), "Data", StringComparison.OrdinalIgnoreCase))
            return selectedRoot;

        var candidate = Path.Combine(selectedRoot, "Data");
        if (Directory.Exists(candidate)) return candidate;

        throw new DirectoryNotFoundException("Select the MU client root or its Data folder.");
    }

    private void Save(ClientWorkspace workspace)
    {
        File.WriteAllText(_stateFile, JsonSerializer.Serialize(new WorkspaceState(workspace.SelectedRoot)));
    }

    private Task SaveAsync(ClientWorkspace workspace, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new WorkspaceState(workspace.SelectedRoot));
        return File.WriteAllTextAsync(_stateFile, json, cancellationToken);
    }

    private sealed record WorkspaceState(string SelectedRoot);
}
