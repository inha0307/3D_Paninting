using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Wonju.CharacterShared;

namespace Wonju.PaintApp
{
    /// <summary>
    /// 캐릭터의 색칠 텍스처를 관리합니다.
    /// canvas에 획을 그리고, 바뀐 프레임마다 한 번 여백을 채운(dilate) 결과를 display로 만들어 캐릭터에 입힙니다.
    /// 색 인자는 모두 UI에서 쓰는 감마(sRGB) 색이며, 셰이더에는 선형으로 바꿔 넘깁니다.
    /// </summary>
    public class PaintCanvas : MonoBehaviour
    {
        static readonly int BrushStartId = Shader.PropertyToID("_BrushStart");
        static readonly int BrushEndId = Shader.PropertyToID("_BrushEnd");
        static readonly int BrushNormalId = Shader.PropertyToID("_BrushNormal");
        static readonly int BrushColorId = Shader.PropertyToID("_BrushColor");
        static readonly int BrushRadiusId = Shader.PropertyToID("_BrushRadius");
        static readonly int BrushHardnessId = Shader.PropertyToID("_BrushHardness");
        static readonly int EraseModeId = Shader.PropertyToID("_EraseMode");
        static readonly int BaseTexId = Shader.PropertyToID("_BaseTex");
        static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        static readonly int PartIdId = Shader.PropertyToID("_PartId");
        static readonly int PartMaskId = Shader.PropertyToID("_PartMask");
        static readonly int MaskValueStepId = Shader.PropertyToID("_MaskValueStep");
        static readonly int CoverageTexId = Shader.PropertyToID("_CoverageTex");
        static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
        static readonly int HighlightPartId = Shader.PropertyToID("_HighlightPart");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        const int BrushPass = 0;
        const int CoveragePass = 1;
        const int PartFillPass = 2;

        [SerializeField] SkinnedMeshRenderer target;
        [Tooltip("처음 상태이자 지우개가 되돌리는 텍스처. 비우면 흰색.")]
        [SerializeField] Texture baseTexture;
        [Tooltip("공유 패키지의 PartMask (Read/Write 켜짐)")]
        [SerializeField] Texture2D partMask;
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
        Texture2D pixelReader;
        CharacterPart highlightPart;
        bool dirty;

        /// <summary>획이 그려지는 원본. 내보내기·되돌리기는 이 텍스처를 씁니다.</summary>
        public RenderTexture Canvas => canvas;
        /// <summary>여백을 채운 표시용 텍스처.</summary>
        public RenderTexture Display => display;
        public Collider Collider => meshCollider;
        /// <summary>바인드 포즈로 구운 메쉬. 정점은 MeshSpace 기준입니다.</summary>
        public Mesh BakedMesh => bakedMesh;
        public Transform MeshSpace => target.transform;
        public Texture BaseTexture => baseTexture != null ? baseTexture : Texture2D.whiteTexture;

        /// <summary>표시용 텍스처에서 밝게 강조할 부위. None이면 강조하지 않습니다.</summary>
        public CharacterPart HighlightPart
        {
            get => highlightPart;
            set
            {
                if (highlightPart == value) return;
                highlightPart = value;
                dirty = true;
            }
        }

        void Awake()
        {
            int size = CharacterParts.TextureSize;
            canvas = CreateTexture("PaintCanvas", size, GraphicsFormat.R8G8B8A8_SRGB, false);
            display = CreateTexture("PaintDisplay", size, GraphicsFormat.R8G8B8A8_SRGB, true);
            coverage = CreateTexture("PaintCoverage", size, GraphicsFormat.R8_UNorm, false);
            pixelReader = new Texture2D(1, 1, TextureFormat.RGBA32, false);

            brushMaterial = new Material(brushShader) { hideFlags = HideFlags.HideAndDontSave };
            dilateMaterial = new Material(dilateShader) { hideFlags = HideFlags.HideAndDontSave };
            brushMaterial.SetFloat(MaskValueStepId, CharacterParts.MaskValueStep);
            brushMaterial.SetTexture(PartMaskId, partMask);
            dilateMaterial.SetFloat(MaskValueStepId, CharacterParts.MaskValueStep);
            dilateMaterial.SetTexture(MaskTexId, partMask);
            commands = new CommandBuffer { name = "PaintCanvas" };

            // 페인팅 앱에서는 캐릭터가 움직이지 않으므로 바인드 포즈를 한 번 구워 칠하기와 충돌 판정에 씁니다.
            bakedMesh = new Mesh { name = "PaintBakedMesh" };
            target.BakeMesh(bakedMesh, false);
            meshCollider = target.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = bakedMesh;

            RenderCoverage();
            ResetCanvas();

            var properties = new MaterialPropertyBlock();
            target.GetPropertyBlock(properties);
            properties.SetTexture(BaseMapId, display);
            target.SetPropertyBlock(properties);
        }

        void OnDestroy()
        {
            if (target != null) target.SetPropertyBlock(null);
            if (meshCollider != null) Destroy(meshCollider);
            Release(canvas);
            Release(display);
            Release(coverage);
            Destroy(pixelReader);
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
            dilateMaterial.SetFloat(HighlightPartId, (float)highlightPart);
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
            => DrawSegment(startWorld, endWorld, normalWorld, color, radiusWorld, false);

        /// <summary>월드 좌표 두 점을 잇는 획을 처음 상태로 되돌립니다.</summary>
        public void EraseSegment(Vector3 startWorld, Vector3 endWorld, Vector3 normalWorld, float radiusWorld)
            => DrawSegment(startWorld, endWorld, normalWorld, Color.white, radiusWorld, true);

        /// <summary>부위 전체(이음새 여백 포함)를 한 색으로 칠합니다.</summary>
        public void FillPart(CharacterPart part, Color color)
        {
            if (part == CharacterPart.None || partMask == null) return;
            brushMaterial.SetVector(FillColorId, color.linear);
            brushMaterial.SetFloat(PartIdId, (float)part);
            Graphics.Blit(partMask, canvas, brushMaterial, PartFillPass);
            dirty = true;
        }

        /// <summary>UV 위치의 부위. 레이캐스트의 textureCoord를 넘깁니다.</summary>
        public CharacterPart PartAt(Vector2 uv)
        {
            if (partMask == null) return CharacterPart.None;
            int x = Mathf.Clamp((int)(uv.x * partMask.width), 0, partMask.width - 1);
            int y = Mathf.Clamp((int)(uv.y * partMask.height), 0, partMask.height - 1);
            return CharacterParts.Decode((byte)Mathf.RoundToInt(partMask.GetPixel(x, y).r * 255f));
        }

        /// <summary>UV 위치의 현재 색(감마). 스포이드에서 클릭할 때 한 번 읽습니다.</summary>
        public Color SampleColor(Vector2 uv)
        {
            int x = Mathf.Clamp((int)(uv.x * canvas.width), 0, canvas.width - 1);
            int y = Mathf.Clamp((int)(uv.y * canvas.height), 0, canvas.height - 1);
            var previous = RenderTexture.active;
            RenderTexture.active = canvas;
            pixelReader.ReadPixels(new Rect(x, y, 1, 1), 0, 0, false);
            pixelReader.Apply(false);
            RenderTexture.active = previous;
            var c = pixelReader.GetPixel(0, 0);
            return new Color(c.r, c.g, c.b, 1f);
        }

        /// <summary>
        /// 업로드용 PNG. 이음새 여백을 채운 결과(강조 표시는 빼고)를 sRGB 1024px PNG로 만듭니다.
        /// Quest 앱이 같은 셰이더로 입히면 색칠 화면과 같은 모습이 됩니다.
        /// </summary>
        public byte[] EncodePng()
        {
            var export = RenderTexture.GetTemporary(canvas.descriptor);
            dilateMaterial.SetTexture(CoverageTexId, coverage);
            dilateMaterial.SetInt(RadiusId, dilatePixels);
            dilateMaterial.SetFloat(HighlightPartId, 0f);
            Graphics.Blit(canvas, export, dilateMaterial, 0);
            dirty = true; // 강조 값을 바꿨으므로 표시용 텍스처를 다시 만듭니다.

            var pixels = new Texture2D(canvas.width, canvas.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = export;
            pixels.ReadPixels(new Rect(0, 0, canvas.width, canvas.height), 0, 0, false);
            pixels.Apply(false);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(export);

            var png = pixels.EncodeToPNG();
            Destroy(pixels);
            return png;
        }

        /// <summary>display를 다시 만들도록 표시합니다. 캔버스를 외부에서 바꾼 뒤(되돌리기 등) 호출합니다.</summary>
        public void MarkDirty() => dirty = true;

        void DrawSegment(Vector3 startWorld, Vector3 endWorld, Vector3 normalWorld, Color color, float radiusWorld, bool erase)
        {
            var space = target.transform;
            float scale = Mathf.Abs(space.lossyScale.x);
            brushMaterial.SetVector(BrushStartId, space.InverseTransformPoint(startWorld));
            brushMaterial.SetVector(BrushEndId, space.InverseTransformPoint(endWorld));
            brushMaterial.SetVector(BrushNormalId, space.InverseTransformDirection(normalWorld).normalized);
            brushMaterial.SetVector(BrushColorId, color.linear);
            brushMaterial.SetFloat(BrushRadiusId, radiusWorld / scale);
            brushMaterial.SetFloat(BrushHardnessId, hardness);
            brushMaterial.SetFloat(EraseModeId, erase ? 1f : 0f);
            brushMaterial.SetTexture(BaseTexId, BaseTexture);

            commands.Clear();
            commands.SetRenderTarget(canvas);
            DrawMeshInUVSpace(BrushPass);
            Graphics.ExecuteCommandBuffer(commands);
            dirty = true;
        }

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
