using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wonju.CharacterShared.Editor
{
    /// <summary>
    /// 실제 장식 모델이 나오기 전까지 쓸 임시 장식 5종을 기본 도형으로 만들고 AccessoryDatabase를 채웁니다.
    /// 장식마다 도형을 재질별 서브메쉬 하나로 합쳐 렌더러 1개로 만듭니다 (Quest에서 아바타 20명 × 장식 5개).
    /// 실제 모델로 바꿀 때는 DB 항목의 prefab만 교체하고 itemId는 그대로 둡니다.
    /// </summary>
    public static class AccessoryPlaceholderBuilder
    {
        const string Root = "Packages/com.wonju.character-shared/Runtime/Accessories";
        public const string DatabasePath = "Packages/com.wonju.character-shared/Runtime/AccessoryDatabase.asset";
        const string ItemIdsPath = "Contract/item_ids.json";

        readonly struct Part
        {
            public readonly PrimitiveType Type;
            public readonly string Color;
            public readonly Vector3 Position, Rotation, Scale;

            public Part(PrimitiveType type, string color, Vector3 position, Vector3 scale, Vector3 rotation = default)
            {
                Type = type;
                Color = color;
                Position = position;
                Scale = scale;
                Rotation = rotation;
            }
        }

        static readonly Dictionary<string, Color> Colors = new()
        {
            ["Red"] = new Color(0.86f, 0.2f, 0.2f),
            ["Cream"] = new Color(0.98f, 0.93f, 0.8f),
            ["Dark"] = new Color(0.12f, 0.13f, 0.16f),
            ["Gold"] = new Color(0.98f, 0.76f, 0.18f),
            ["Blue"] = new Color(0.2f, 0.45f, 0.85f),
            ["Purple"] = new Color(0.55f, 0.3f, 0.8f),
            ["Yellow"] = new Color(1f, 0.9f, 0.3f),
        };

        // 단위 m. 원점 = 본에 붙는 지점. 원통 기본 높이 2, 반지름 0.5 / 구 지름 1 / 상자 1.
        static (string id, string name, string[] bones, Vector3 pos, Vector3 rot, Part[] parts)[] Specs() => new[]
        {
            ("hat_01", "모자", new[] { "Head" }, new Vector3(0f, 0.25f, 0f), Vector3.zero, new[]
            {
                new Part(PrimitiveType.Cylinder, "Red", new Vector3(0f, 0.01f, 0f), new Vector3(0.36f, 0.01f, 0.36f)),
                new Part(PrimitiveType.Cylinder, "Red", new Vector3(0f, 0.08f, 0f), new Vector3(0.24f, 0.07f, 0.24f)),
                new Part(PrimitiveType.Cylinder, "Cream", new Vector3(0f, 0.035f, 0f), new Vector3(0.245f, 0.015f, 0.245f)),
            }),
            ("glasses_01", "안경", new[] { "Head" }, new Vector3(0f, 0.14f, 0.16f), Vector3.zero, new[]
            {
                new Part(PrimitiveType.Cylinder, "Dark", new Vector3(-0.05f, 0f, 0f), new Vector3(0.07f, 0.004f, 0.07f), new Vector3(90f, 0f, 0f)),
                new Part(PrimitiveType.Cylinder, "Dark", new Vector3(0.05f, 0f, 0f), new Vector3(0.07f, 0.004f, 0.07f), new Vector3(90f, 0f, 0f)),
                new Part(PrimitiveType.Cube, "Dark", new Vector3(0f, 0.005f, 0f), new Vector3(0.035f, 0.008f, 0.008f)),
                new Part(PrimitiveType.Cube, "Dark", new Vector3(-0.088f, 0.005f, -0.06f), new Vector3(0.008f, 0.008f, 0.12f)),
                new Part(PrimitiveType.Cube, "Dark", new Vector3(0.088f, 0.005f, -0.06f), new Vector3(0.008f, 0.008f, 0.12f)),
            }),
            ("backpack_01", "가방", new[] { "Chest", "Hips" }, new Vector3(0f, 0.07f, -0.1f), Vector3.zero, new[]
            {
                new Part(PrimitiveType.Cube, "Blue", new Vector3(0f, 0f, -0.04f), new Vector3(0.2f, 0.22f, 0.08f)),
                new Part(PrimitiveType.Cube, "Yellow", new Vector3(0f, -0.05f, -0.09f), new Vector3(0.14f, 0.08f, 0.02f)),
                new Part(PrimitiveType.Cube, "Dark", new Vector3(0f, 0.115f, -0.04f), new Vector3(0.08f, 0.01f, 0.03f)),
            }),
            ("crown_01", "왕관", new[] { "Head" }, new Vector3(0f, 0.27f, 0f), Vector3.zero, CrownParts()),
            ("wand_01", "요술봉", new[] { "Right_Hand_Socket", "Left_Hand_Socket" }, Vector3.zero, new Vector3(90f, 0f, 0f), new[]
            {
                new Part(PrimitiveType.Cylinder, "Purple", new Vector3(0f, 0.06f, 0f), new Vector3(0.016f, 0.12f, 0.016f)),
                new Part(PrimitiveType.Sphere, "Yellow", new Vector3(0f, 0.2f, 0f), new Vector3(0.055f, 0.055f, 0.055f)),
                new Part(PrimitiveType.Cube, "Yellow", new Vector3(0f, 0.2f, 0f), new Vector3(0.05f, 0.05f, 0.012f), new Vector3(0f, 0f, 45f)),
            }),
        };

        static Part[] CrownParts()
        {
            var parts = new List<Part>
            {
                new(PrimitiveType.Cylinder, "Gold", new Vector3(0f, 0.03f, 0f), new Vector3(0.2f, 0.03f, 0.2f)),
            };
            for (int i = 0; i < 5; i++)
            {
                float a = i * Mathf.PI * 2f / 5f;
                parts.Add(new Part(PrimitiveType.Cube, "Gold", new Vector3(Mathf.Sin(a) * 0.085f, 0.075f, Mathf.Cos(a) * 0.085f),
                    new Vector3(0.03f, 0.03f, 0.03f), new Vector3(45f, a * Mathf.Rad2Deg, 45f)));
            }
            parts.Add(new Part(PrimitiveType.Sphere, "Red", new Vector3(0f, 0.035f, 0.1f), new Vector3(0.025f, 0.025f, 0.025f)));
            return parts.ToArray();
        }

        [MenuItem("Tools/Character/Build Placeholder Accessories")]
        static void BuildMenu() => Debug.Log(Build());

        public static string Build()
        {
            Directory.CreateDirectory(Path.GetFullPath(Root + "/Meshes"));
            Directory.CreateDirectory(Path.GetFullPath(Root + "/Materials"));
            AssetDatabase.Refresh();

            var materials = Colors.ToDictionary(c => c.Key, c => MaterialFor(c.Key, c.Value));
            var primitives = new Dictionary<PrimitiveType, Mesh>();
            Mesh PrimitiveMesh(PrimitiveType type)
            {
                if (!primitives.TryGetValue(type, out var mesh))
                {
                    var temp = GameObject.CreatePrimitive(type);
                    mesh = temp.GetComponent<MeshFilter>().sharedMesh;
                    Object.DestroyImmediate(temp);
                    primitives[type] = mesh;
                }
                return mesh;
            }

            var definitions = new List<AccessoryDatabase.Definition>();
            foreach (var spec in Specs())
            {
                var colorOrder = spec.parts.Select(p => p.Color).Distinct().ToList();
                var perMaterial = colorOrder.Select(color =>
                {
                    var combine = spec.parts.Where(p => p.Color == color).Select(p => new CombineInstance
                    {
                        mesh = PrimitiveMesh(p.Type),
                        transform = Matrix4x4.TRS(p.Position, Quaternion.Euler(p.Rotation), p.Scale),
                    }).ToArray();
                    var m = new Mesh();
                    m.CombineMeshes(combine, true, true);
                    return m;
                }).ToList();

                var mesh = new Mesh { name = spec.id };
                mesh.CombineMeshes(perMaterial.Select(m => new CombineInstance { mesh = m, transform = Matrix4x4.identity }).ToArray(), false, false);
                mesh.RecalculateBounds();
                perMaterial.ForEach(Object.DestroyImmediate);
                var meshPath = $"{Root}/Meshes/{spec.id}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                if (existing != null)
                {
                    EditorUtility.CopySerialized(mesh, existing);
                    Object.DestroyImmediate(mesh);
                    mesh = existing;
                }
                else
                {
                    AssetDatabase.CreateAsset(mesh, meshPath);
                }

                var go = new GameObject(spec.id);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = colorOrder.Select(c => materials[c]).ToArray();
                var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{Root}/{spec.id}.prefab");
                Object.DestroyImmediate(go);

                definitions.Add(new AccessoryDatabase.Definition
                {
                    itemId = spec.id,
                    displayName = spec.name,
                    prefab = prefab,
                    allowedBones = spec.bones,
                    defaultPosition = spec.pos,
                    defaultRotation = spec.rot,
                    defaultScale = 1f,
                });
            }

            var db = AssetDatabase.LoadAssetAtPath<AccessoryDatabase>(DatabasePath);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<AccessoryDatabase>();
                AssetDatabase.CreateAsset(db, DatabasePath);
            }
            // 아이콘처럼 사람이 넣은 값은 같은 itemId면 유지합니다.
            foreach (var d in definitions)
                d.icon = db.Find(d.itemId)?.icon;
            db.SetItems(definitions);
            AssetDatabase.SaveAssets();

            ExportItemIds(db);
            return $"[AccessoryPlaceholderBuilder] 임시 장식 {definitions.Count}종: {string.Join(", ", definitions.Select(d => d.itemId))} → {DatabasePath}, {ItemIdsPath}";
        }

        [MenuItem("Tools/Character/Export Accessory IDs")]
        static void ExportMenu()
        {
            var db = AssetDatabase.LoadAssetAtPath<AccessoryDatabase>(DatabasePath);
            ExportItemIds(db);
            Debug.Log($"[AccessoryPlaceholderBuilder] {ItemIdsPath} 갱신");
        }

        /// <summary>서버(Server/contract.py)가 같은 목록으로 검사하도록 itemId와 본 목록을 내보냅니다.</summary>
        static void ExportItemIds(AccessoryDatabase db)
        {
            var export = new ItemIdExport
            {
                bones = db.AttachableBones.ToArray(),
                items = db.Items.Select(d => new ItemIdExport.Item { itemId = d.itemId, displayName = d.displayName, allowedBones = d.allowedBones }).ToArray(),
            };
            File.WriteAllText(ItemIdsPath, JsonUtility.ToJson(export, true) + "\n", new System.Text.UTF8Encoding(false));
        }

        [System.Serializable]
        class ItemIdExport
        {
            [System.Serializable]
            public class Item
            {
                public string itemId;
                public string displayName;
                public string[] allowedBones;
            }

            public string[] bones;
            public Item[] items;
        }

        static Material MaterialFor(string name, Color color)
        {
            var path = $"{Root}/Materials/Accessory_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Wonju/CharacterPaint"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color); // _BaseMap은 셰이더 기본값(흰색)
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
