using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Wonju.CharacterShared.Editor
{
    /// <summary>
    /// 베이스 캐릭터에서 부위 마스크와 기본 컬러 텍스처를 만들고 UV 겹침을 검사합니다.
    /// 부위는 본 가중치를 부위별로 합산해 픽셀마다 보간한 뒤 가장 큰 쪽으로 정하므로 경계가 매끄럽습니다.
    /// 캐릭터를 바꾸면 메뉴를 다시 실행합니다.
    /// </summary>
    public static class CharacterAssetBaker
    {
        public const string PackageRoot = "Packages/com.wonju.character-shared/Runtime";
        public const string ModelPath = PackageRoot + "/Models/character_vr_paint3d.fbx";
        public const string MaskPath = PackageRoot + "/Textures/Masks/PartMask.png";
        public const string DefaultTexturePath = PackageRoot + "/Textures/DefaultColor.png";
        const string DebugDir = "Temp/CharacterBake";
        const int DilatePixels = 4;
        const float InteriorMarginPx = 0.5f;

        [MenuItem("Tools/Character/Bake Part Mask and Default Texture")]
        static void BakeMenu() => Debug.Log(Bake(ModelPath));

        public static string Bake(string modelPath)
        {
            int size = CharacterParts.TextureSize;
            ConfigureModel(modelPath);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var smr = model.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = smr.sharedMesh;
            var uv = mesh.uv;
            var tris = mesh.triangles;
            var weights = mesh.boneWeights;
            var bones = smr.bones;

            const int partCount = (int)CharacterPart.RightFoot + 1;
            var bonePart = bones.Select(b => (int)CharacterParts.FromBoneName(b.name)).ToArray();
            var vertexPartWeights = new float[weights.Length * partCount];
            for (int v = 0; v < weights.Length; v++)
            {
                var w = weights[v];
                int o = v * partCount;
                vertexPartWeights[o + bonePart[w.boneIndex0]] += w.weight0;
                vertexPartWeights[o + bonePart[w.boneIndex1]] += w.weight1;
                vertexPartWeights[o + bonePart[w.boneIndex2]] += w.weight2;
                vertexPartWeights[o + bonePart[w.boneIndex3]] += w.weight3;
            }

            var ids = new byte[size * size];
            var interiorHits = new byte[size * size];
            var raster = new TriangleRaster(size, partCount, vertexPartWeights, ids, interiorHits);
            for (int t = 0; t < tris.Length; t += 3)
                raster.Draw(tris[t], tris[t + 1], tris[t + 2], uv);

            var pixelsPerPart = new int[partCount];
            foreach (var id in ids) pixelsPerPart[id]++;

            int covered = ids.Count(id => id != 0);
            int interior = interiorHits.Count(h => h >= 1);
            int overlap = interiorHits.Count(h => h >= 2);
            SaveDebugImage(ids, interiorHits, size);

            Dilate(ids, size, DilatePixels);

            var mask = new Color32[size * size];
            var colors = new Color32[size * size];
            for (int i = 0; i < ids.Length; i++)
            {
                byte v = CharacterParts.Encode((CharacterPart)ids[i]);
                mask[i] = new Color32(v, v, v, 255);
                colors[i] = CharacterParts.DefaultColor((CharacterPart)ids[i]);
            }
            WritePng(MaskPath, mask, size);
            WritePng(DefaultTexturePath, colors, size);
            ConfigureMask(MaskPath);
            ConfigureColor(DefaultTexturePath);

            var report = new StringBuilder();
            report.AppendLine($"[CharacterAssetBaker] {Path.GetFileName(modelPath)}: 삼각형 {tris.Length / 3}, 텍스처 {size}px");
            report.AppendLine($"UV 사용 면적 {100f * covered / ids.Length:F1}%, 겹침 {overlap}px ({100f * overlap / Mathf.Max(1, interior):F2}% of interior)");
            for (int p = 1; p < partCount; p++)
                report.AppendLine($"  {(CharacterPart)p}: {100f * pixelsPerPart[p] / ids.Length:F1}%");
            report.Append($"디버그 이미지: {DebugDir}/uv_overlap.png (빨강 = 겹침)");
            return report.ToString();
        }

        sealed class TriangleRaster
        {
            readonly int size, partCount;
            readonly float[] vertexPartWeights;
            readonly byte[] ids, interiorHits;

            public TriangleRaster(int size, int partCount, float[] vertexPartWeights, byte[] ids, byte[] interiorHits)
            {
                this.size = size;
                this.partCount = partCount;
                this.vertexPartWeights = vertexPartWeights;
                this.ids = ids;
                this.interiorHits = interiorHits;
            }

            public void Draw(int i0, int i1, int i2, Vector2[] uv)
            {
                Vector2 a = uv[i0] * size, b = uv[i1] * size, c = uv[i2] * size;
                float area = Edge(a, b, c);
                if (Mathf.Abs(area) < 1e-6f) return;
                if (area < 0) { (b, c) = (c, b); (i1, i2) = (i2, i1); area = -area; }
                float lab = (b - a).magnitude, lbc = (c - b).magnitude, lca = (a - c).magnitude;

                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x)));
                int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x, c.x)));
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y, c.y)));
                int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y, c.y)));

                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float e0 = Edge(b, c, p), e1 = Edge(c, a, p), e2 = Edge(a, b, p);
                    if (e0 < 0 || e1 < 0 || e2 < 0) continue;
                    int i = y * size + x;
                    ids[i] = (byte)DominantPart(i0, i1, i2, e0 / area, e1 / area, e2 / area);
                    if (e0 / lbc > InteriorMarginPx && e1 / lca > InteriorMarginPx && e2 / lab > InteriorMarginPx && interiorHits[i] < 255)
                        interiorHits[i]++;
                }
            }

            int DominantPart(int i0, int i1, int i2, float w0, float w1, float w2)
            {
                int best = (int)CharacterPart.Torso;
                float bestWeight = -1f;
                for (int part = 1; part < partCount; part++)
                {
                    float w = w0 * vertexPartWeights[i0 * partCount + part]
                            + w1 * vertexPartWeights[i1 * partCount + part]
                            + w2 * vertexPartWeights[i2 * partCount + part];
                    if (w > bestWeight) { bestWeight = w; best = part; }
                }
                return best;
            }
        }

        static float Edge(Vector2 a, Vector2 b, Vector2 p) => (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);

        /// <summary>UV 아일랜드 바깥 픽셀을 이웃 부위 ID로 채워 이음새에서 배경이 비치지 않게 합니다.</summary>
        static void Dilate(byte[] ids, int size, int iterations)
        {
            var next = new byte[ids.Length];
            for (int it = 0; it < iterations; it++)
            {
                System.Array.Copy(ids, next, ids.Length);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    if (ids[i] != 0) continue;
                    for (int dy = -1; dy <= 1 && next[i] == 0; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                        byte n = ids[ny * size + nx];
                        if (n != 0) { next[i] = n; break; }
                    }
                }
                System.Array.Copy(next, ids, ids.Length);
            }
        }

        static void SaveDebugImage(byte[] ids, byte[] interiorHits, int size)
        {
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                if (interiorHits[i] >= 2) pixels[i] = new Color32(255, 0, 0, 255);
                else if (ids[i] != 0)
                {
                    var c = CharacterParts.DefaultColor((CharacterPart)ids[i]);
                    pixels[i] = new Color32((byte)(c.r * 0.7f), (byte)(c.g * 0.7f), (byte)(c.b * 0.7f), 255);
                }
                else pixels[i] = new Color32(20, 20, 20, 255);
            }
            Directory.CreateDirectory(DebugDir);
            File.WriteAllBytes(Path.Combine(DebugDir, "uv_overlap.png"), ToPng(pixels, size));
        }

        static byte[] ToPng(Color32[] pixels, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            var png = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return png;
        }

        static void WritePng(string assetPath, Color32[] pixels, int size)
        {
            File.WriteAllBytes(Path.GetFullPath(assetPath), ToPng(pixels, size));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
        }

        /// <summary>런타임 MeshCollider·BakeMesh용으로 읽기 가능하게 하고, 내장 머티리얼과 애니메이션은 쓰지 않습니다.</summary>
        static void ConfigureModel(string path)
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(path);
            if (imp.isReadable && imp.materialImportMode == ModelImporterMaterialImportMode.None && !imp.importAnimation)
                return;
            imp.isReadable = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;
            imp.importAnimation = false;
            imp.SaveAndReimport();
        }

        static void ConfigureMask(string path)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = false;
            imp.mipmapEnabled = false;
            imp.filterMode = FilterMode.Point;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.isReadable = true;
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.maxTextureSize = CharacterParts.TextureSize;
            imp.SaveAndReimport();
        }

        static void ConfigureColor(string path)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = TextureImporterType.Default;
            imp.sRGBTexture = true;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.maxTextureSize = CharacterParts.TextureSize;
            imp.SaveAndReimport();
        }
    }
}
