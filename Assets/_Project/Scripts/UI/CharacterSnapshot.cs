using UnityEngine;

namespace Wonju.PaintApp.UI
{
    /// <summary>
    /// 완료 확인 창에 보여 줄 캐릭터 앞·뒤 사진을 찍습니다.
    /// URP에서 새로 만든 카메라는 Render()로 그려지지 않을 수 있어, 메인 카메라의 위치·출력 대상을 잠시 바꿔 찍고 되돌립니다.
    /// 헤드라이트도 촬영 방향으로 잠시 돌립니다.
    /// </summary>
    public static class CharacterSnapshot
    {
        const float FieldOfView = 30f;

        public static void Capture(Camera cam, Renderer target, Light headlight, float yaw, RenderTexture output)
        {
            var transform = cam.transform;
            var position = transform.position;
            var rotation = transform.rotation;
            var targetTexture = cam.targetTexture;
            float fov = cam.fieldOfView;
            var lightRotation = headlight != null ? headlight.transform.rotation : Quaternion.identity;

            var bounds = target.bounds;
            var view = Quaternion.Euler(5f, yaw, 0f);
            float halfHeight = bounds.extents.y * 1.15f;
            float distance = halfHeight / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad) + bounds.extents.z;

            transform.SetPositionAndRotation(bounds.center - view * Vector3.forward * distance, view);
            cam.fieldOfView = FieldOfView;
            cam.targetTexture = output;
            cam.aspect = (float)output.width / output.height;
            if (headlight != null) headlight.transform.rotation = view * Quaternion.Euler(20f, 20f, 0f);

            cam.Render();

            cam.targetTexture = targetTexture;
            cam.ResetAspect();
            cam.fieldOfView = fov;
            transform.SetPositionAndRotation(position, rotation);
            if (headlight != null) headlight.transform.rotation = lightRotation;
        }
    }
}
