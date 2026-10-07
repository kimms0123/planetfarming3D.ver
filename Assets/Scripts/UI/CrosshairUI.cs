using UnityEngine;
using UnityEngine.UI;
using FarmingSystem.Player;
using FarmingSystem.UI.Shop;

namespace FarmingSystem.UI
{
    /// <summary>
    /// 화면 가운데 십자선. 커서가 잠겨 있을 때(마우스로 화면을 돌리는 중)만 보이고,
    /// Alt·상점·인벤토리로 커서가 나타나면 숨는다.
    /// 지금 클릭/E로 뭔가 할 수 있는 대상을 가리키면 색이 바뀌고 살짝 커진다.
    /// </summary>
    public class CrosshairUI : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [Tooltip("색을 바꿀 십자선 조각들 (가로, 세로 막대 등)")]
        [SerializeField] private Graphic[] parts;

        [Header("상태 판단용 (선택)")]
        [SerializeField] private FarmActionController farmActionController;
        [SerializeField] private ShopInteractable[] interactables;

        [Header("모양")]
        [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] private Color activeColor = new Color(1f, 0.85f, 0.3f, 1f);
        [SerializeField] private float activeScale = 1.3f;
        [SerializeField] private float animationSharpness = 15f;

        private void Reset()
        {
            canvasGroup = GetComponent<CanvasGroup>();
            parts = GetComponentsInChildren<Graphic>();
        }

        private void LateUpdate()
        {
            bool visible = Cursor.lockState == CursorLockMode.Locked;
            if (canvasGroup != null) canvasGroup.alpha = visible ? 1f : 0f;
            if (!visible) return;

            bool active = IsPointingAtSomething();
            float t = 1f - Mathf.Exp(-animationSharpness * Time.unscaledDeltaTime);

            Vector3 targetScale = Vector3.one * (active ? activeScale : 1f);
            transform.localScale = Vector3.Lerp(transform.localScale, targetScale, t);

            Color targetColor = active ? activeColor : normalColor;
            foreach (Graphic part in parts)
                if (part != null) part.color = Color.Lerp(part.color, targetColor, t);
        }

        private bool IsPointingAtSomething()
        {
            if (interactables != null)
                foreach (ShopInteractable interactable in interactables)
                    if (interactable != null && interactable.CanInteract) return true;

            return farmActionController != null
                && farmActionController.HoveredInRange
                && farmActionController.HoveredAction != FarmActionType.None;
        }
    }
}