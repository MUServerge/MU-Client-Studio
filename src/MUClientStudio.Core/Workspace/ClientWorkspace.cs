namespace MUClientStudio.Core.Workspace;

public sealed record ClientWorkspace(
    string SelectedRoot,
    string DataRoot,
    int FileCount,
    DateTime LoadedAtUtc)
{
    public bool HasData => Directory.Exists(DataRoot);
}
