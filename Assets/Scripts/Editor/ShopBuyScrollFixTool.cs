#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using FarmingSystem.UI.Shop;

namespace FarmingSystem.EditorTools
{
    // 구매 창 씨앗 칸이 많아지면 드래그·휠로 아래로 내려갈 수 있게 (스크롤 검사 + 고치기)
    // 메뉴: Tools → 행성농사일지 → 상점 → 구매 창 스크롤 고치기
    // ShopBuyPanelUI의 Slot Container(칸이 생기는 곳)를 기준으로:
    //  - 위쪽에 ScrollRect가 없으면: 그 자리에 스크롤 칸을 만들고 Slot Container를 안에 넣음 (크기·위치 그대로)
    //  - 있으면: 세로 스크롤 켜기, Content 연결, 칸 수만큼 Content 높이가 늘어나게(ContentSizeFitter),
    //            칸 밖은 잘라내기(RectMask2D), 빈 곳을 잡고 드래그해도 되게(투명 Image) 확인
    // 바꾼 내용은 끝나고 창으로 알려 줌. Ctrl+Z로 되돌리기 가능
    public static class ShopBuyScrollFixTool
    {
        [MenuItem("Tools/행성농사일지/상점/구매 창 스크롤 고치기")]
        private static void Fix()
        {
            ShopBuyPanelUI buy = Object.FindAnyObjectByType<ShopBuyPanelUI>(FindObjectsInactive.Include);
            if (buy == null) { EditorUtility.DisplayDialog("구매 창 스크롤", "씬에서 ShopBuyPanelUI를 못 찾았어요.", "확인"); return; }
            RectTransform content = new SerializedObject(buy).FindProperty("slotContainer").objectReferenceValue as RectTransform;
            if (content == null) { EditorUtility.DisplayDialog("구매 창 스크롤", "ShopBuyPanelUI의 Slot Container가 비어 있어요.", "확인"); return; }

            var log = new StringBuilder();
            Undo.SetCurrentGroupName("구매 창 스크롤 고치기");
            int group = Undo.GetCurrentGroup();

            ScrollRect scroll = content.GetComponentInParent<ScrollRect>(true);
            if (scroll == null)
            {
                scroll = WrapInScroll(content);
                log.AppendLine("• 스크롤 칸이 없어서 새로 만듦 (BuyScroll) → Slot Container를 그 안에 넣음");
            }
            else log.AppendLine($"• 스크롤 칸 있음: {Path(scroll.transform)}");

            Undo.RecordObject(scroll, "스크롤");
            if (scroll.content != content && !content.IsChildOf(scroll.content != null ? scroll.content : scroll.transform))
            {
                scroll.content = content;
                log.AppendLine("• ScrollRect의 Content를 Slot Container로 연결");
            }
            if (scroll.content == null) { scroll.content = content; log.AppendLine("• ScrollRect의 Content가 비어 있어서 Slot Container로 연결"); }
            if (!scroll.vertical) { scroll.vertical = true; log.AppendLine("• 세로 스크롤(Vertical) 켬"); }
            if (scroll.horizontal) { scroll.horizontal = false; log.AppendLine("• 가로 스크롤 끔"); }
            if (scroll.movementType == ScrollRect.MovementType.Unrestricted) { scroll.movementType = ScrollRect.MovementType.Clamped; log.AppendLine("• 끝에서 멈추게 (Clamped)"); }
            if (scroll.scrollSensitivity < 10f) { scroll.scrollSensitivity = 30f; log.AppendLine("• 마우스 휠 속도 30"); }

            // 보이는 칸(Viewport): 밖은 잘라내고, 빈 곳을 잡아도 드래그되게
            RectTransform viewport = scroll.viewport != null ? scroll.viewport : (RectTransform)scroll.transform;
            if (scroll.viewport == null) { scroll.viewport = viewport; log.AppendLine("• Viewport 연결"); }
            if (viewport.GetComponent<RectMask2D>() == null && viewport.GetComponent<Mask>() == null)
            {
                Undo.AddComponent<RectMask2D>(viewport.gameObject);
                log.AppendLine("• 칸 밖 잘라내기(RectMask2D) 추가");
            }
            Graphic vg = viewport.GetComponent<Graphic>();
            if (vg == null)
            {
                Image img = Undo.AddComponent<Image>(viewport.gameObject);
                img.color = new Color(1f, 1f, 1f, 0f);
                img.raycastTarget = true;
                log.AppendLine("• 빈 곳 드래그용 투명 Image 추가");
            }
            else if (!vg.raycastTarget)
            {
                Undo.RecordObject(vg, "스크롤");
                vg.raycastTarget = true;
                log.AppendLine($"• {vg.name}의 Raycast Target 켬 (빈 곳 드래그)");
            }

            // Content: 위에 붙고, 칸 수만큼 높이가 늘어나게
            Undo.RecordObject(content, "스크롤");
            if (content.anchorMin.y < 0.99f || content.anchorMax.y < 0.99f || Mathf.Abs(content.pivot.y - 1f) > 0.01f)
            {
                float w = content.rect.width;
                bool stretchX = content.anchorMin.x < 0.01f && content.anchorMax.x > 0.99f;
                content.anchorMin = new Vector2(stretchX ? 0f : 0.5f, 1f);
                content.anchorMax = new Vector2(stretchX ? 1f : 0.5f, 1f);
                content.pivot = new Vector2(0.5f, 1f);
                content.anchoredPosition = Vector2.zero;
                if (!stretchX) content.sizeDelta = new Vector2(w, content.sizeDelta.y);
                log.AppendLine("• Content를 위쪽에 붙임 (위에서부터 칸이 쌓이게)");
            }
            if (content.GetComponent<LayoutGroup>() == null)
                log.AppendLine("⚠ Slot Container에 Grid Layout Group이 없어요. 칸이 겹치면 Grid Layout Group을 붙여 주세요");
            ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
            if (fitter == null)
            {
                fitter = Undo.AddComponent<ContentSizeFitter>(content.gameObject);
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                log.AppendLine("• 칸 수만큼 높이가 늘어나게 (Content Size Fitter) 추가 ← 보통 이게 없어서 안 내려감");
            }
            else if (fitter.verticalFit != ContentSizeFitter.FitMode.PreferredSize)
            {
                Undo.RecordObject(fitter, "스크롤");
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                log.AppendLine("• Content Size Fitter의 Vertical Fit = Preferred Size");
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(buy.gameObject.scene);
            Selection.activeGameObject = scroll.gameObject;
            string msg = log.ToString();
            Debug.Log("[구매 창 스크롤]\n" + msg);
            EditorUtility.DisplayDialog("구매 창 스크롤", msg + "\nCtrl+S로 씬 저장 후 플레이해서 드래그·휠로 내려 보세요.", "확인");
        }

        // Slot Container 자리에 스크롤 칸을 만들고 그 안에 넣기 (보이는 크기·위치는 그대로)
        private static ScrollRect WrapInScroll(RectTransform content)
        {
            Transform parent = content.parent;
            var go = new GameObject("BuyScroll", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "스크롤");
            RectTransform rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.SetSiblingIndex(content.GetSiblingIndex());
            rt.anchorMin = content.anchorMin;
            rt.anchorMax = content.anchorMax;
            rt.pivot = content.pivot;
            rt.anchoredPosition = content.anchoredPosition;
            rt.sizeDelta = content.sizeDelta;
            rt.localScale = Vector3.one;

            // 부모가 레이아웃으로 크기를 정하고 있었으면 같은 크기를 받게
            LayoutElement le = content.GetComponent<LayoutElement>();
            if (le != null)
            {
                LayoutElement nle = go.AddComponent<LayoutElement>();
                EditorUtility.CopySerialized(le, nle);
            }

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(1f, 1f, 1f, 0f);
            bg.raycastTarget = true;
            go.AddComponent<RectMask2D>();

            Undo.SetTransformParent(content, rt, "스크롤");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, content.sizeDelta.y);

            ScrollRect s = go.AddComponent<ScrollRect>();
            s.content = content;
            s.viewport = rt;
            s.horizontal = false;
            s.vertical = true;
            s.movementType = ScrollRect.MovementType.Clamped;
            s.scrollSensitivity = 30f;
            return s;
        }

        private static string Path(Transform t)
        {
            string p = t.name;
            while (t.parent != null && p.Length < 100) { t = t.parent; p = t.name + "/" + p; }
            return p;
        }
    }
}
#endif