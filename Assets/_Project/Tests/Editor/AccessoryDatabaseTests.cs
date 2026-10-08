using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Wonju.CharacterShared;

namespace Wonju.Tests
{
    /// <summary>장식 DB가 규약 샘플·서버용 ID 목록·캐릭터 리그와 어긋나지 않는지 확인합니다.</summary>
    public class AccessoryDatabaseTests
    {
        const string DatabasePath = "Packages/com.wonju.character-shared/Runtime/AccessoryDatabase.asset";
        const string CharacterPrefab = "Packages/com.wonju.character-shared/Runtime/Prefabs/Character.prefab";

        [Serializable]
        class Known
        {
            public string[] knownItems;
            public string[] knownBones;
        }

        [Serializable]
        class ItemIds
        {
            [Serializable]
            public class Item
            {
                public string itemId;
                public string[] allowedBones;
            }

            public string[] bones;
            public Item[] items;
        }

        static AccessoryDatabase Db => AssetDatabase.LoadAssetAtPath<AccessoryDatabase>(DatabasePath);

        [Test]
        public void DatabaseMatchesContractSamples()
        {
            var known = JsonUtility.FromJson<Known>(File.ReadAllText("Contract/samples/expected.json"));
            CollectionAssert.AreEquivalent(known.knownItems, Db.Items.Select(d => d.itemId).ToArray(), "샘플의 knownItems와 DB의 itemId가 다릅니다");
            CollectionAssert.AreEquivalent(known.knownBones, Db.AttachableBones.ToArray(), "샘플의 knownBones와 DB의 본 목록이 다릅니다");
        }

        [Test]
        public void ExportedItemIdsMatchDatabase()
        {
            var export = JsonUtility.FromJson<ItemIds>(File.ReadAllText("Contract/item_ids.json"));
            CollectionAssert.AreEqual(Db.Items.Select(d => d.itemId).ToArray(), export.items.Select(i => i.itemId).ToArray(),
                "Contract/item_ids.json이 오래됐습니다. Tools > Character > Export Accessory IDs를 실행하세요.");
            CollectionAssert.AreEqual(Db.AttachableBones.ToArray(), export.bones);
        }

        [Test]
        public void EveryItemIsUsable()
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefab).transform;
            var ids = Db.Items.Select(d => d.itemId).ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "itemId가 겹칩니다");

            foreach (var d in Db.Items)
            {
                Assert.IsNotNull(d.prefab, $"{d.itemId}: 프리팹 없음");
                Assert.AreEqual(1, d.prefab.GetComponentsInChildren<Renderer>().Length, $"{d.itemId}: 렌더러는 1개여야 합니다 (Quest 그리기 호출)");
                Assert.IsEmpty(d.prefab.GetComponentsInChildren<Collider>(), $"{d.itemId}: 프리팹에 콜라이더를 넣지 않습니다");
                Assert.IsNotEmpty(d.allowedBones, $"{d.itemId}: 붙일 본 없음");
                foreach (var bone in d.allowedBones)
                {
                    Assert.IsTrue(Db.IsAttachableBone(bone), $"{d.itemId}: '{bone}'은 attachableBones에 없습니다");
                    Assert.IsNotNull(CharacterContract.FindBone(rig, bone), $"{d.itemId}: 리그에 '{bone}' 없음");
                }

                var issues = new System.Collections.Generic.List<CharacterContract.Issue>();
                var data = new CharacterData { id = 1, name = "시험", texture = "1.png" };
                data.accessories.Add(d.CreateEntry());
                Db.Sanitize(data, issues);
                Assert.IsEmpty(issues, $"{d.itemId}: 기본 배치가 규약 검사를 통과하지 못합니다\n{string.Join("\n", issues)}");
            }
        }
    }
}
