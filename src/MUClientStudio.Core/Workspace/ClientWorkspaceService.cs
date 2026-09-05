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
        if (string.IsNullOrWhiteSpace(selectedRoot) || !Directory.Exists(selectedRoot))
            throw new DirectoryNotFoundException(selectedRoot);

        var full = Path.GetFullPath(selectedRoot);
        var dataRoot = ResolveDataRoot(full);
        var fileCount = Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories).Count();
        var workspace = new ClientWorkspace(full, dataRoot, fileCount, DateTime.UtcNow);
        Save(workspace);
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
        catch
        {
            return null;
        }
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

    private sealed record WorkspaceState(string SelectedRoot);
}
