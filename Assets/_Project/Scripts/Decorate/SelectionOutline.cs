using UnityEngine;

namespace Wonju.PaintApp
{
    /// <summary>선택한 장식에 테두리를 그립니다. 놓을 수 있으면 파랑, 놓을 수 없는 자리로 끌면 빨강.</summary>
    public class SelectionOutline : MonoBehaviour
    {
        static readonly int ColorId = Shader.PropertyToID("_OutlineColor");
        static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");

        [SerializeField] AccessoryRig rig;
        [SerializeField] Shader shader;
        [SerializeField] float width = 0.004f;
        [SerializeField] Color validColor = new(0.18f, 0.44f, 0.93f);
        [SerializeField] Color invalidColor = new(0.85f, 0.23f, 0.23f);

        Material material;

        /// <summary>끄는 중 놓을 수 없는 자리면 true (빨간 테두리).</summary>
        public bool Invalid { get; set; }

        void Awake() => material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };

        void OnDestroy() => Destroy(material);

        void LateUpdate()
        {
            var selected = rig.Selected;
            if (selected == null || selected.Instance == null) return;
            var filter = selected.Instance.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;

            material.SetVector(ColorId, (Invalid ? invalidColor : validColor).linear);
            material.SetFloat(WidthId, width);
            var renderParams = new RenderParams(material) { layer = selected.Instance.gameObject.layer };
            var matrix = selected.Instance.localToWorldMatrix;
            for (int i = 0; i < filter.sharedMesh.subMeshCount; i++)
                Graphics.RenderMesh(renderParams, filter.sharedMesh, i, matrix);
        }
    }
}
