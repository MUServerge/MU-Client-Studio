namespace MUClientStudio.Models.Formats.Bmd;

public readonly record struct BmdVector3(float X, float Y, float Z);
public readonly record struct BmdTexCoord(float U, float V);

public sealed record BmdVertex(short BoneIndex, BmdVector3 Position);
public sealed record BmdNormal(short BoneIndex, BmdVector3 Normal, short BindVertex);

public sealed record BmdTriangle(
    byte Polygon,
    IReadOnlyList<short> VertexIndices,
    IReadOnlyList<short> NormalIndices,
    IReadOnlyList<short> TexCoordIndices);

public sealed record BmdMesh(
    short TextureIndex,
    string TexturePath,
    IReadOnlyList<BmdVertex> Vertices,
    IReadOnlyList<BmdNormal> Normals,
    IReadOnlyList<BmdTexCoord> TexCoords,
    IReadOnlyList<BmdTriangle> Triangles);

public sealed record BmdAction(
    short AnimationKeyCount,
    bool LockPositions,
    IReadOnlyList<BmdVector3> LockedPositions);

public sealed record BmdBoneAnimation(
    IReadOnlyList<BmdVector3> Positions,
    IReadOnlyList<BmdVector3> Rotations);

public sealed record BmdBone(
    int BmdBoneIndex,
    string Name,
    short ParentIndex,
    bool IsDummy,
    IReadOnlyList<BmdBoneAnimation> Animations);

public sealed record BmdDocument(
    byte Version,
    bool IsEncrypted,
    string Name,
    IReadOnlyList<BmdMesh> Meshes,
    IReadOnlyList<BmdBone> Bones,
    IReadOnlyList<BmdAction> Actions,
    string SourcePath)
{
    public int MeshCount => Meshes.Count;
    public int BoneCount => Bones.Count;
    public int ActionCount => Actions.Count;
}
