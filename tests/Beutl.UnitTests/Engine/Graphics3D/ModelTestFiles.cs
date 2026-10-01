using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.UnitTests.Engine.Graphics3D;

internal static class ModelTestFiles
{
    public static string WriteGltf(string directory, bool externalBuffer = false, bool embeddedTexture = false, bool binary = false)
    {
        Directory.CreateDirectory(directory);
        float[] vertices = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        byte[] buffer = vertices.SelectMany(BitConverter.GetBytes).ToArray();
        if (externalBuffer)
        {
            Directory.CreateDirectory(Path.Combine(directory, "buffers"));
            File.WriteAllBytes(Path.Combine(directory, "buffers", "geometry.bin"), buffer);
        }
        var json = JsonSerializer.SerializeToNode(new
        {
            asset = new { version = "2.0" },
            scene = 0,
            scenes = new[] { new { nodes = new[] { 0 } } },
            nodes = new[] { new { mesh = 0 } },
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0 }, mode = 4 } } } },
            buffers = new[] { new { uri = externalBuffer ? "buffers/geometry.bin" : "data:application/octet-stream;base64," + Convert.ToBase64String(buffer), byteLength = buffer.Length } },
            bufferViews = new[] { new { buffer = 0, byteOffset = 0, byteLength = 36, target = 34962 } },
            accessors = new[] { new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3", min = new[] { 0, 0, 0 }, max = new[] { 1, 1, 0 } } },
        })!;
        if (embeddedTexture)
        {
            using var bitmap = new Bitmap(2, 2);
            bitmap.GetPixelSpan().Fill(255);
            using var stream = new MemoryStream();
            Assert.That(bitmap.Save(stream, EncodedImageFormat.Png), Is.True);
            byte[] png = stream.ToArray();
            json["meshes"]![0]!["primitives"]![0]!["material"] = 0;
            json["materials"] = JsonNode.Parse("[{\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}]");
            json["textures"] = JsonNode.Parse("[{\"source\":0}]");
            if (binary)
            {
                json["bufferViews"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { buffer = 0, byteOffset = buffer.Length, byteLength = png.Length }));
                json["images"] = JsonSerializer.SerializeToNode(new[] { new { bufferView = 1, mimeType = "image/png" } });
                buffer = [.. buffer, .. png];
            }
            else
                json["images"] = JsonSerializer.SerializeToNode(new[] { new { uri = "data:image/png;base64," + Convert.ToBase64String(png) } });
        }
        if (!binary)
        {
            string path = Path.Combine(directory, "model.gltf");
            File.WriteAllText(path, json.ToJsonString());
            return path;
        }

        json["buffers"]![0]!.AsObject().Remove("uri");
        json["buffers"]![0]!["byteLength"] = buffer.Length;
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json.ToJsonString());
        int jsonLength = (jsonBytes.Length + 3) / 4 * 4;
        int binaryLength = (buffer.Length + 3) / 4 * 4;
        string glb = Path.Combine(directory, "model.glb");
        using var writer = new BinaryWriter(File.Create(glb));
        writer.Write(0x46546c67); writer.Write(2); writer.Write(12 + 8 + jsonLength + 8 + binaryLength);
        writer.Write(jsonLength); writer.Write(0x4e4f534a); writer.Write(jsonBytes);
        for (int i = jsonBytes.Length; i < jsonLength; i++) writer.Write((byte)32);
        writer.Write(binaryLength); writer.Write(0x004e4942); writer.Write(buffer);
        for (int i = buffer.Length; i < binaryLength; i++) writer.Write((byte)0);
        return glb;
    }
}
