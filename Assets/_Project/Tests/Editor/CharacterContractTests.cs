using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Wonju.CharacterShared;

namespace Wonju.Tests
{
    /// <summary>
    /// Contract/samples/expected.json으로 C# 규약 구현을 검증합니다. Python(Server/tests/test_contract.py)도 같은 파일을 씁니다.
    /// </summary>
    public class CharacterContractTests
    {
        const string SamplesFolder = "Contract/samples";
        const string CharacterPrefab = "Packages/com.wonju.character-shared/Runtime/Prefabs/Character.prefab";

        [Serializable]
        class Expected
        {
            public string[] knownItems;
            public string[] knownBones;
            public Case[] cases;
        }

        [Serializable]
        class Case
        {
            public string file;
            public string[] kept;
            public ExpectedIssue[] issues;
        }

        [Serializable]
        class ExpectedIssue
        {
            public string code;
            public int index;
        }

        static Expected LoadExpected() =>
            JsonUtility.FromJson<Expected>(File.ReadAllText(Path.Combine(SamplesFolder, "expected.json")));

        static IEnumerable<string> SampleFiles() => LoadExpected().cases.Select(c => c.file);

        [TestCaseSource(nameof(SampleFiles))]
        public void SampleMatchesExpected(string file)
        {
            var expected = LoadExpected();
            var testCase = expected.cases.Single(c => c.file == file);
            var data = JsonUtility.FromJson<CharacterData>(File.ReadAllText(Path.Combine(SamplesFolder, file)));
            var issues = new List<CharacterContract.Issue>();

            var result = CharacterContract.Sanitize(data, expected.knownItems.Contains, expected.knownBones.Contains, issues);

            CollectionAssert.AreEqual(testCase.kept, result.accessories.Select(a => a.itemId).ToArray());
            CollectionAssert.AreEqual(
                testCase.issues.Select(i => $"{i.code}@{i.index}").ToArray(),
                issues.Select(i => $"{i.Code}@{i.Index}").ToArray(),
                string.Join("\n", issues));
        }

        [TestCaseSource(nameof(SampleFiles))]
        public void SanitizedOutputSurvivesJsonRoundTrip(string file)
        {
            var expected = LoadExpected();
            var data = JsonUtility.FromJson<CharacterData>(File.ReadAllText(Path.Combine(SamplesFolder, file)));
            var once = CharacterContract.Sanitize(data, expected.knownItems.Contains, expected.knownBones.Contains, new List<CharacterContract.Issue>());

            var reloaded = JsonUtility.FromJson<CharacterData>(JsonUtility.ToJson(once));
            var issues = new List<CharacterContract.Issue>();
            var twice = CharacterContract.Sanitize(reloaded, expected.knownItems.Contains, expected.knownBones.Contains, issues);

            Assert.IsEmpty(issues.Where(i => i.Index >= 0), string.Join("\n", issues));
            Assert.AreEqual(JsonUtility.ToJson(once), JsonUtility.ToJson(twice));
        }

        [Test]
        public void SourceDefaultsToBuiltinWhenOmitted()
        {
            var data = JsonUtility.FromJson<CharacterData>(
                "{\"id\":3,\"name\":\"a\",\"texture\":\"3.png\",\"accessories\":[{\"itemId\":\"hat_01\",\"bone\":\"Head\",\"pos\":[0,0,0],\"rot\":[0,0,0],\"scale\":1}]}");
            Assert.AreEqual(AccessoryEntry.SourceBuiltin, data.accessories[0].source);
        }

        [TestCase(0f, 0.12f, 0f, 0f, 0f, 0f, 1f)]
        [TestCase(0.03f, -0.02f, 0.11f, 10f, -45f, 30f, 0.8f)]
        [TestCase(0f, 0f, 0.05f, 0f, 170f, -90f, 1.5f)]
        public void ApplyThenCaptureGivesSameValues(float px, float py, float pz, float rx, float ry, float rz, float scale)
        {
            var root = new GameObject("Root");
            var bone = new GameObject("Head").transform;
            bone.SetParent(root.transform, false);
            bone.localRotation = Quaternion.Euler(270f, 180f, 0f); // FBX 본처럼 회전된 부모
            var item = new GameObject("Item").transform;
            try
            {
                var entry = new AccessoryEntry { itemId = "hat_01", bone = "Head", pos = new[] { px, py, pz }, rot = new[] { rx, ry, rz }, scale = scale };
                CharacterContract.Apply(entry, bone, item);

                var captured = new AccessoryEntry();
                CharacterContract.Capture(item, captured);

                Assert.AreEqual("Head", captured.bone);
                Assert.That(Vector3.Distance(V(entry.pos), V(captured.pos)), Is.LessThan(0.001f), "위치 1mm 이내");
                float angle = Quaternion.Angle(Quaternion.Euler(V(entry.rot)), Quaternion.Euler(V(captured.rot)));
                Assert.That(angle, Is.LessThan(1f), "각도 1도 이내");
                Assert.AreEqual(scale, captured.scale, 1e-4f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);   // 본에 붙은 장식도 함께 지워짐
                if (item != null) UnityEngine.Object.DestroyImmediate(item.gameObject);
            }
        }

        [Test]
        public void CharacterRigHasAllProposedBones()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterPrefab);
            Assert.IsNotNull(prefab, CharacterPrefab);
            foreach (var bone in LoadExpected().knownBones)
                Assert.IsNotNull(CharacterContract.FindBone(prefab.transform, bone), $"리그에 본 '{bone}'이 없습니다");
        }

        static Vector3 V(float[] v) => new(v[0], v[1], v[2]);
    }
}
