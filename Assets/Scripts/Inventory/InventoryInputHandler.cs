using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace FarmingSystem.Inventory
{
    // 마우스 휠(도구 전환), 숫자키 1~9,0(핫바 10칸 선택), Tab(전체 인벤토리 토글)을
    // 매 프레임 직접 폴링한다. Input Actions 에셋을 거치지 않고 Mouse.current / Keyboard.current를
    // 바로 읽는 방식 - 단순한 입력은 이 방식이 Input Actions 연결 문제 없이 더 안정적이다.

    public class InventoryInputHandler : MonoBehaviour
    {
        private void Update()
        {
            HandleToolScroll();
            HandleHotbarNumberKeys();
            HandleInventoryToggle();
        }

        private void HandleToolScroll()
        {
            if (InventoryManager.Instance == null) return;
            if (Mouse.current == null) return;

            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) < 0.01f) return;

            int direction = scroll > 0 ? 1 : -1;
            InventoryManager.Instance.CycleTool(direction);
        }

        // 1,2,...,9,0 순서로 핫바 인덱스 0~9에 대응 (숫자키 0이 10번째 슬롯)</summary>
        private void HandleHotbarNumberKeys()
        {
            if (InventoryManager.Instance == null) return;
            if (Keyboard.current == null) return;

            int hotbarSize = InventoryManager.Instance.HotbarSize;

            for (int i = 0; i < hotbarSize && i < 10; i++)
            {
                int keyNumber = (i == 9) ? 0 : i + 1; // 9번 인덱스(10번째 슬롯)는 숫자키 0
                KeyControl key = GetDigitKey(keyNumber);
                if (key != null && key.wasPressedThisFrame)
                {
                    InventoryManager.Instance.SelectHotbarSlot(i);
                }
            }
        }

        private void HandleInventoryToggle()
        {
            if (InventoryManager.Instance == null) return;
            if (Keyboard.current == null) return;

            if (Keyboard.current.tabKey.wasPressedThisFrame)
            {
                InventoryManager.Instance.ToggleInventory();
            }
        }

        private KeyControl GetDigitKey(int number)
        {
            switch (number)
            {
                case 0: return Keyboard.current.digit0Key;
                case 1: return Keyboard.current.digit1Key;
                case 2: return Keyboard.current.digit2Key;
                case 3: return Keyboard.current.digit3Key;
                case 4: return Keyboard.current.digit4Key;
                case 5: return Keyboard.current.digit5Key;
                case 6: return Keyboard.current.digit6Key;
                case 7: return Keyboard.current.digit7Key;
                case 8: return Keyboard.current.digit8Key;
                case 9: return Keyboard.current.digit9Key;
                default: return null;
            }
        }
    }
}