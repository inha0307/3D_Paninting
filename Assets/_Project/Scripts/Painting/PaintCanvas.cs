using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Wonju.CharacterShared;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 캐릭터의 색칠 텍스처를 관리합니다.
    /// canvas에 획을 그리고, 프레임마다 한 번 여백을 채운(dilate) 결과를 display로 만들어 캐릭터에 입힙니다.
    /// </summary>
    public class PaintCanvas : MonoBehaviour
    {
        static readonly int BrushStartId = Shader.PropertyToID("_BrushStart");
        static readonly int BrushEndId = Shader.PropertyToID("_BrushEnd");
        static readonly int BrushNormalId = Shader.PropertyToID("_BrushNormal");
        static readonly int BrushColorId = Shader.PropertyToID("_BrushColor");
        static readonly int BrushRadiusId = Shader.PropertyToID("_BrushRadius");
        static readonly int BrushHardnessId = Shader.PropertyToID("_BrushHardness");
        static readonly int CoverageTexId = Shader.PropertyToID("_CoverageTex");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        const int BrushPass = 0;
        const int CoveragePass = 1;

        [SerializeField] SkinnedMeshRenderer target;
        [Tooltip("처음 상태이자 지우개가 되돌리는 텍스처. 비우면 흰색.")]
        [SerializeField] Texture baseTexture;
        [SerializeField] Shader brushShader;
        [SerializeField] Shader dilateShader;
        [Range(0, 4)]
        [SerializeField] int dilatePixels = 4;
        [Range(0f, 1f)]
        [SerializeField] float hardness = 0.8f;

        Mesh bakedMesh;
        MeshCollider meshCollider;
        RenderTexture canvas, display, coverage;
        Material brushMaterial, dilateMaterial;
        CommandBuffer commands;
        MaterialPropertyBlock rendererProperties;
        bool dirty;

        /// <summary>획이 그려지는 원본. 내보내기·되돌리기는 이 텍스처를 씁니다.</summary>
        public RenderTexture Canvas => canvas;
        /// <summary>여백을 채운 표시용 텍스처.</summary>
        public RenderTexture Display => display;
        public Collider Collider => meshCollider;
        public Texture BaseTexture => baseTexture != null ? baseTexture : Texture2D.whiteTexture;

        void Awake()
        {
            int size = CharacterParts.TextureSize;
            canvas = CreateTexture("PaintCanvas", size, GraphicsFormat.R8G8B8A8_SRGB, false);
            display = CreateTexture("PaintDisplay", size, GraphicsFormat.R8G8B8A8_SRGB, true);
            coverage = CreateTexture("PaintCoverage", size, GraphicsFormat.R8_UNorm, false);

            brushMaterial = new Material(brushShader) { hideFlags = HideFlags.HideAndDontSave };
            dilateMaterial = new Material(dilateShader) { hideFlags = HideFlags.HideAndDontSave };
            commands = new CommandBuffer { name = "PaintCanvas" };

            // 페인팅 앱에서는 캐릭터가 움직이지 않으므로 바인드 포즈를 한 번 구워 칠하기와 충돌 판정에 씁니다.
            bakedMesh = new Mesh { name = "PaintBakedMesh" };
            target.BakeMesh(bakedMesh, false);
            meshCollider = target.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = bakedMesh;

            RenderCoverage();
            ResetCanvas();

            rendererProperties = new MaterialPropertyBlock();
            target.GetPropertyBlock(rendererProperties);
            rendererProperties.SetTexture(BaseMapId, display);
            target.SetPropertyBlock(rendererProperties);
        }

        void OnDestroy()
        {
            if (target != null) target.SetPropertyBlock(null);
            if (meshCollider != null) Destroy(meshCollider);
            Release(canvas);
            Release(display);
            Release(coverage);
            Destroy(brushMaterial);
            Destroy(dilateMaterial);
            Destroy(bakedMesh);
            commands?.Release();
        }

        void LateUpdate()
        {
            if (!dirty) return;
            dirty = false;
            dilateMaterial.SetTexture(CoverageTexId, coverage);
            dilateMaterial.SetInt(RadiusId, dilatePixels);
            Graphics.Blit(canvas, display, dilateMaterial, 0);
        }

        /// <summary>캔버스를 처음 상태(baseTexture)로 되돌립니다.</summary>
        public void ResetCanvas()
        {
            Graphics.Blit(BaseTexture, canvas);
            dirty = true;
        }

        /// <summary>월드 좌표 두 점을 잇는 획을 칠합니다. 시작점과 끝점이 같으면 점 하나를 찍습니다.</summary>
        public void PaintSegment(Vector3 startWorld, Vector3 endWorld, Vector3 normalWorld, Color color, float radiusWorld)
        {
            var space = target.transform;
            float scale = Mathf.Abs(space.lossyScale.x);
            brushMaterial.SetVector(BrushStartId, space.InverseTransformPoint(startWorld));
            brushMaterial.SetVector(BrushEndId, space.InverseTransformPoint(endWorld));
            brushMaterial.SetVector(BrushNormalId, space.InverseTransformDirection(normalWorld).normalized);
            brushMaterial.SetColor(BrushColorId, color);
            brushMaterial.SetFloat(BrushRadiusId, radiusWorld / scale);
            brushMaterial.SetFloat(BrushHardnessId, hardness);

            commands.Clear();
            commands.SetRenderTarget(canvas);
            DrawMeshInUVSpace(BrushPass);
            Graphics.ExecuteCommandBuffer(commands);
            dirty = true;
        }

        /// <summary>display를 다시 만들도록 표시합니다. 캔버스를 외부에서 바꾼 뒤(되돌리기 등) 호출합니다.</summary>
        public void MarkDirty() => dirty = true;

        void RenderCoverage()
        {
            commands.Clear();
            commands.SetRenderTarget(coverage);
            commands.ClearRenderTarget(false, true, Color.clear);
            DrawMeshInUVSpace(CoveragePass);
            Graphics.ExecuteCommandBuffer(commands);
        }

        void DrawMeshInUVSpace(int pass)
        {
            for (int i = 0; i < bakedMesh.subMeshCount; i++)
                commands.DrawMesh(bakedMesh, Matrix4x4.identity, brushMaterial, i, pass);
        }

        static RenderTexture CreateTexture(string name, int size, GraphicsFormat format, bool mips)
        {
            var desc = new RenderTextureDescriptor(size, size, format, GraphicsFormat.None)
            {
                useMipMap = mips,
                autoGenerateMips = mips,
            };
            var rt = new RenderTexture(desc)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = mips ? 4 : 0,
            };
            rt.Create();
            return rt;
        }

        static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
        }
    }
}
