using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FarmingSystem.Inventory;

namespace FarmingSystem.UI
{
    /// <summary>
    /// 슬롯 1칸의 표시(아이콘, 수량, 선택 상태)를 담당.
    /// 핫바와 전체 인벤토리 창 둘 다 이 컴포넌트를 그대로 재사용한다.
    /// 선택 표시는 두 가지 방식 중 하나로 동작한다:
    /// 1) normalSlotSprite/selectedSlotSprite가 있으면 배경 스프라이트 자체를 교체 (지금 UI 에셋처럼
    ///    "일반 슬롯"과 "선택된 슬롯"이 서로 다른 그림인 경우)
    /// 2) 없으면 selectedHighlight 오브젝트를 켜고 끄는 방식 (테두리 오버레이 등)
    /// </summary>
    public class ItemSlotUI : MonoBehaviour
    {
        [Header("UI 요소")]
        [SerializeField] private Image background;
        [SerializeField] private Image itemIcon;
        [SerializeField] private TextMeshProUGUI quantityText;
        [SerializeField] private GameObject selectedHighlight; // 방식 2번용 (선택 사항)

        [Header("배경 스프라이트 교체 방식 (선택 사항)")]
        [SerializeField] private Sprite normalSlotSprite;
        [SerializeField] private Sprite selectedSlotSprite;

        [Header("등급 별 (상급·최상급 작물일 때 칸 오른쪽 위)")]
        [Tooltip("비워 두면 처음 필요할 때 자동으로 만들어 붙임")]
        [SerializeField] private Image qualityStar;
        [SerializeField] private Color perfectStarColor = Color.white;                  // Icon_Star 원래 색 (노랑)
        [SerializeField] private Color goodStarColor = new Color(0.75f, 0.85f, 1f, 1f); // 은색 느낌 (가방 창과 같은 색)
        private static Sprite fallbackStar;

        public void SetItem(Sprite icon, int quantity)
        {
            if (itemIcon != null)
            {
                itemIcon.enabled = icon != null;
                itemIcon.sprite = icon;
            }

            if (quantityText != null)
            {
                bool showQuantity = icon != null && quantity > 1;
                quantityText.gameObject.SetActive(showQuantity);
                quantityText.text = quantity.ToString();
            }
        }

        // 등급 별 표시 (가방 창 InventorySlotView와 같은 규칙: 상급 = 은색 별, 최상급 = 노란 별)
        public void SetQuality(ItemQuality quality, bool hasItem, Sprite starSprite)
        {
            bool show = hasItem && (quality == ItemQuality.Perfect || quality == ItemQuality.Good);
            if (!show && qualityStar == null) return;
            if (qualityStar == null) qualityStar = CreateStar();

            qualityStar.gameObject.SetActive(show);
            if (!show) return;
            Sprite sprite = starSprite != null ? starSprite : FallbackStar();
            if (qualityStar.sprite != sprite) qualityStar.sprite = sprite;
            qualityStar.color = quality == ItemQuality.Perfect
                ? (starSprite != null ? perfectStarColor : new Color(1f, 0.82f, 0.25f, 1f))
                : goodStarColor;
        }

        private Image CreateStar()
        {
            var go = new GameObject("QualityStar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            go.transform.SetAsLastSibling(); // 아이콘 위에 보이게
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            float size = Mathf.Max(18f, ((RectTransform)transform).rect.width * 0.3f);
            rt.sizeDelta = new Vector2(size, size);
            rt.anchoredPosition = new Vector2(-4f, -4f);
            var img = go.GetComponent<Image>();
            img.preserveAspect = true;
            img.raycastTarget = false;
            return img;
        }

        // 별 스프라이트를 안 넣었을 때 쓰는 기본 별 (코드로 그림)
        private static Sprite FallbackStar()
        {
            if (fallbackStar != null) return fallbackStar;
            const int N = 64, SS = 4;
            var tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pts = new Vector2[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.PI / 2f + i * Mathf.PI / 5f;
                float r = (i % 2 == 0 ? 0.48f : 0.2f) * N;
                pts[i] = new Vector2(N / 2f + Mathf.Cos(a) * r, N / 2f + Mathf.Sin(a) * r - 1.5f);
            }
            var px = new Color32[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int inside = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                            if (InPoly(pts, new Vector2(x + (sx + 0.5f) / SS, y + (sy + 0.5f) / SS))) inside++;
                    px[y * N + x] = new Color32(255, 255, 255, (byte)(255 * inside / (SS * SS)));
                }
            tex.SetPixels32(px);
            tex.Apply();
            fallbackStar = Sprite.Create(tex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f), 100f);
            return fallbackStar;
        }

        private static bool InPoly(Vector2[] p, Vector2 q)
        {
            bool c = false;
            for (int i = 0, j = p.Length - 1; i < p.Length; j = i++)
                if ((p[i].y > q.y) != (p[j].y > q.y) && q.x < (p[j].x - p[i].x) * (q.y - p[i].y) / (p[j].y - p[i].y) + p[i].x) c = !c;
            return c;
        }

        public void SetSelected(bool isSelected)
        {
            if (selectedHighlight != null)
                selectedHighlight.SetActive(isSelected);

            if (background != null && normalSlotSprite != null && selectedSlotSprite != null)
                background.sprite = isSelected ? selectedSlotSprite : normalSlotSprite;
        }
    }
}