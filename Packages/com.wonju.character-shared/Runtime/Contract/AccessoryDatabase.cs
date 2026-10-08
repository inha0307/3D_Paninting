using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wonju.CharacterShared
{
    /// <summary>
    /// 기본 장식 목록. 페인팅 툴과 Quest 앱이 같은 에셋을 써서 itemId → 프리팹을 똑같이 해석합니다.
    /// 프리팹 루트(원점)가 본에 붙는 지점이며, default 값은 색칠 앱에서 처음 붙일 때의 자리입니다.
    /// </summary>
    [CreateAssetMenu(menuName = "Wonju/Accessory Database", fileName = "AccessoryDatabase")]
    public class AccessoryDatabase : ScriptableObject
    {
        [Serializable]
        public class Definition
        {
            public string itemId;
            public string displayName;
            public GameObject prefab;
            public Sprite icon;
            [Tooltip("색칠 앱에서 이 장식을 붙일 수 있는 본. 첫 번째가 처음 붙는 본입니다.")]
            public string[] allowedBones = Array.Empty<string>();
            public Vector3 defaultPosition;
            public Vector3 defaultRotation;
            public float defaultScale = 1f;

            public string DefaultBone => allowedBones.Length > 0 ? allowedBones[0] : "";

            /// <summary>처음 붙일 때의 규약 항목.</summary>
            public AccessoryEntry CreateEntry() => new()
            {
                itemId = itemId,
                source = AccessoryEntry.SourceBuiltin,
                bone = DefaultBone,
                pos = new[] { defaultPosition.x, defaultPosition.y, defaultPosition.z },
                rot = new[] { defaultRotation.x, defaultRotation.y, defaultRotation.z },
                scale = defaultScale,
            };
        }

        [Tooltip("장식을 붙일 수 있는 본 전체 (규약의 bone 검사에 씁니다)")]
        [SerializeField] string[] attachableBones = { "Head", "Chest", "Hips", "Left_Hand_Socket", "Right_Hand_Socket" };
        [SerializeField] List<Definition> items = new();

        public IReadOnlyList<Definition> Items => items;
        public IReadOnlyList<string> AttachableBones => attachableBones;

        public Definition Find(string itemId) => items.Find(d => d.itemId == itemId);
        public bool IsKnownItem(string itemId) => Find(itemId) != null;
        public bool IsAttachableBone(string bone) => Array.IndexOf(attachableBones, bone) >= 0;

        /// <summary>규약 검사. 기본 장식은 이 목록으로, 본은 attachableBones로 판정합니다.</summary>
        public CharacterData Sanitize(CharacterData data, List<CharacterContract.Issue> issues) =>
            CharacterContract.Sanitize(data, IsKnownItem, IsAttachableBone, issues);

#if UNITY_EDITOR
        /// <summary>에디터 도구용: 목록을 통째로 바꿉니다.</summary>
        public void SetItems(IEnumerable<Definition> definitions)
        {
            items = new List<Definition>(definitions);
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
