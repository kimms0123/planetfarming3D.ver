using UnityEngine;
using FarmingSystem.Farming;

namespace FarmingSystem.Player
{
    /// <summary>
    /// 지금 대상으로 잡힌 밭 타일 위에 "빛나는 테두리"를 띄운다.
    /// - 테두리 이미지는 코드가 직접 만들어서(둥근 사각형 + 바깥으로 퍼지는 빛) 별도 그림이 필요 없다
    /// - 숨쉬듯 밝기와 크기가 살짝 커졌다 작아진다
    /// - 다른 칸으로 옮기면 순간이동하지 않고 부드럽게 미끄러진다
    /// - 색으로 클릭 결과를 알려준다: 행동 가능 / 수확 가능 / 할 수 있는 것 없음 / 거리 밖
    /// - 색을 HDR로 밝게 주고 Global Volume에 Bloom을 켜면 실제로 빛 번짐이 생긴다
    ///
    /// 구조: 이 스크립트를 붙인 빈 오브젝트(회전 0) 아래에 X축 90도로 눕힌 Quad를 자식으로 두고,
    /// 그 Quad의 Renderer를 Highlight Renderer에 연결한다.
    /// </summary>
    public class TileHighlighter : MonoBehaviour
    {
        [SerializeField] private FarmActionController actionController;
        [Tooltip("자식 Quad의 MeshRenderer (Collider는 반드시 제거)")]
        [SerializeField] private Renderer highlightRenderer;
        [Tooltip("비워두면 코드가 빛나는 테두리 이미지를 자동으로 만든다. 디자이너 이미지가 생기면 여기에 넣기")]
        [SerializeField] private Texture2D overrideTexture;

        [Header("모양")]
        [Tooltip("타일 윗면에서 얼마나 띄울지 (바닥과 겹쳐 깜빡이는 것 방지)")]
        [SerializeField] private float heightOffset = 0.03f;
        [Tooltip("타일 크기 대비 테두리 크기 (1보다 크면 빛이 타일 밖으로 살짝 퍼짐)")]
        [SerializeField] private float sizeMultiplier = 1.15f;

        [Header("자동 생성 테두리 (Override Texture가 비어 있을 때)")]
        [SerializeField, Range(0.01f, 0.2f)] private float borderWidth = 0.07f;
        [SerializeField, Range(0.05f, 0.4f)] private float glowWidth = 0.22f;
        [SerializeField, Range(0f, 0.5f)] private float cornerRadius = 0.18f;
        [Tooltip("테두리 안쪽을 얼마나 은은하게 채울지 (0 = 테두리만)")]
        [SerializeField, Range(0f, 0.5f)] private float innerFill = 0.12f;

        [Header("움직임")]
        [Tooltip("숨쉬는 속도")]
        [SerializeField] private float pulseSpeed = 3f;
        [Tooltip("가장 어두울 때 밝기 비율")]
        [SerializeField, Range(0f, 1f)] private float pulseMinAlpha = 0.55f;
        [Tooltip("숨쉴 때 크기 변화량")]
        [SerializeField] private float pulseScale = 0.04f;
        [Tooltip("다른 칸으로 옮길 때 따라가는 빠르기 (클수록 빠름, 0이면 즉시 이동)")]
        [SerializeField] private float moveSharpness = 25f;

        [Header("색상 (HDR - 밝기 1 이상이면 Bloom으로 빛 번짐)")]
        [ColorUsage(true, true)] [SerializeField] private Color actionColor = new Color(1.6f, 1.6f, 1.5f, 0.9f);
        [ColorUsage(true, true)] [SerializeField] private Color harvestColor = new Color(2.0f, 1.6f, 0.4f, 0.9f);
        [ColorUsage(true, true)] [SerializeField] private Color noActionColor = new Color(1.6f, 0.45f, 0.4f, 0.7f);
        [ColorUsage(true, true)] [SerializeField] private Color outOfRangeColor = new Color(0.5f, 0.75f, 1.2f, 0.6f);
        [Tooltip("거리 밖 타일도 회색으로 표시할지 (Alt로 마우스 선택할 때만 해당)")]
        [SerializeField] private bool showOutOfRange = true;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

        private Material material;
        private Texture2D generatedTexture;

        private FarmTile lastTile;
        private TileState lastState;
        private bool lastHadCrop;

        private Vector3 targetPosition;
        private Vector3 targetScale = Vector3.one;
        private bool wasVisible = false;

        private void Awake()
        {
            if (highlightRenderer == null) return;

            material = highlightRenderer.material; // 이 오브젝트 전용 복사본
            highlightRenderer.enabled = false;

            Texture2D tex = overrideTexture;
            if (tex == null)
            {
                generatedTexture = CreateGlowTexture(128);
                tex = generatedTexture;
            }
            if (material.HasProperty(BaseMapId)) material.SetTexture(BaseMapId, tex);
            if (material.HasProperty(MainTexId)) material.SetTexture(MainTexId, tex);
        }

        private void OnDestroy()
        {
            if (generatedTexture != null) Destroy(generatedTexture);
        }

        private void LateUpdate()
        {
            if (actionController == null || highlightRenderer == null) return;

            FarmTile tile = actionController.HoveredTile;
            bool show = tile != null && (actionController.HoveredInRange || showOutOfRange);

            if (!show)
            {
                highlightRenderer.enabled = false;
                wasVisible = false;
                lastTile = null;
                return;
            }

            // 타일이 바뀌었거나, 같은 타일이라도 모델이 바뀌었으면(갈기/심기/수확) 목표 위치·크기 다시 계산
            bool hasCrop = tile.CurrentCrop != null;
            if (tile != lastTile || tile.State != lastState || hasCrop != lastHadCrop)
            {
                CalculateTarget(tile);
                lastTile = tile;
                lastState = tile.State;
                lastHadCrop = hasCrop;
            }

            // 처음 나타날 때는 바로 그 자리에, 이후엔 부드럽게 따라감
            if (!wasVisible || moveSharpness <= 0f)
            {
                transform.position = targetPosition;
            }
            else
            {
                float t = 1f - Mathf.Exp(-moveSharpness * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, targetPosition, t);
            }

            // 숨쉬기: 0~1 사이를 오가는 값
            float wave = (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f;
            transform.rotation = Quaternion.identity;
            transform.localScale = targetScale * (1f + pulseScale * wave);

            Color color = GetColor();
            color.a *= Mathf.Lerp(pulseMinAlpha, 1f, wave);
            SetColor(color);

            highlightRenderer.enabled = true;
            wasVisible = true;
        }

        private Color GetColor()
        {
            if (!actionController.HoveredInRange) return outOfRangeColor;

            switch (actionController.HoveredAction)
            {
                case FarmActionType.Harvest: return harvestColor;
                case FarmActionType.None: return noActionColor;
                default: return actionColor;
            }
        }

        private void CalculateTarget(FarmTile tile)
        {
            Bounds bounds = new Bounds(tile.transform.position, Vector3.one);
            bool found = false;

            foreach (Renderer r in tile.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r == highlightRenderer) continue;
                // 작물 모델은 제외 (작물 꼭대기에 테두리가 뜨지 않도록)
                if (tile.CurrentCrop != null && r.transform.IsChildOf(tile.CurrentCrop.transform)) continue;

                if (!found) { bounds = r.bounds; found = true; }
                else bounds.Encapsulate(r.bounds);
            }

            targetPosition = new Vector3(bounds.center.x, bounds.max.y + heightOffset, bounds.center.z);
            targetScale = new Vector3(bounds.size.x * sizeMultiplier, 1f, bounds.size.z * sizeMultiplier);
        }

        private void SetColor(Color color)
        {
            if (material == null) return;
            if (material.HasProperty(BaseColorId)) material.SetColor(BaseColorId, color);
            else if (material.HasProperty(ColorId)) material.SetColor(ColorId, color);
        }

        /// <summary>
        /// 둥근 사각형 테두리 + 바깥/안쪽으로 부드럽게 퍼지는 빛 + 은은한 안쪽 채움을 흰색 알파 이미지로 만든다.
        /// 색은 머티리얼 색으로 입히므로 이미지는 흰색이면 된다.
        /// </summary>
        private Texture2D CreateGlowTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TileGlow (generated)",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color32[size * size];
            float half = 1f - glowWidth;          // 테두리 선이 놓일 위치 (바깥 빛이 이미지 끝까지 퍼지도록 여유)
            float radius = Mathf.Min(cornerRadius, half);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // -1 ~ 1 좌표
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;

                    // 둥근 사각형까지의 부호 있는 거리 (음수 = 안쪽)
                    float qx = Mathf.Abs(u) - (half - radius);
                    float qy = Mathf.Abs(v) - (half - radius);
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
                    float inside = Mathf.Min(Mathf.Max(qx, qy), 0f);
                    float signedDistance = outside + inside - radius;

                    // 테두리 선: 선 두께 안은 1, 바깥으로 glowWidth에 걸쳐 부드럽게 사라짐
                    float distanceToLine = Mathf.Abs(signedDistance) - borderWidth * 0.5f;
                    float glow = 1f - Mathf.Clamp01(distanceToLine / glowWidth);
                    glow = glow * glow * glow;

                    float fill = signedDistance < 0f ? innerFill : 0f;
                    float alpha = Mathf.Max(glow, fill);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}