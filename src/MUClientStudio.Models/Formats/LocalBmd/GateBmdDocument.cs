namespace MUClientStudio.Models.Formats.LocalBmd;

/// <summary>
/// Client Gate.bmd entry. The binary file stores 1024 fixed GATE_ATTRIBUTE records; the array
/// position is the gate index.
/// </summary>
public sealed record GateRecord(
    int Index,
    byte Flag,
    byte Map,
    byte X1,
    byte Y1,
    byte X2,
    byte Y2,
    ushort TargetGate,
    byte Angle,
    ushort MinLevel,
    ushort MaxLevel,
    byte[] RawRecord)
{
    public bool IsDefined =>
        Flag != 0 || Map != 0 || X1 != 0 || Y1 != 0 || X2 != 0 || Y2 != 0 ||
        TargetGate != 0 || Angle != 0 || MinLevel != 0 || MaxLevel != 0;
}

public sealed record GateBmdDocument(
    IReadOnlyList<GateRecord> Records,
    string SourcePath)
{
    public const int RecordCount = 1024;
    public const int RecordSize = 14;

    public IEnumerable<GateRecord> DefinedGates => Records.Where(record => record.IsDefined);

    public GateRecord? Find(int index) =>
        index >= 0 && index < Records.Count ? Records[index] : null;
}
