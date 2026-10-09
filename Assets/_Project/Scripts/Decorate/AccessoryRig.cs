using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Wonju.CharacterShared;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 캐릭터에 달린 장식을 관리합니다: 달기, 고르기, 표면을 따라 옮기기, 돌리기·기울이기·크기, 처음 자리로, 빼기.
    /// 장식의 상태는 항상 규약 값(AccessoryEntry: 본 기준 pos·rot·scale)과 맞춰 두어 그대로 JSON으로 보냅니다.
    /// </summary>
    public class AccessoryRig : MonoBehaviour
    {
        public const float AngleStep = 15f;
        public const float ScaleStep = 0.1f;
        public const float MinScaleFactor = 0.5f;
        public const float MaxScaleFactor = 2f;

        // 장식을 끌어다 놓을 수 있는 본과, 그 본에 해당하는 캐릭터 부위 (부위 마스크로 판정)
        static readonly Dictionary<string, CharacterPart[]> BoneParts = new()
        {
            ["Head"] = new[] { CharacterPart.Head },
            ["Chest"] = new[] { CharacterPart.Torso },
            ["Hips"] = new[] { CharacterPart.Torso },
            ["Left_Hand_Socket"] = new[] { CharacterPart.LeftArm },
            ["Right_Hand_Socket"] = new[] { CharacterPart.RightArm },
        };

        static readonly string[] HandSockets = { "Right_Hand_Socket", "Left_Hand_Socket" };

        [SerializeField] AccessoryDatabase database;
        [SerializeField] Transform characterRoot;
        [SerializeField] PaintCanvas canvas;
        [Tooltip("스캔 장식을 처음 달 때 가장 긴 변의 길이 (m)")]
        [SerializeField] float scanDefaultLength = 0.15f;
        [Tooltip("양손이 다 찼을 때 스캔 장식을 다는 가슴 앞 자리 (Chest 기준)")]
        [SerializeField] Vector3 scanChestPosition = new(0f, 0.06f, 0.13f);

        public class Placed
        {
            public AccessoryEntry Entry;     // 지금 상태 (규약 값)
            public AccessoryEntry Origin;    // 처음 달았을 때 (처음 자리로, 크기 범위 기준)
            public string DisplayName;
            public string[] AllowedBones;
            public Transform Instance;
            public Collider Collider;
            public bool IsScan => Entry.IsScan;
        }

        /// <summary>되돌리기용 상태 하나 (장식 순서 그대로).</summary>
        public sealed class State
        {
            public readonly List<(AccessoryEntry entry, AccessoryEntry origin, string name)> Items = new();
        }

        /// <summary>스캔 장식의 원본(비활성 템플릿)과 이름을 찾습니다. 세션이 ScanLibrary로 채웁니다.</summary>
        public Func<string, (GameObject template, string name)?> ScanResolver;

        readonly List<Placed> items = new();
        Placed selected;

        public IReadOnlyList<Placed> Items => items;
        public Placed Selected => selected;
        public AccessoryDatabase Database => database;
        public int ScanCount => items.Count(p => p.IsScan);

        /// <summary>장식이 달리거나, 빠지거나, 바뀌거나, 선택이 바뀌면 호출됩니다.</summary>
        public event Action Changed;

        public bool CanAdd(bool scan) =>
            items.Count < CharacterContract.MaxAccessories && (!scan || ScanCount < CharacterContract.MaxScanAccessories);

        // ───────────────────────── 달기·빼기 ─────────────────────────

        public Placed AddBuiltin(AccessoryDatabase.Definition definition)
        {
            if (definition == null || !CanAdd(false)) return null;
            var entry = definition.CreateEntry();
            return Spawn(entry, entry.Clone(), definition.displayName, definition.allowedBones, definition.prefab);
        }

        /// <summary>스캔 장식을 빈 손에, 양손이 다 찼으면 가슴 앞에 답니다. 가장 긴 변이 scanDefaultLength가 되게 줄입니다.</summary>
        public Placed AddScan(string itemId, string file, string displayName, GameObject template)
        {
            if (template == null || !CanAdd(true)) return null;
            var bounds = template.GetComponent<MeshFilter>().sharedMesh.bounds;
            float longest = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            float scale = longest > 0f ? Mathf.Clamp(scanDefaultLength / longest, 0.1f, 1f) : 1f;

            string hand = HandSockets.FirstOrDefault(h => items.All(p => p.Entry.bone != h));
            var entry = new AccessoryEntry
            {
                itemId = itemId,
                source = AccessoryEntry.SourceScan,
                file = file,
                bone = hand ?? "Chest",
                pos = hand != null ? new[] { 0f, 0f, 0f } : new[] { scanChestPosition.x, scanChestPosition.y, scanChestPosition.z },
                rot = new[] { 0f, 0f, 0f },
                scale = (float)Math.Round(scale, 4),
            };
            return Spawn(entry, entry.Clone(), displayName, BoneParts.Keys.ToArray(), template);
        }

        public void Remove(Placed placed)
        {
            if (placed == null || !items.Remove(placed)) return;
            if (selected == placed) selected = null;
            Destroy(placed.Instance.gameObject);
            Changed?.Invoke();
        }

        public void Clear()
        {
            foreach (var p in items) Destroy(p.Instance.gameObject);
            items.Clear();
            selected = null;
            Changed?.Invoke();
        }

        public void Select(Placed placed)
        {
            if (selected == placed) return;
            selected = placed;
            Changed?.Invoke();
        }

        Placed Spawn(AccessoryEntry entry, AccessoryEntry origin, string displayName, string[] allowedBones, GameObject template)
        {
            var bone = CharacterContract.FindBone(characterRoot, entry.bone);
            if (bone == null || template == null)
            {
                Debug.LogWarning($"[AccessoryRig] 달 수 없음: {entry.itemId} → {entry.bone}");
                return null;
            }
            var instance = Instantiate(template).transform;
            instance.name = entry.itemId;
            instance.gameObject.SetActive(true);
            CharacterContract.Apply(entry, bone, instance);

            // 고르기용 충돌체 (프리팹에는 넣지 않으므로 여기서 붙입니다)
            var mesh = instance.GetComponent<MeshFilter>().sharedMesh;
            var box = instance.gameObject.AddComponent<BoxCollider>();
            box.center = mesh.bounds.center;
            box.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.02f);

            var placed = new Placed
            {
                Entry = entry,
                Origin = origin,
                DisplayName = displayName,
                AllowedBones = allowedBones,
                Instance = instance,
                Collider = box,
            };
            items.Add(placed);
            selected = placed;
            Changed?.Invoke();
            return placed;
        }

        // ───────────────────────── 옮기기 ─────────────────────────

        /// <summary>끄는 중: 표면의 hit 위치로 옮깁니다. 이 장식을 달 수 없는 부위면 false (자리는 그대로).</summary>
        public bool TryMoveTo(Placed placed, RaycastHit hit)
        {
            var part = canvas.PartAt(hit.textureCoord);
            Transform best = null;
            float bestDistance = float.MaxValue;
            foreach (var boneName in placed.AllowedBones)
            {
                if (!BoneParts.TryGetValue(boneName, out var parts) || Array.IndexOf(parts, part) < 0) continue;
                var bone = CharacterContract.FindBone(characterRoot, boneName);
                if (bone == null) continue;
                float d = Vector3.Distance(bone.position, hit.point);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = bone;
                }
            }
            if (best == null) return false;

            var instance = placed.Instance;
            bool socket = Array.IndexOf(HandSockets, best.name) >= 0;
            if (instance.parent != best)
            {
                instance.SetParent(best, true);
                // 손에 쥐는 장식은 손 방향 기준의 처음 회전을 씁니다 (양손 소켓은 같은 방향).
                if (socket && Array.IndexOf(HandSockets, placed.Origin.bone) >= 0)
                    instance.localRotation = Quaternion.Euler(placed.Origin.rot[0], placed.Origin.rot[1], placed.Origin.rot[2]);
            }
            if (socket) instance.localPosition = Vector3.zero;
            else instance.position = hit.point;
            Sync(placed, false);
            return true;
        }

        /// <summary>끌기를 마쳤을 때 호출합니다 (상태 변경 알림).</summary>
        public void CommitMove(Placed placed) => Sync(placed, true);

        // ───────────────────────── 패널 버튼 ─────────────────────────

        public void Rotate(Placed placed, float yawSteps, float tiltSteps)
        {
            if (placed == null) return;
            placed.Instance.localRotation *= Quaternion.Euler(tiltSteps * AngleStep, yawSteps * AngleStep, 0f);
            Sync(placed, true);
        }

        public void Scale(Placed placed, int steps)
        {
            if (placed == null) return;
            float origin = placed.Origin.scale;
            float scale = Mathf.Clamp(placed.Instance.localScale.x + steps * ScaleStep * origin, origin * MinScaleFactor, origin * MaxScaleFactor);
            placed.Instance.localScale = Vector3.one * scale;
            Sync(placed, true);
        }

        public bool CanScale(Placed placed, int steps)
        {
            if (placed == null) return false;
            float origin = placed.Origin.scale, current = placed.Instance.localScale.x;
            return steps > 0 ? current < origin * MaxScaleFactor - 1e-4f : current > origin * MinScaleFactor + 1e-4f;
        }

        public void ResetPlacement(Placed placed)
        {
            if (placed == null) return;
            var bone = CharacterContract.FindBone(characterRoot, placed.Origin.bone);
            CharacterContract.Apply(placed.Origin, bone, placed.Instance);
            Sync(placed, true);
        }

        void Sync(Placed placed, bool notify)
        {
            CharacterContract.Capture(placed.Instance, placed.Entry);
            if (notify) Changed?.Invoke();
        }

        // ───────────────────────── 내보내기·되돌리기 ─────────────────────────

        public List<AccessoryEntry> ToEntries() => items.Select(p => p.Entry.Clone()).ToList();

        public State Snapshot()
        {
            var state = new State();
            foreach (var p in items) state.Items.Add((p.Entry.Clone(), p.Origin.Clone(), p.DisplayName));
            return state;
        }

        /// <summary>저장한 상태로 되돌립니다. 템플릿을 찾을 수 없는 장식(지워진 스캔 등)은 건너뜁니다.</summary>
        public void Restore(State state)
        {
            foreach (var p in items) Destroy(p.Instance.gameObject);
            items.Clear();
            selected = null;
            foreach (var (entry, origin, name) in state.Items)
            {
                if (entry.IsScan)
                {
                    var scan = ScanResolver?.Invoke(entry.itemId);
                    if (scan.HasValue) Spawn(entry.Clone(), origin.Clone(), name, BoneParts.Keys.ToArray(), scan.Value.template);
                }
                else
                {
                    var definition = database.Find(entry.itemId);
                    if (definition != null) Spawn(entry.Clone(), origin.Clone(), name, definition.allowedBones, definition.prefab);
                }
            }
            selected = null;
            Changed?.Invoke();
        }

        /// <summary>화면 광선에 가장 먼저 맞는 장식.</summary>
        public Placed Pick(Ray ray, out float distance)
        {
            Placed best = null;
            distance = float.MaxValue;
            foreach (var p in items)
            {
                if (p.Collider.Raycast(ray, out var hit, 100f) && hit.distance < distance)
                {
                    distance = hit.distance;
                    best = p;
                }
            }
            return best;
        }
    }
}
