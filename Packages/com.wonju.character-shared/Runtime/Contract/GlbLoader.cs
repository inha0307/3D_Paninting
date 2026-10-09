using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wonju.CharacterShared
{
    /// <summary>
    /// 서버가 검사를 통과시킨 스캔 GLB(규약: 메쉬 노드 변환 없음, 텍스처 1장, PNG·JPEG)를 런타임에 불러옵니다.
    /// 외부 패키지 없이 두 앱이 같은 결과를 얻도록 공유 패키지에 둡니다.
    /// glTF(오른손 좌표, Y 위)를 Unity(왼손 좌표)로 바꿀 때 X를 뒤집고, 그에 맞춰 삼각형 순서와 UV 세로 방향을 바꿉니다.
    /// </summary>
    public static class GlbLoader
    {
        const uint Magic = 0x46546C67;      // "glTF"
        const uint ChunkJson = 0x4E4F534A;  // "JSON"
        const uint ChunkBin = 0x004E4942;   // "BIN\0"

        /// <summary>불러온 결과. Template은 비활성 상태이며, 복제해서 쓰고 다 쓰면 Dispose로 메쉬·재질·텍스처를 지웁니다.</summary>
        public sealed class Result : IDisposable
        {
            public GameObject Template;
            public Mesh Mesh;
            public Material[] Materials;
            public Texture2D[] Textures;

            /// <summary>원점 기준 메쉬 경계 (서버 규약상 피벗 = 중심, 최대 0.5m).</summary>
            public Bounds Bounds => Mesh.bounds;

            public void Dispose()
            {
                if (Template != null) UnityEngine.Object.Destroy(Template);
                if (Mesh != null) UnityEngine.Object.Destroy(Mesh);
                foreach (var m in Materials ?? Array.Empty<Material>()) if (m != null) UnityEngine.Object.Destroy(m);
                foreach (var t in Textures ?? Array.Empty<Texture2D>()) if (t != null) UnityEngine.Object.Destroy(t);
            }
        }

        /// <summary>GLB 바이트를 불러옵니다. 형식이 맞지 않으면 FormatException.</summary>
        public static Result Load(byte[] glb, Shader shader, string name)
        {
            var (gltf, bin) = ReadChunks(glb);
            var reader = new AccessorReader(gltf, bin);

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var submeshes = new List<int[]>();
            var submeshMaterials = new List<int>();
            bool allNormals = true, anyUv = false;

            foreach (var meshObj in List(gltf, "meshes"))
            {
                foreach (var primObj in List((Dictionary<string, object>)meshObj, "primitives"))
                {
                    var prim = (Dictionary<string, object>)primObj;
                    if (Int(prim, "mode", 4) != 4) throw new FormatException("삼각형이 아닌 프리미티브");
                    var attributes = (Dictionary<string, object>)prim["attributes"];
                    int baseVertex = positions.Count;

                    var pos = reader.ReadFloats(Int(attributes, "POSITION"), 3);
                    int count = pos.Length / 3;
                    for (int v = 0; v < count; v++)
                        positions.Add(new Vector3(-pos[v * 3], pos[v * 3 + 1], pos[v * 3 + 2]));

                    if (attributes.ContainsKey("NORMAL"))
                    {
                        var n = reader.ReadFloats(Int(attributes, "NORMAL"), 3);
                        for (int v = 0; v < count; v++) normals.Add(new Vector3(-n[v * 3], n[v * 3 + 1], n[v * 3 + 2]));
                    }
                    else
                    {
                        allNormals = false;
                        for (int v = 0; v < count; v++) normals.Add(Vector3.up);
                    }

                    if (attributes.ContainsKey("TEXCOORD_0"))
                    {
                        anyUv = true;
                        var uv = reader.ReadFloats(Int(attributes, "TEXCOORD_0"), 2);
                        for (int v = 0; v < count; v++) uvs.Add(new Vector2(uv[v * 2], 1f - uv[v * 2 + 1]));
                    }
                    else
                    {
                        for (int v = 0; v < count; v++) uvs.Add(Vector2.zero);
                    }

                    int[] indices = prim.ContainsKey("indices") ? reader.ReadIndices(Int(prim, "indices")) : Sequence(count);
                    var triangles = new int[indices.Length];
                    for (int t = 0; t + 2 < indices.Length; t += 3)
                    {
                        // X를 뒤집었으므로 감기 순서도 뒤집습니다.
                        triangles[t] = baseVertex + indices[t];
                        triangles[t + 1] = baseVertex + indices[t + 2];
                        triangles[t + 2] = baseVertex + indices[t + 1];
                    }
                    submeshes.Add(triangles);
                    submeshMaterials.Add(Int(prim, "material", -1));
                }
            }
            if (positions.Count == 0) throw new FormatException("메쉬가 없습니다");

            var mesh = new Mesh { name = name, indexFormat = positions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(positions);
            mesh.SetNormals(normals);
            if (anyUv) mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int i = 0; i < submeshes.Count; i++) mesh.SetTriangles(submeshes[i], i);
            if (!allNormals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var textures = LoadTextures(gltf, bin);
            var materialCache = new Dictionary<int, Material>();
            var materials = new Material[submeshes.Count];
            for (int i = 0; i < submeshes.Count; i++)
            {
                int index = submeshMaterials[i];
                if (!materialCache.TryGetValue(index, out var material))
                {
                    material = CreateMaterial(gltf, index, textures, shader, name);
                    materialCache[index] = material;
                }
                materials[i] = material;
            }

            var go = new GameObject(name);
            go.SetActive(false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return new Result
            {
                Template = go,
                Mesh = mesh,
                Materials = new List<Material>(materialCache.Values).ToArray(),
                Textures = textures,
            };
        }

        static (Dictionary<string, object> gltf, ArraySegment<byte> bin) ReadChunks(byte[] data)
        {
            if (data == null || data.Length < 20 || BitConverter.ToUInt32(data, 0) != Magic)
                throw new FormatException("GLB가 아닙니다");
            if (BitConverter.ToUInt32(data, 4) != 2 || BitConverter.ToUInt32(data, 8) != data.Length)
                throw new FormatException("GLB 헤더가 맞지 않습니다");
            int jsonLength = (int)BitConverter.ToUInt32(data, 12);
            if (BitConverter.ToUInt32(data, 16) != ChunkJson || 20 + jsonLength > data.Length)
                throw new FormatException("JSON 청크가 없습니다");
            var gltf = MiniJson.Parse(Encoding.UTF8.GetString(data, 20, jsonLength)) as Dictionary<string, object>
                       ?? throw new FormatException("glTF JSON이 객체가 아닙니다");

            int offset = 20 + jsonLength;
            var bin = new ArraySegment<byte>(Array.Empty<byte>());
            if (offset + 8 <= data.Length && BitConverter.ToUInt32(data, offset + 4) == ChunkBin)
            {
                int binLength = (int)BitConverter.ToUInt32(data, offset);
                if (offset + 8 + binLength > data.Length) throw new FormatException("BIN 청크가 잘렸습니다");
                bin = new ArraySegment<byte>(data, offset + 8, binLength);
            }
            return (gltf, bin);
        }

        static Texture2D[] LoadTextures(Dictionary<string, object> gltf, ArraySegment<byte> bin)
        {
            var images = List(gltf, "images");
            var result = new Texture2D[images.Count];
            for (int i = 0; i < images.Count; i++)
            {
                var image = (Dictionary<string, object>)images[i];
                if (!image.ContainsKey("bufferView")) throw new FormatException("GLB 밖의 이미지는 쓸 수 없습니다");
                var view = (Dictionary<string, object>)List(gltf, "bufferViews")[Int(image, "bufferView")];
                var bytes = new byte[Int(view, "byteLength")];
                Buffer.BlockCopy(bin.Array, bin.Offset + Int(view, "byteOffset", 0), bytes, 0, bytes.Length);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = $"ScanTexture{i}", wrapMode = TextureWrapMode.Repeat };
                if (!texture.LoadImage(bytes, true)) throw new FormatException("텍스처를 읽지 못했습니다");
                result[i] = texture;
            }
            return result;
        }

        static Material CreateMaterial(Dictionary<string, object> gltf, int index, Texture2D[] textures, Shader shader, string name)
        {
            var material = new Material(shader) { name = $"{name}_{index}" };
            var materials = List(gltf, "materials");
            if (index < 0 || index >= materials.Count) return material;

            var pbr = ((Dictionary<string, object>)materials[index]).TryGetValue("pbrMetallicRoughness", out var p)
                ? (Dictionary<string, object>)p : null;
            if (pbr == null) return material;

            if (pbr.TryGetValue("baseColorFactor", out var factorObj))
            {
                var f = (List<object>)factorObj;
                // glTF 색은 선형 값이고 SetColor는 감마 색을 받으므로 감마로 바꿔 넘깁니다.
                material.SetColor("_BaseColor", new Color((float)(double)f[0], (float)(double)f[1], (float)(double)f[2], 1f).gamma);
            }
            if (pbr.TryGetValue("baseColorTexture", out var texObj))
            {
                int textureIndex = Int((Dictionary<string, object>)texObj, "index");
                var gltfTexture = (Dictionary<string, object>)List(gltf, "textures")[textureIndex];
                int source = Int(gltfTexture, "source");
                if (source >= 0 && source < textures.Length) material.SetTexture("_BaseMap", textures[source]);
            }
            return material;
        }

        // ───────────── 접근자 읽기 ─────────────

        sealed class AccessorReader
        {
            readonly List<object> accessors, views;
            readonly ArraySegment<byte> bin;

            public AccessorReader(Dictionary<string, object> gltf, ArraySegment<byte> bin)
            {
                accessors = List(gltf, "accessors");
                views = List(gltf, "bufferViews");
                this.bin = bin;
            }

            public float[] ReadFloats(int accessorIndex, int components)
            {
                var (acc, view, stride, count, componentType) = Describe(accessorIndex, components);
                bool normalized = acc.TryGetValue("normalized", out var n) && n is bool b && b;
                var result = new float[count * components];
                int size = ComponentSize(componentType);
                for (int i = 0; i < count; i++)
                for (int c = 0; c < components; c++)
                {
                    int at = view + i * stride + c * size;
                    result[i * components + c] = componentType switch
                    {
                        5126 => BitConverter.ToSingle(bin.Array, at),
                        5123 => BitConverter.ToUInt16(bin.Array, at) / (normalized ? 65535f : 1f),
                        5121 => bin.Array[at] / (normalized ? 255f : 1f),
                        _ => throw new FormatException($"지원하지 않는 성분 형식 {componentType}"),
                    };
                }
                return result;
            }

            public int[] ReadIndices(int accessorIndex)
            {
                var (_, view, stride, count, componentType) = Describe(accessorIndex, 1);
                var result = new int[count];
                for (int i = 0; i < count; i++)
                {
                    int at = view + i * stride;
                    result[i] = componentType switch
                    {
                        5125 => (int)BitConverter.ToUInt32(bin.Array, at),
                        5123 => BitConverter.ToUInt16(bin.Array, at),
                        5121 => bin.Array[at],
                        _ => throw new FormatException($"지원하지 않는 인덱스 형식 {componentType}"),
                    };
                }
                return result;
            }

            (Dictionary<string, object> acc, int start, int stride, int count, int componentType) Describe(int accessorIndex, int components)
            {
                var acc = (Dictionary<string, object>)accessors[accessorIndex];
                var view = (Dictionary<string, object>)views[Int(acc, "bufferView")];
                int componentType = Int(acc, "componentType");
                int stride = Int(view, "byteStride", ComponentSize(componentType) * components);
                int start = bin.Offset + Int(view, "byteOffset", 0) + Int(acc, "byteOffset", 0);
                int count = Int(acc, "count");
                int end = start + (count - 1) * stride + ComponentSize(componentType) * components;
                if (count > 0 && end > bin.Offset + bin.Count) throw new FormatException("접근자가 BIN 범위를 넘습니다");
                return (acc, start, stride, count, componentType);
            }

            static int ComponentSize(int type) => type switch
            {
                5120 or 5121 => 1,
                5122 or 5123 => 2,
                5125 or 5126 => 4,
                _ => throw new FormatException($"알 수 없는 성분 형식 {type}"),
            };
        }

        static List<object> List(Dictionary<string, object> obj, string key) =>
            obj.TryGetValue(key, out var value) && value is List<object> list ? list : new List<object>();

        static int Int(Dictionary<string, object> obj, string key, int fallback = int.MinValue)
        {
            if (obj.TryGetValue(key, out var value) && value is double d) return (int)d;
            if (fallback == int.MinValue) throw new FormatException($"'{key}'가 없습니다");
            return fallback;
        }

        static int[] Sequence(int count)
        {
            var result = new int[count];
            for (int i = 0; i < count; i++) result[i] = i;
            return result;
        }
    }
}
