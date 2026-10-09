using System;
using System.Collections.Generic;

namespace Wonju.CharacterShared
{
    /// <summary>{id}.json 한 개. 규약은 저장소의 Contract/CONTRACT.md를 따릅니다.</summary>
    [Serializable]
    public class CharacterData
    {
        public int id;
        public string name = "";
        public string texture = "";
        public List<AccessoryEntry> accessories = new();

        public static string TextureFileName(int id) => $"{id}.png";
        public static string JsonFileName(int id) => $"{id}.json";
    }

    /// <summary>장식 하나. pos·rot·scale은 bone 기준 로컬 값입니다.</summary>
    [Serializable]
    public class AccessoryEntry
    {
        public const string SourceBuiltin = "builtin";
        public const string SourceScan = "scan";

        public string itemId = "";
        public string source = SourceBuiltin;   // JSON에 없으면 builtin
        public string file = "";                // 스캔 장식만: scans/{파일}.glb
        public string bone = "";
        public float[] pos = { 0f, 0f, 0f };    // m
        public float[] rot = { 0f, 0f, 0f };    // 도, Quaternion.Euler(x, y, z)
        public float scale = 1f;

        public bool IsScan => source == SourceScan;

        public AccessoryEntry Clone() => new()
        {
            itemId = itemId,
            source = source,
            file = file,
            bone = bone,
            pos = (float[])pos?.Clone(),
            rot = (float[])rot?.Clone(),
            scale = scale,
        };
    }
}
