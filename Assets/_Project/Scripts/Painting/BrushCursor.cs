using UnityEngine;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 캐릭터 표면에 붙는 브러시 미리보기 원을 그립니다.
    /// PaintCanvas가 구운 메쉬를 BrushCursor 셰이더로 한 번 더 그리며, Show를 호출한 프레임에만 보입니다.
    /// </summary>
    public class BrushCursor : MonoBehaviour
    {
        public const int ModeBrush = 0;
        public const int ModeEraser = 1;
        public const int ModePicker = 2;

        static readonly int CenterId = Shader.PropertyToID("_CursorCenter");
        static readonly int NormalId = Shader.PropertyToID("_CursorNormal");
        static readonly int ColorId = Shader.PropertyToID("_CursorColor");
        static readonly int RadiusId = Shader.PropertyToID("_CursorRadius");
        static readonly int ModeId = Shader.PropertyToID("_CursorMode");

        [SerializeField] PaintCanvas canvas;
        [SerializeField] Shader shader;

        Material material;
        bool visible;

        void Awake() => material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        void OnDestroy() => Destroy(material);

        /// <summary>월드 좌표 center에 반지름 radius의 원을 보여 줍니다. color는 감마 색입니다.</summary>
        public void Show(Vector3 center, Vector3 normal, float radius, int mode, Color color)
        {
            material.SetVector(CenterId, center);
            material.SetVector(NormalId, normal.normalized);
            material.SetVector(ColorId, color.linear);
            material.SetFloat(RadiusId, radius);
            material.SetFloat(ModeId, mode);
            visible = true;
        }

        public void Hide() => visible = false;

        void LateUpdate()
        {
            if (!visible || canvas.BakedMesh == null) return;
            var space = canvas.MeshSpace;
            var renderParams = new RenderParams(material) { layer = space.gameObject.layer };
            var matrix = space.localToWorldMatrix;
            for (int i = 0; i < canvas.BakedMesh.subMeshCount; i++)
                Graphics.RenderMesh(renderParams, canvas.BakedMesh, i, matrix);
        }
    }
}
