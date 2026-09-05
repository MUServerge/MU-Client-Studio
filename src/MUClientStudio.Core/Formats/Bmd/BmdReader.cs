using System.Buffers.Binary;
using System.Text;
using MUClientStudio.Models.Formats.Bmd;

namespace MUClientStudio.Core.Formats.Bmd;

public sealed class BmdReader
{
    private const int MaximumMeshes = 4096;
    private const int MaximumBones = 4096;
    private const int MaximumActions = 4096;
    private const int MaximumMeshElements = 1_000_000;
    private const int MaximumAnimationKeys = 100_000;
    private const int TriangleStride = 64;

    public async Task<BmdDocument> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("BMD path is required.", nameof(path));

        var file = new FileInfo(path);
        if (!file.Exists)
            throw new FileNotFoundException("BMD model was not found.", path);
        if (file.Length > BmdFileDecoder.MaximumFileSize)
            throw new BmdFormatException($"BMD file exceeds the {BmdFileDecoder.MaximumFileSize / 1024 / 1024} MB safety limit.");

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Read(bytes, path, cancellationToken);
    }

    public BmdDocument Read(ReadOnlySpan<byte> source, string sourcePath = "<memory>", CancellationToken cancellationToken = default)
    {
        var decoded = BmdFileDecoder.Decode(source);
        var reader = new BufferReader(decoded.Payload);

        var name = reader.ReadString(32, "model name");
        var meshCount = AssertCount(reader.ReadUInt16(), MaximumMeshes, "mesh");
        var boneCount = AssertCount(reader.ReadUInt16(), MaximumBones, "bone");
        var actionCount = AssertCount(reader.ReadUInt16(), MaximumActions, "action");

        var meshes = new List<BmdMesh>(meshCount);
        for (var meshIndex = 0; meshIndex < meshCount; meshIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var vertexCount = AssertCount(reader.ReadInt16(), MaximumMeshElements, $"mesh {meshIndex} vertex");
            var normalCount = AssertCount(reader.ReadInt16(), MaximumMeshElements, $"mesh {meshIndex} normal");
            var texCoordCount = AssertCount(reader.ReadInt16(), MaximumMeshElements, $"mesh {meshIndex} texcoord");
            var triangleCount = AssertCount(reader.ReadInt16(), MaximumMeshElements, $"mesh {meshIndex} triangle");
            var textureIndex = reader.ReadInt16();

            var vertices = new List<BmdVertex>(vertexCount);
            for (var i = 0; i < vertexCount; i++)
            {
                var node = reader.ReadInt16();
                reader.Skip(2, $"mesh {meshIndex} vertex padding");
                vertices.Add(new BmdVertex(node, new BmdVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())));
            }

            var normals = new List<BmdNormal>(normalCount);
            for (var i = 0; i < normalCount; i++)
            {
                var node = reader.ReadInt16();
                reader.Skip(2, $"mesh {meshIndex} normal padding");
                var normal = new BmdVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                var bindVertex = reader.ReadInt16();
                reader.Skip(2, $"mesh {meshIndex} normal tail padding");
                normals.Add(new BmdNormal(node, normal, bindVertex));
            }

            var texCoords = new List<BmdTexCoord>(texCoordCount);
            for (var i = 0; i < texCoordCount; i++)
                texCoords.Add(new BmdTexCoord(reader.ReadSingle(), reader.ReadSingle()));

            var triangles = new List<BmdTriangle>(triangleCount);
            for (var triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
            {
                var triangleStart = reader.Offset;
                var polygon = reader.ReadByte();
                if (polygon is not (3 or 4))
                    throw new BmdFormatException($"Invalid polygon size {polygon} in mesh {meshIndex}, triangle {triangleIndex}.");

                reader.Skip(1, $"mesh {meshIndex} triangle padding");
                var vertexIndices = ReadFourInt16(reader);
                var normalIndices = ReadFourInt16(reader);
                var texCoordIndices = ReadFourInt16(reader);

                var consumed = reader.Offset - triangleStart;
                reader.Skip(TriangleStride - consumed, $"mesh {meshIndex} triangle tail");
                triangles.Add(new BmdTriangle(polygon, vertexIndices, normalIndices, texCoordIndices));
            }

            var texturePath = reader.ReadString(32, $"mesh {meshIndex} texture path");
            meshes.Add(new BmdMesh(textureIndex, texturePath, vertices, normals, texCoords, triangles));
        }

        var actions = new List<BmdAction>(actionCount);
        for (var actionIndex = 0; actionIndex < actionCount; actionIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var keyCountValue = reader.ReadInt16();
            var keyCount = AssertCount(keyCountValue, MaximumAnimationKeys, $"action {actionIndex} key");
            var lockPositions = reader.ReadByte() > 0;
            var lockedPositions = new List<BmdVector3>(lockPositions ? keyCount : 0);

            if (lockPositions)
            {
                for (var keyIndex = 0; keyIndex < keyCount; keyIndex++)
                    lockedPositions.Add(new BmdVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
            }

            actions.Add(new BmdAction((short)keyCount, lockPositions, lockedPositions));
        }

        var bones = new List<BmdBone>(boneCount);
        for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var isDummy = reader.ReadByte() > 0;
            if (isDummy)
            {
                bones.Add(new BmdBone(boneIndex, $"dummy_{boneIndex}", -1, true, Array.Empty<BmdBoneAnimation>()));
                continue;
            }

            var boneName = reader.ReadString(32, $"bone {boneIndex} name");
            var parentIndex = reader.ReadInt16();
            if (parentIndex < -1 || parentIndex >= boneCount)
                throw new BmdFormatException($"Invalid parent index {parentIndex} for bone {boneIndex}.");

            var animations = new List<BmdBoneAnimation>(actionCount);
            for (var actionIndex = 0; actionIndex < actionCount; actionIndex++)
            {
                var keyCount = actions[actionIndex].AnimationKeyCount;
                if (keyCount <= 0)
                {
                    animations.Add(new BmdBoneAnimation(
                        [new BmdVector3(0, 0, 0)],
                        [new BmdVector3(0, 0, 0)]));
                    continue;
                }

                var positions = new List<BmdVector3>(keyCount);
                for (var keyIndex = 0; keyIndex < keyCount; keyIndex++)
                    positions.Add(new BmdVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));

                var rotations = new List<BmdVector3>(keyCount);
                for (var keyIndex = 0; keyIndex < keyCount; keyIndex++)
                    rotations.Add(new BmdVector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));

                animations.Add(new BmdBoneAnimation(positions, rotations));
            }

            bones.Add(new BmdBone(boneIndex, boneName, parentIndex, false, animations));
        }

        return new BmdDocument(
            decoded.Version,
            decoded.IsEncrypted,
            name,
            meshes,
            bones,
            actions,
            sourcePath);
    }

    private static short[] ReadFourInt16(BufferReader reader) =>
    [reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16(), reader.ReadInt16()];

    private static int AssertCount(int value, int maximum, string label)
    {
        if (value < 0 || value > maximum)
            throw new BmdFormatException($"Invalid {label} count: {value}.");
        return value;
    }

    private sealed class BufferReader
    {
        private readonly byte[] _buffer;

        public BufferReader(byte[] buffer)
        {
            _buffer = buffer;
        }

        public int Offset { get; private set; }

        public byte ReadByte()
        {
            Ensure(1, "byte");
            return _buffer[Offset++];
        }

        public short ReadInt16()
        {
            Ensure(2, "int16");
            var value = BinaryPrimitives.ReadInt16LittleEndian(_buffer.AsSpan(Offset, 2));
            Offset += 2;
            return value;
        }

        public ushort ReadUInt16()
        {
            Ensure(2, "uint16");
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_buffer.AsSpan(Offset, 2));
            Offset += 2;
            return value;
        }

        public float ReadSingle()
        {
            Ensure(4, "float32");
            var bits = BinaryPrimitives.ReadInt32LittleEndian(_buffer.AsSpan(Offset, 4));
            Offset += 4;
            return BitConverter.Int32BitsToSingle(bits);
        }

        public string ReadString(int length, string label)
        {
            Ensure(length, label);
            var span = _buffer.AsSpan(Offset, length);
            Offset += length;
            var nullIndex = span.IndexOf((byte)0);
            if (nullIndex >= 0) span = span.Slice(0, nullIndex);
            return Encoding.ASCII.GetString(span);
        }

        public void Skip(int byteCount, string label)
        {
            if (byteCount < 0)
                throw new BmdFormatException($"Invalid negative skip while reading {label}.");
            Ensure(byteCount, label);
            Offset += byteCount;
        }

        private void Ensure(int byteCount, string label)
        {
            if (Offset < 0 || byteCount < 0 || Offset > _buffer.Length - byteCount)
                throw new BmdFormatException($"Truncated BMD while reading {label} at offset {Offset}.");
        }
    }
}
