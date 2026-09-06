using System.IO;
using System.Windows.Media.Imaging;
using MUClientStudio.Models.Formats.Bmd;
using MUClientStudio.Models.Player;
using MUClientStudio.Models.Textures;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Mathematics;
using NumericsQuaternion = System.Numerics.Quaternion;
using NumericsVector3 = System.Numerics.Vector3;
using WpfPixelFormats = System.Windows.Media.PixelFormats;
using GlPixelFormat = OpenTK.Graphics.OpenGL4.PixelFormat;

namespace MUClientStudio.Rendering.Player;

/// <summary>
/// Continuous Player preview renderer. Character/model data is uploaded once; animated frames only
/// update dynamic vertex buffers. Textures/VAOs are not recreated every frame.
/// </summary>
public sealed class MuAnimatedPlayerGlRenderer
{
    private const int FloatsPerVertex = 8;
    private const float FieldOfViewDegrees = 38f;
    private const double SourceFramesPerSecond = 24.0;

    private readonly List<AnimatedGlMesh> _meshes = [];
    private PlayerCharacterSource? _pendingCharacter;
    private PlayerCharacterSource? _uploadedCharacter;
    private int _pendingActionIndex;
    private int _uploadedActionIndex = -1;
    private int _program;
    private int _viewLocation = -1;
    private int _projectionLocation = -1;
    private int _textureLocation = -1;
    private int _hasTextureLocation = -1;
    private bool _initialized;
    private float _zoom = 1f;
    private float _yaw;
    private float _pitch;
    private Bounds3 _bounds = Bounds3.Empty;
    private double _playbackSeconds;
    private long _lastAnimationTick = -1;

    public MuPlayerGlRenderStats Stats { get; private set; } = new(0, 0, 0, 0, 0, 0);

    public void SetCharacter(PlayerCharacterSource? character, int actionIndex)
    {
        var safeAction = character is null || character.ActionCount == 0
            ? 0
            : Math.Clamp(actionIndex, 0, character.ActionCount - 1);

        if (ReferenceEquals(_pendingCharacter, character) && _pendingActionIndex == safeAction)
            return;

        var characterChanged = !ReferenceEquals(_pendingCharacter, character);
        _pendingCharacter = character;
        _pendingActionIndex = safeAction;
        _playbackSeconds = 0;
        _lastAnimationTick = -1;

        if (characterChanged)
            _zoom = 1f;
    }

    public void Zoom(int wheelDelta)
    {
        if (wheelDelta == 0) return;
        _zoom = Math.Clamp(_zoom * (wheelDelta > 0 ? 0.88f : 1.14f), 0.15f, 8f);
    }

    public void Rotate(float deltaX, float deltaY)
    {
        _yaw += deltaX * 0.0105f;
        _pitch = Math.Clamp(_pitch - deltaY * 0.0085f, -1.15f, 1.15f);
    }

    public void ResetView()
    {
        _zoom = 1f;
        _yaw = 0f;
        _pitch = 0f;
    }

    public void Render(int pixelWidth, int pixelHeight, double deltaSeconds)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return;
        EnsureInitialized();

        if (!ReferenceEquals(_uploadedCharacter, _pendingCharacter) ||
            _uploadedActionIndex != _pendingActionIndex)
        {
            UploadPendingCharacter();
        }
        else
        {
            AdvanceAnimation(deltaSeconds);
        }

        GL.Viewport(0, 0, pixelWidth, pixelHeight);
        GL.ClearColor(0.031f, 0.051f, 0.075f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        if (_meshes.Count == 0 || _bounds.IsEmpty) return;

        var aspect = Math.Max(0.01f, pixelWidth / (float)pixelHeight);
        var center = _bounds.Center;
        var maxDimension = Math.Max(_bounds.Size.X, Math.Max(_bounds.Size.Y, _bounds.Size.Z));
        maxDimension = Math.Max(maxDimension, 1f);
        var halfFov = MathHelper.DegreesToRadians(FieldOfViewDegrees) * 0.5f;
        var distance = maxDimension / (2f * MathF.Tan(halfFov)) * 1.35f * _zoom;
        distance = Math.Max(distance, maxDimension * 1.15f);

        var cosPitch = MathF.Cos(_pitch);
        var orbit = new Vector3(
            MathF.Sin(_yaw) * cosPitch,
            MathF.Sin(_pitch),
            MathF.Cos(_yaw) * cosPitch);
        var eye = center + orbit * distance;
        eye.Y += _bounds.Size.Y * 0.04f;

        var view = Matrix4.LookAt(eye, center, Vector3.UnitY);
        var near = Math.Max(0.01f, distance / 10000f);
        var far = Math.Max(1000f, distance * 20f + maxDimension * 4f);
        var projection = Matrix4.CreatePerspectiveFieldOfView(
            MathHelper.DegreesToRadians(FieldOfViewDegrees), aspect, near, far);

        GL.UseProgram(_program);
        GL.UniformMatrix4(_viewLocation, true, ref view);
        GL.UniformMatrix4(_projectionLocation, true, ref projection);
        GL.Uniform1(_textureLocation, 0);

        foreach (var mesh in _meshes)
        {
            GL.ActiveTexture(TextureUnit.Texture0);
            if (mesh.TextureHandle != 0)
            {
                GL.BindTexture(TextureTarget.Texture2D, mesh.TextureHandle);
                GL.Uniform1(_hasTextureLocation, 1);
            }
            else
            {
                GL.BindTexture(TextureTarget.Texture2D, 0);
                GL.Uniform1(_hasTextureLocation, 0);
            }

            GL.BindVertexArray(mesh.VertexArrayHandle);
            GL.DrawArrays(PrimitiveType.Triangles, 0, mesh.VertexCount);
        }

        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.UseProgram(0);
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;

        _program = CreateProgram(VertexShaderSource, FragmentShaderSource);
        _viewLocation = GL.GetUniformLocation(_program, "view");
        _projectionLocation = GL.GetUniformLocation(_program, "projection");
        _textureLocation = GL.GetUniformLocation(_program, "texture0");
        _hasTextureLocation = GL.GetUniformLocation(_program, "hasTexture");

        GL.Enable(EnableCap.DepthTest);
        GL.DepthFunc(DepthFunction.Lequal);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        _initialized = true;
    }

    private void UploadPendingCharacter()
    {
        ReleaseMeshes();
        _uploadedCharacter = _pendingCharacter;
        _uploadedActionIndex = _pendingActionIndex;
        _playbackSeconds = 0;
        _lastAnimationTick = 0;
        _bounds = Bounds3.Empty;

        var character = _pendingCharacter;
        if (character is null)
        {
            Stats = new MuPlayerGlRenderStats(0, 0, 0, 0, 0, 0);
            return;
        }

        var bodyPoses = BuildPoses(character.SkeletonDocument, character.AnimationDocument, _pendingActionIndex, 0);
        var triangles = 0;
        var textures = 0;
        var missingTextures = 0;
        var bodyParts = 0;
        var attachments = 0;

        foreach (var part in character.BodyParts)
        {
            var rendered = false;
            for (var meshIndex = 0; meshIndex < part.Document.Meshes.Count; meshIndex++)
            {
                var sourceMesh = part.Document.Meshes[meshIndex];
                var texture = meshIndex < part.MeshTextures.Count ? part.MeshTextures[meshIndex] : null;
                var cpu = BuildCpuMesh(sourceMesh, bodyPoses, null, -1);
                if (cpu.Vertices.Length == 0) continue;

                var textureHandle = texture is null ? 0 : CreateTexture(texture);
                if (textureHandle != 0) textures++;
                else if (!string.IsNullOrWhiteSpace(sourceMesh.TexturePath)) missingTextures++;

                UploadMesh(
                    cpu,
                    textureHandle,
                    sourceMesh,
                    part.Document,
                    character.AnimationDocument,
                    false,
                    -1);
                triangles += cpu.TriangleCount;
                _bounds = _bounds.Include(cpu.Bounds);
                rendered = true;
            }
            if (rendered) bodyParts++;
        }

        foreach (var attachment in character.Attachments)
        {
            if (attachment.AttachBoneIndex < 0 || attachment.AttachBoneIndex >= bodyPoses.Length) continue;
            var ownPoses = BuildPoses(attachment.Document, attachment.Document, 0, 0);
            var rendered = false;

            for (var meshIndex = 0; meshIndex < attachment.Document.Meshes.Count; meshIndex++)
            {
                var sourceMesh = attachment.Document.Meshes[meshIndex];
                var texture = meshIndex < attachment.MeshTextures.Count ? attachment.MeshTextures[meshIndex] : null;
                var cpu = BuildCpuMesh(sourceMesh, ownPoses, bodyPoses, attachment.AttachBoneIndex);
                if (cpu.Vertices.Length == 0) continue;

                var textureHandle = texture is null ? 0 : CreateTexture(texture);
                if (textureHandle != 0) textures++;
                else if (!string.IsNullOrWhiteSpace(sourceMesh.TexturePath)) missingTextures++;

                UploadMesh(
                    cpu,
                    textureHandle,
                    sourceMesh,
                    attachment.Document,
                    attachment.Document,
                    true,
                    attachment.AttachBoneIndex);
                triangles += cpu.TriangleCount;
                _bounds = _bounds.Include(cpu.Bounds);
                rendered = true;
            }
            if (rendered) attachments++;
        }

        Stats = new MuPlayerGlRenderStats(_meshes.Count, triangles, textures, missingTextures, bodyParts, attachments);
    }

    private void AdvanceAnimation(double deltaSeconds)
    {
        var character = _uploadedCharacter;
        if (character is null || _meshes.Count == 0) return;

        if (!double.IsFinite(deltaSeconds) || deltaSeconds <= 0)
            deltaSeconds = 1.0 / 60.0;
        deltaSeconds = Math.Min(deltaSeconds, 0.1);
        _playbackSeconds += deltaSeconds;

        var animationTick = (long)Math.Floor(_playbackSeconds * SourceFramesPerSecond);
        if (animationTick == _lastAnimationTick)
            return;
        _lastAnimationTick = animationTick;

        var bodyFrame = GetFrameIndex(character.AnimationDocument, _uploadedActionIndex, _playbackSeconds);
        var bodyPoses = BuildPoses(character.SkeletonDocument, character.AnimationDocument, _uploadedActionIndex, bodyFrame);
        var attachmentPoseCache = new Dictionary<string, BonePose[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var mesh in _meshes)
        {
            IReadOnlyList<BonePose> poses;
            IReadOnlyList<BonePose>? parentPoses = null;
            var parentBoneIndex = -1;

            if (mesh.IsAttachment)
            {
                var key = mesh.AnimationDocument.SourcePath;
                if (!attachmentPoseCache.TryGetValue(key, out var ownPoses))
                {
                    var frame = GetFrameIndex(mesh.AnimationDocument, 0, _playbackSeconds);
                    ownPoses = BuildPoses(mesh.SkeletonDocument, mesh.AnimationDocument, 0, frame);
                    attachmentPoseCache[key] = ownPoses;
                }

                poses = ownPoses;
                parentPoses = bodyPoses;
                parentBoneIndex = mesh.AttachBoneIndex;
            }
            else
            {
                poses = bodyPoses;
            }

            var cpu = BuildCpuMesh(mesh.SourceMesh, poses, parentPoses, parentBoneIndex);
            if (cpu.VertexCount != mesh.VertexCount)
                continue;

            GL.BindBuffer(BufferTarget.ArrayBuffer, mesh.VertexBufferHandle);
            GL.BufferData(
                BufferTarget.ArrayBuffer,
                cpu.Vertices.Length * sizeof(float),
                cpu.Vertices,
                BufferUsageHint.DynamicDraw);
        }

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
    }

    private static int GetFrameIndex(BmdDocument animationDocument, int actionIndex, double playbackSeconds)
    {
        if (animationDocument.Actions.Count == 0)
            return 0;

        var safeAction = Math.Clamp(actionIndex, 0, animationDocument.Actions.Count - 1);
        var keyCount = Math.Max(1, animationDocument.Actions[safeAction].AnimationKeyCount);
        var loopFrames = Math.Max(1, keyCount - 1);
        var tick = (long)Math.Floor(Math.Max(0, playbackSeconds) * SourceFramesPerSecond);
        return (int)(tick % loopFrames);
    }

    private void UploadMesh(
        CpuMesh cpu,
        int textureHandle,
        BmdMesh sourceMesh,
        BmdDocument skeletonDocument,
        BmdDocument animationDocument,
        bool isAttachment,
        int attachBoneIndex)
    {
        var vao = GL.GenVertexArray();
        var vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(
            BufferTarget.ArrayBuffer,
            cpu.Vertices.Length * sizeof(float),
            cpu.Vertices,
            BufferUsageHint.DynamicDraw);

        var stride = FloatsPerVertex * sizeof(float);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);

        _meshes.Add(new AnimatedGlMesh(
            vao,
            vbo,
            textureHandle,
            cpu.VertexCount,
            sourceMesh,
            skeletonDocument,
            animationDocument,
            isAttachment,
            attachBoneIndex));
    }

    private static CpuMesh BuildCpuMesh(
        BmdMesh mesh,
        IReadOnlyList<BonePose> poses,
        IReadOnlyList<BonePose>? parentPoses,
        int parentBoneIndex)
    {
        var data = new List<float>(mesh.Triangles.Count * 3 * FloatsPerVertex);
        var bounds = Bounds3.Empty;
        var triangles = 0;

        foreach (var triangle in mesh.Triangles)
        {
            if (AppendTriangle(mesh, triangle, poses, parentPoses, parentBoneIndex, data, ref bounds, 0, 2, 1))
                triangles++;
            if (triangle.Polygon == 4 &&
                AppendTriangle(mesh, triangle, poses, parentPoses, parentBoneIndex, data, ref bounds, 0, 2, 3))
                triangles++;
        }

        return new CpuMesh(data.ToArray(), bounds, triangles);
    }

    private static bool AppendTriangle(
        BmdMesh mesh,
        BmdTriangle triangle,
        IReadOnlyList<BonePose> poses,
        IReadOnlyList<BonePose>? parentPoses,
        int parentBoneIndex,
        List<float> output,
        ref Bounds3 bounds,
        int a,
        int b,
        int c)
    {
        Span<int> corners = stackalloc int[3] { a, b, c };
        Span<VertexValue> values = stackalloc VertexValue[3];

        for (var i = 0; i < 3; i++)
        {
            var corner = corners[i];
            if (corner >= triangle.VertexIndices.Count ||
                corner >= triangle.NormalIndices.Count ||
                corner >= triangle.TexCoordIndices.Count)
                return false;

            var vertexIndex = triangle.VertexIndices[corner];
            var normalIndex = triangle.NormalIndices[corner];
            var uvIndex = triangle.TexCoordIndices[corner];
            if (vertexIndex < 0 || vertexIndex >= mesh.Vertices.Count ||
                normalIndex < 0 || normalIndex >= mesh.Normals.Count ||
                uvIndex < 0 || uvIndex >= mesh.TexCoords.Count)
                return false;

            var vertex = mesh.Vertices[vertexIndex];
            var normal = mesh.Normals[normalIndex];
            var uv = mesh.TexCoords[uvIndex];
            var position = TransformPosition(ToNumerics(vertex.Position), vertex.BoneIndex, poses);
            var transformedNormal = TransformNormal(ToNumerics(normal.Normal), vertex.BoneIndex, poses);

            if (parentPoses is not null && parentBoneIndex >= 0)
            {
                position = TransformPosition(position, (short)parentBoneIndex, parentPoses);
                transformedNormal = TransformNormal(transformedNormal, (short)parentBoneIndex, parentPoses);
            }

            var scenePosition = new NumericsVector3(position.X, position.Z, -position.Y);
            var sceneNormal = NormalizeSafe(new NumericsVector3(transformedNormal.X, transformedNormal.Z, -transformedNormal.Y));
            values[i] = new VertexValue(scenePosition, sceneNormal, uv.U, uv.V);
        }

        foreach (var value in values)
        {
            output.Add(value.Position.X);
            output.Add(value.Position.Y);
            output.Add(value.Position.Z);
            output.Add(value.Normal.X);
            output.Add(value.Normal.Y);
            output.Add(value.Normal.Z);
            output.Add(value.U);
            output.Add(value.V);
            bounds = bounds.Include(new Vector3(value.Position.X, value.Position.Y, value.Position.Z));
        }

        return true;
    }

    private static BonePose[] BuildPoses(
        BmdDocument skeletonDocument,
        BmdDocument animationDocument,
        int actionIndex,
        int frameIndex)
    {
        var poses = new BonePose[skeletonDocument.Bones.Count];

        for (var index = 0; index < skeletonDocument.Bones.Count; index++)
        {
            var skeletonBone = skeletonDocument.Bones[index];
            if (skeletonBone.IsDummy)
            {
                poses[index] = new BonePose(skeletonBone.ParentIndex, NumericsVector3.Zero, NumericsQuaternion.Identity);
                continue;
            }

            var animationBone = index < animationDocument.Bones.Count
                ? animationDocument.Bones[index]
                : skeletonBone;
            if (animationBone.IsDummy || animationBone.Animations.Count == 0)
            {
                poses[index] = new BonePose(skeletonBone.ParentIndex, NumericsVector3.Zero, NumericsQuaternion.Identity);
                continue;
            }

            var safeAction = Math.Clamp(actionIndex, 0, animationBone.Animations.Count - 1);
            var animation = animationBone.Animations[safeAction];
            var position = animation.Positions.Count == 0
                ? NumericsVector3.Zero
                : ToNumerics(animation.Positions[Math.Clamp(frameIndex, 0, animation.Positions.Count - 1)]);
            var rotation = animation.Rotations.Count == 0
                ? NumericsVector3.Zero
                : ToNumerics(animation.Rotations[Math.Clamp(frameIndex, 0, animation.Rotations.Count - 1)]);

            poses[index] = new BonePose(
                skeletonBone.ParentIndex,
                position,
                EulerToQuaternion(rotation));
        }

        return poses;
    }

    private static NumericsVector3 TransformPosition(
        NumericsVector3 value,
        short boneIndex,
        IReadOnlyList<BonePose> poses)
    {
        if (boneIndex < 0 || boneIndex >= poses.Count) return value;

        var result = value;
        var current = boneIndex;
        var guard = 0;
        while (current >= 0 && current < poses.Count && guard++ <= poses.Count)
        {
            var pose = poses[current];
            result = NumericsVector3.Transform(result, pose.Rotation) + pose.Position;
            current = pose.ParentIndex;
        }

        return result;
    }

    private static NumericsVector3 TransformNormal(
        NumericsVector3 value,
        short boneIndex,
        IReadOnlyList<BonePose> poses)
    {
        if (boneIndex < 0 || boneIndex >= poses.Count) return NormalizeSafe(value);

        var result = value;
        var current = boneIndex;
        var guard = 0;
        while (current >= 0 && current < poses.Count && guard++ <= poses.Count)
        {
            var pose = poses[current];
            result = NumericsVector3.Transform(result, pose.Rotation);
            current = pose.ParentIndex;
        }

        return NormalizeSafe(result);
    }

    private static NumericsQuaternion EulerToQuaternion(NumericsVector3 euler)
    {
        var halfX = euler.X * 0.5f;
        var halfY = euler.Y * 0.5f;
        var halfZ = euler.Z * 0.5f;
        var sinX = MathF.Sin(halfX);
        var cosX = MathF.Cos(halfX);
        var sinY = MathF.Sin(halfY);
        var cosY = MathF.Cos(halfY);
        var sinZ = MathF.Sin(halfZ);
        var cosZ = MathF.Cos(halfZ);

        return NumericsQuaternion.Normalize(new NumericsQuaternion(
            sinX * cosY * cosZ - cosX * sinY * sinZ,
            cosX * sinY * cosZ + sinX * cosY * sinZ,
            cosX * cosY * sinZ - sinX * sinY * cosZ,
            cosX * cosY * cosZ + sinX * sinY * sinZ));
    }

    private static NumericsVector3 NormalizeSafe(NumericsVector3 value) =>
        value.LengthSquared() > 0.000001f ? NumericsVector3.Normalize(value) : NumericsVector3.UnitZ;

    private static NumericsVector3 ToNumerics(BmdVector3 value) => new(value.X, value.Y, value.Z);

    private static int CreateTexture(MuTextureAsset texture)
    {
        try
        {
            var decoded = DecodeTexture(texture);
            if (decoded.Width <= 0 || decoded.Height <= 0 || decoded.Pixels.Length == 0) return 0;

            var handle = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, handle);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                decoded.Width,
                decoded.Height,
                0,
                GlPixelFormat.Bgra,
                PixelType.UnsignedByte,
                decoded.Pixels);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            return handle;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or ArgumentException)
        {
            return 0;
        }
    }

    private static DecodedTexture DecodeTexture(MuTextureAsset texture)
    {
        if (texture.PayloadKind == MuTexturePayloadKind.Bgra32)
        {
            if (texture.Width <= 0 || texture.Height <= 0 ||
                texture.Data.Length < texture.Width * texture.Height * 4)
                throw new InvalidDataException("Invalid BGRA texture payload.");

            var rawPixels = texture.FlipVertical
                ? FlipRows(texture.Data, texture.Width, texture.Height)
                : texture.Data;
            return new DecodedTexture(texture.Width, texture.Height, rawPixels);
        }

        using var stream = new MemoryStream(texture.Data, writable: false);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("Encoded texture contains no image frame.");

        BitmapSource source = decoder.Frames[0];
        if (source.Format != WpfPixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap();
            converted.BeginInit();
            converted.Source = source;
            converted.DestinationFormat = WpfPixelFormats.Bgra32;
            converted.EndInit();
            converted.Freeze();
            source = converted;
        }

        var stride = checked(source.PixelWidth * 4);
        var decodedPixels = new byte[checked(stride * source.PixelHeight)];
        source.CopyPixels(decodedPixels, stride, 0);
        if (texture.FlipVertical)
            decodedPixels = FlipRows(decodedPixels, source.PixelWidth, source.PixelHeight);
        return new DecodedTexture(source.PixelWidth, source.PixelHeight, decodedPixels);
    }

    private static byte[] FlipRows(byte[] source, int width, int height)
    {
        var stride = checked(width * 4);
        var output = new byte[checked(stride * height)];
        for (var y = 0; y < height; y++)
            Buffer.BlockCopy(source, y * stride, output, (height - 1 - y) * stride, stride);
        return output;
    }

    private void ReleaseMeshes()
    {
        foreach (var mesh in _meshes)
        {
            if (mesh.TextureHandle != 0) GL.DeleteTexture(mesh.TextureHandle);
            if (mesh.VertexBufferHandle != 0) GL.DeleteBuffer(mesh.VertexBufferHandle);
            if (mesh.VertexArrayHandle != 0) GL.DeleteVertexArray(mesh.VertexArrayHandle);
        }
        _meshes.Clear();
    }

    private static int CreateProgram(string vertexSource, string fragmentSource)
    {
        var vertex = CompileShader(ShaderType.VertexShader, vertexSource);
        var fragment = CompileShader(ShaderType.FragmentShader, fragmentSource);
        var program = GL.CreateProgram();
        GL.AttachShader(program, vertex);
        GL.AttachShader(program, fragment);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out var linked);
        var log = GL.GetProgramInfoLog(program);
        GL.DetachShader(program, vertex);
        GL.DetachShader(program, fragment);
        GL.DeleteShader(vertex);
        GL.DeleteShader(fragment);
        if (linked != 0) return program;
        GL.DeleteProgram(program);
        throw new InvalidOperationException($"MU Player OpenGL shader link failed: {log}");
    }

    private static int CompileShader(ShaderType type, string source)
    {
        var shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out var compiled);
        if (compiled != 0) return shader;
        var log = GL.GetShaderInfoLog(shader);
        GL.DeleteShader(shader);
        throw new InvalidOperationException($"MU Player OpenGL {type} compile failed: {log}");
    }

    private const string VertexShaderSource = """
        #version 330 core
        layout (location = 0) in vec3 aPosition;
        layout (location = 1) in vec3 aNormal;
        layout (location = 2) in vec2 aTexCoord;
        out vec3 Normal;
        out vec2 TexCoord;
        uniform mat4 view;
        uniform mat4 projection;
        void main()
        {
            gl_Position = vec4(aPosition, 1.0) * view * projection;
            Normal = aNormal;
            TexCoord = aTexCoord;
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec3 Normal;
        in vec2 TexCoord;
        out vec4 FragColor;
        uniform sampler2D texture0;
        uniform int hasTexture;
        void main()
        {
            vec4 baseColor = hasTexture != 0 ? texture(texture0, TexCoord) : vec4(0.66, 0.71, 0.77, 1.0);
            if (baseColor.a < 0.04) discard;
            vec3 normal = normalize(Normal);
            vec3 lightDirection = normalize(vec3(0.35, 0.55, 0.76));
            float diffuse = max(dot(normal, lightDirection), 0.0);
            float lighting = 0.38 + diffuse * 0.78;
            FragColor = vec4(baseColor.rgb * lighting, baseColor.a);
        }
        """;

    private readonly record struct AnimatedGlMesh(
        int VertexArrayHandle,
        int VertexBufferHandle,
        int TextureHandle,
        int VertexCount,
        BmdMesh SourceMesh,
        BmdDocument SkeletonDocument,
        BmdDocument AnimationDocument,
        bool IsAttachment,
        int AttachBoneIndex);

    private readonly record struct CpuMesh(float[] Vertices, Bounds3 Bounds, int TriangleCount)
    {
        public int VertexCount => Vertices.Length / FloatsPerVertex;
    }

    private readonly record struct DecodedTexture(int Width, int Height, byte[] Pixels);
    private readonly record struct BonePose(short ParentIndex, NumericsVector3 Position, NumericsQuaternion Rotation);
    private readonly record struct VertexValue(NumericsVector3 Position, NumericsVector3 Normal, float U, float V);

    private readonly record struct Bounds3(Vector3 Min, Vector3 Max, bool IsEmpty)
    {
        public static Bounds3 Empty => new(Vector3.Zero, Vector3.Zero, true);
        public Vector3 Size => IsEmpty ? Vector3.Zero : Max - Min;
        public Vector3 Center => IsEmpty ? Vector3.Zero : (Min + Max) * 0.5f;

        public Bounds3 Include(Vector3 point)
        {
            if (IsEmpty) return new Bounds3(point, point, false);
            return new Bounds3(Vector3.ComponentMin(Min, point), Vector3.ComponentMax(Max, point), false);
        }

        public Bounds3 Include(Bounds3 other)
        {
            if (other.IsEmpty) return this;
            if (IsEmpty) return other;
            return new Bounds3(
                Vector3.ComponentMin(Min, other.Min),
                Vector3.ComponentMax(Max, other.Max),
                false);
        }
    }
}
