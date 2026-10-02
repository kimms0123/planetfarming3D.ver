using UnityEngine;
using System.Collections.Generic;
using FarmingSystem.Economy;

namespace FarmingSystem.UI.Shop
{
    /// <summary>
    /// "살 때" 창. ShopManager.BuyCatalog을 기준으로 슬롯을 동적으로 생성해서 보여준다.
    /// </summary>
    public class ShopBuyPanelUI : MonoBehaviour
    {
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Transform slotContainer;
        [SerializeField] private ShopBuySlotUI slotPrefab;

        private readonly List<ShopBuySlotUI> spawnedSlots = new List<ShopBuySlotUI>();

        public void Open()
        {
            if (panelRoot != null) panelRoot.SetActive(true);
            Refresh();
            Debug.Log("[ShopBuyPanelUI] 구매 창 열림");
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void Refresh()
        {
            if (ShopManager.Instance == null || slotContainer == null || slotPrefab == null) return;

            foreach (var slot in spawnedSlots)
                Destroy(slot.gameObject);
            spawnedSlots.Clear();

            var catalog = ShopManager.Instance.BuyCatalog;
            for (int i = 0; i < catalog.Count; i++)
            {
                ShopBuySlotUI slot = Instantiate(slotPrefab, slotContainer);
                slot.SetEntry(i, catalog[i]);
                spawnedSlots.Add(slot);
            }

            Debug.Log($"[ShopBuyPanelUI] 구매 목록 {catalog.Count}개 표시");
        }
    }
}