using UnityEngine;

namespace Wonju.CharacterShared
{
    /// <summary>부위별 채우기에 쓰는 부위 ID. 마스크 텍스처의 R 채널에 ID × MaskValueStep으로 저장됩니다.</summary>
    public enum CharacterPart : byte
    {
        None = 0,
        Head = 1,
        Torso = 2,
        LeftArm = 3,
        RightArm = 4,
        LeftLeg = 5,
        RightLeg = 6,
        LeftFoot = 7,
        RightFoot = 8,
    }

    public static class CharacterParts
    {
        public const int TextureSize = 1024;
        public const int MaskValueStep = 28;

        public static byte Encode(CharacterPart part) => (byte)((int)part * MaskValueStep);

        public static CharacterPart Decode(byte maskValue) =>
            (CharacterPart)Mathf.Clamp((maskValue + MaskValueStep / 2) / MaskValueStep, 0, (int)CharacterPart.RightFoot);

        /// <summary>본 이름으로 부위를 정합니다. 손은 팔에, 어깨는 몸통에 포함됩니다.</summary>
        public static CharacterPart FromBoneName(string bone)
        {
            bool left = bone.StartsWith("Left_");
            if (bone.Contains("Foot")) return left ? CharacterPart.LeftFoot : CharacterPart.RightFoot;
            if (bone.Contains("Leg")) return left ? CharacterPart.LeftLeg : CharacterPart.RightLeg;
            if (bone.Contains("Arm") || bone.Contains("Hand"))
                return left ? CharacterPart.LeftArm : CharacterPart.RightArm;
            if (bone == "Head" || bone == "Neck") return CharacterPart.Head;
            return CharacterPart.Torso; // Root, Hips, Spine, Chest, Shoulder
        }

        /// <summary>색칠이 늦는 학생용 기본 컬러.</summary>
        public static Color32 DefaultColor(CharacterPart part) => part switch
        {
            CharacterPart.Head => new Color32(255, 224, 196, 255),
            CharacterPart.Torso => new Color32(142, 201, 240, 255),
            CharacterPart.LeftArm or CharacterPart.RightArm => new Color32(255, 224, 196, 255),
            CharacterPart.LeftLeg or CharacterPart.RightLeg => new Color32(75, 101, 132, 255),
            CharacterPart.LeftFoot or CharacterPart.RightFoot => new Color32(61, 61, 61, 255),
            _ => new Color32(255, 255, 255, 255),
        };
    }
}
