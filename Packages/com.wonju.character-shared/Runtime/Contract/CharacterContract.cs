using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Wonju.CharacterShared
{
    /// <summary>
    /// 캐릭터 데이터 규약의 검사·적용 기준 구현. 페인팅 툴과 Quest 앱이 같은 코드로 같은 결과를 내도록 공유합니다.
    /// Python 쪽 같은 규칙: Server/contract.py. 두 구현은 Contract/samples/expected.json으로 함께 검증합니다.
    /// </summary>
    public static class CharacterContract
    {
        public const int MaxAccessories = 5;
        public const int MaxScanAccessories = 2;
        public const int MaxNameLength = 8;
        public static readonly int[] TextureSizes = { 1024, 2048 };

        static readonly Regex ScanFilePattern = new(@"^scans/[A-Za-z0-9_-]{1,64}\.glb$");

        public enum IssueCode
        {
            BadId,
            BadName,
            BadTexture,
            UnknownItem,
            UnknownBone,
            BadVector,
            BadScale,
            BadScanFile,
            TooManyScans,
            TooManyAccessories,
        }

        /// <summary>문제 하나. index는 원래 accessories 배열에서의 위치(장식 문제가 아니면 -1).</summary>
        public readonly struct Issue
        {
            public readonly IssueCode Code;
            public readonly int Index;
            public readonly string Message;

            public Issue(IssueCode code, int index, string message)
            {
                Code = code;
                Index = index;
                Message = message;
            }

            public override string ToString() => Index >= 0 ? $"{Code} [{Index}] {Message}" : $"{Code} {Message}";
        }

        /// <summary>
        /// 규약대로 정리한 사본과 문제 목록을 돌려줍니다. 원본은 바꾸지 않습니다.
        /// 장식: ① 잘못된 항목 버림 ② 스캔은 앞에서 2개 ③ 전체는 앞에서 5개.
        /// isKnownItem은 기본 장식 ID만 판정합니다 (스캔 장식은 file 형식으로 판정).
        /// </summary>
        public static CharacterData Sanitize(CharacterData data, Func<string, bool> isKnownItem, Func<string, bool> isKnownBone, List<Issue> issues)
        {
            var result = new CharacterData
            {
                id = data.id,
                name = data.name ?? "",
                texture = data.texture ?? "",
            };

            if (data.id < 1)
                issues.Add(new Issue(IssueCode.BadId, -1, $"id는 1 이상이어야 합니다: {data.id}"));
            if (result.name.Length is 0 or > MaxNameLength)
                issues.Add(new Issue(IssueCode.BadName, -1, $"name은 1~{MaxNameLength}자여야 합니다: '{result.name}'"));
            if (result.texture != CharacterData.TextureFileName(data.id))
                issues.Add(new Issue(IssueCode.BadTexture, -1, $"texture는 '{CharacterData.TextureFileName(data.id)}'여야 합니다: '{result.texture}'"));

            var valid = new List<(int index, AccessoryEntry entry)>();
            var source = data.accessories ?? new List<AccessoryEntry>();
            for (int i = 0; i < source.Count; i++)
            {
                var a = source[i];
                if (a == null) continue;
                var problem = CheckEntry(a, isKnownItem, isKnownBone);
                if (problem.HasValue)
                {
                    issues.Add(new Issue(problem.Value.code, i, problem.Value.message));
                    continue;
                }
                valid.Add((i, a));
            }

            int scans = 0;
            foreach (var (index, entry) in valid)
            {
                if (entry.IsScan && ++scans > MaxScanAccessories)
                {
                    issues.Add(new Issue(IssueCode.TooManyScans, index, $"스캔 장식은 {MaxScanAccessories}개까지: {entry.itemId}"));
                    continue;
                }
                if (result.accessories.Count >= MaxAccessories)
                {
                    issues.Add(new Issue(IssueCode.TooManyAccessories, index, $"장식은 {MaxAccessories}개까지: {entry.itemId}"));
                    continue;
                }
                result.accessories.Add(Copy(entry));
            }
            return result;
        }

        static (IssueCode code, string message)? CheckEntry(AccessoryEntry a, Func<string, bool> isKnownItem, Func<string, bool> isKnownBone)
        {
            string source = string.IsNullOrEmpty(a.source) ? AccessoryEntry.SourceBuiltin : a.source;
            if (source == AccessoryEntry.SourceScan)
            {
                if (string.IsNullOrEmpty(a.itemId) || !a.itemId.StartsWith("scan_"))
                    return (IssueCode.UnknownItem, $"스캔 장식 itemId는 scan_으로 시작해야 합니다: '{a.itemId}'");
                if (a.file == null || !ScanFilePattern.IsMatch(a.file))
                    return (IssueCode.BadScanFile, $"스캔 file은 scans/*.glb여야 합니다: '{a.file}'");
            }
            else if (source != AccessoryEntry.SourceBuiltin || !isKnownItem(a.itemId ?? ""))
            {
                return (IssueCode.UnknownItem, $"알 수 없는 장식: '{a.itemId}' (source '{a.source}')");
            }
            if (!isKnownBone(a.bone ?? ""))
                return (IssueCode.UnknownBone, $"알 수 없는 본: '{a.bone}'");
            if (!IsVector3(a.pos) || !IsVector3(a.rot))
                return (IssueCode.BadVector, "pos·rot은 숫자 3개여야 합니다");
            if (!(a.scale > 0f) || float.IsInfinity(a.scale))
                return (IssueCode.BadScale, $"scale은 0보다 커야 합니다: {a.scale}");
            return null;
        }

        static bool IsVector3(float[] v) =>
            v != null && v.Length == 3 && !float.IsNaN(v[0] + v[1] + v[2]) && !float.IsInfinity(v[0] + v[1] + v[2]);

        static AccessoryEntry Copy(AccessoryEntry a) => new()
        {
            itemId = a.itemId,
            source = string.IsNullOrEmpty(a.source) ? AccessoryEntry.SourceBuiltin : a.source,
            file = a.IsScan ? a.file : "",
            bone = a.bone,
            pos = (float[])a.pos.Clone(),
            rot = (float[])a.rot.Clone(),
            scale = a.scale,
        };

        // ───────────── 배치 (두 앱이 같은 방식으로 해석하도록 여기서만 변환) ─────────────

        /// <summary>장식 인스턴스를 본의 자식으로 붙이고 규약의 로컬 값을 적용합니다.</summary>
        public static void Apply(AccessoryEntry entry, Transform bone, Transform instance)
        {
            instance.SetParent(bone, false);
            instance.localPosition = new Vector3(entry.pos[0], entry.pos[1], entry.pos[2]);
            instance.localRotation = Quaternion.Euler(entry.rot[0], entry.rot[1], entry.rot[2]);
            instance.localScale = Vector3.one * entry.scale;
        }

        /// <summary>본에 붙어 있는 장식의 현재 상태를 규약 값으로 읽습니다. 색칠 앱이 저장할 때 씁니다.</summary>
        public static void Capture(Transform instance, AccessoryEntry entry)
        {
            var bone = instance.parent;
            entry.bone = bone != null ? bone.name : "";
            var p = instance.localPosition;
            var r = instance.localEulerAngles;
            entry.pos = new[] { Round(p.x, 4), Round(p.y, 4), Round(p.z, 4) };
            entry.rot = new[] { Round(NormalizeAngle(r.x), 2), Round(NormalizeAngle(r.y), 2), Round(NormalizeAngle(r.z), 2) };
            entry.scale = Round(instance.localScale.x, 4);
        }

        /// <summary>리그에서 이름으로 본을 찾습니다 (Transform 이름 그대로, 대소문자 구분).</summary>
        public static Transform FindBone(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindBone(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        static float NormalizeAngle(float degrees) => degrees > 180f ? degrees - 360f : degrees;
        static float Round(float value, int digits) => (float)Math.Round(value, digits);
    }
}
