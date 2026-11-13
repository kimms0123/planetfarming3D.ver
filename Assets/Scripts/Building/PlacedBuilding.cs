using System.Collections.Generic;
using UnityEngine;

namespace FarmingSystem.Building
{
    // 맵에 놓인 건물 하나
    // - 공사 중: 울타리 + 표지판이 둘러진 공사장 (동숲처럼). 공사 기간이 지나면 아침에 건물로 바뀜
    // - 완성: 건물 모델 + 칸 크기만큼의 충돌 상자 (그 자리는 걸어서 통과하거나 밭을 갈 수 없음)
    public class PlacedBuilding : MonoBehaviour
    {
        public enum BuildState { UnderConstruction, Complete }

        public BuildingData Data { get; private set; }
        public IReadOnlyList<Vector2Int> Cells => cells;
        public BuildState State { get; private set; }
        public int CompleteDay { get; private set; }
        public int Rotation { get; private set; }

        private readonly List<Vector2Int> cells = new List<Vector2Int>();
        private BuildingSystem system;
        private GameObject site;
        private GameObject model;

        public void Setup(BuildingSystem owner, BuildingData data, List<Vector2Int> footprintCells, int rotation, int today)
        {
            system = owner;
            Data = data;
            Rotation = rotation;
            cells.Clear();
            cells.AddRange(footprintCells);

            if (data.buildDays <= 0) Complete();
            else
            {
                State = BuildState.UnderConstruction;
                CompleteDay = today + data.buildDays;
                BuildSite();
            }
        }

        /// <summary>저장 불러오기: 공사 중인 건물의 완성 날짜를 저장된 값으로 되돌림</summary>
        public void RestoreCompleteDay(int day)
        {
            if (State == BuildState.UnderConstruction) CompleteDay = day;
        }

        public void OnNewDay(int day)
        {
            if (State == BuildState.UnderConstruction && day >= CompleteDay) Complete();
        }

        [ContextMenu("디버그: 바로 완성")]
        public void Complete()
        {
            if (site != null) Destroy(site);
            if (model != null) Destroy(model);
            State = BuildState.Complete;
            model = BuildingSystem.SpawnFitted(Data, transform, system.CellSize, Rotation);
            AddBlockCollider(model != null ? model : gameObject, 4f, true);
            Debug.Log($"[건축] {Data.displayName} 완성!");
        }

        // 칸 크기 상자 (건물 높이만큼). 모델 자체 충돌체가 없어도 막히게
        private void AddBlockCollider(GameObject target, float fallbackHeight, bool measureModel)
        {
            Vector2Int fp = BuildingSystem.RotatedFootprint(Data, Rotation);
            float c = system.CellSize;
            float h = fallbackHeight;
            if (measureModel && BuildingSystem.TryGetBounds(target.transform, out Bounds b)) h = Mathf.Max(1f, b.size.y);

            Transform old = transform.Find("BlockCollider"); // 공사장 때 만든 것 지우기
            if (old != null) Destroy(old.gameObject);

            var box = new GameObject("BlockCollider");
            box.transform.SetParent(transform, false);
            box.layer = system.BlockerLayer;
            BoxCollider col = box.AddComponent<BoxCollider>();
            col.size = new Vector3(fp.x * c * 0.96f, h, fp.y * c * 0.96f);
            col.center = new Vector3(0f, h * 0.5f, 0f);
        }

        // ---------- 공사장 ----------

        private void BuildSite()
        {
            Vector2Int fp = BuildingSystem.RotatedFootprint(Data, Rotation);
            float c = system.CellSize;
            float sx = fp.x * c * 0.92f, sz = fp.y * c * 0.92f;

            if (system.constructionSitePrefab != null)
            {
                site = new GameObject("ConstructionSite");
                site.transform.SetParent(transform, false);
                Instantiate(system.constructionSitePrefab, site.transform, false);
                BuildingSystem.FitToArea(site.transform, transform.position, sx, sz);
            }
            else
            {
                site = new GameObject("ConstructionSite");
                site.transform.SetParent(transform, false);
                BuildFence(site.transform, sx, sz);
            }
            AddBlockCollider(site, 1.2f, false);
        }

        // 기본 공사장: 모서리 기둥 + 가로대 두 줄 + 앞쪽 표지판
        private void BuildFence(Transform parent, float sx, float sz)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f;
            float postH = 1.1f, postW = 0.12f;

            int postsX = Mathf.Max(2, Mathf.RoundToInt(sx / 1.6f) + 1);
            int postsZ = Mathf.Max(2, Mathf.RoundToInt(sz / 1.6f) + 1);
            for (int i = 0; i < postsX; i++)
            {
                float x = Mathf.Lerp(-hx, hx, i / (float)(postsX - 1));
                Part(parent, new Vector3(x, postH * 0.5f, -hz), new Vector3(postW, postH, postW), system.fenceMaterial);
                Part(parent, new Vector3(x, postH * 0.5f, hz), new Vector3(postW, postH, postW), system.fenceMaterial);
            }
            for (int i = 1; i < postsZ - 1; i++)
            {
                float z = Mathf.Lerp(-hz, hz, i / (float)(postsZ - 1));
                Part(parent, new Vector3(-hx, postH * 0.5f, z), new Vector3(postW, postH, postW), system.fenceMaterial);
                Part(parent, new Vector3(hx, postH * 0.5f, z), new Vector3(postW, postH, postW), system.fenceMaterial);
            }
            foreach (float y in new[] { 0.45f, 0.85f })
            {
                Part(parent, new Vector3(0f, y, -hz), new Vector3(sx, 0.06f, 0.05f), system.fenceMaterial);
                Part(parent, new Vector3(0f, y, hz), new Vector3(sx, 0.06f, 0.05f), system.fenceMaterial);
                Part(parent, new Vector3(-hx, y, 0f), new Vector3(0.05f, 0.06f, sz), system.fenceMaterial);
                Part(parent, new Vector3(hx, y, 0f), new Vector3(0.05f, 0.06f, sz), system.fenceMaterial);
            }
            // 표지판 (앞쪽 = -Z 쪽 가운데)
            Part(parent, new Vector3(0f, 0.6f, -hz - 0.15f), new Vector3(0.08f, 1.2f, 0.08f), system.fenceMaterial);
            Part(parent, new Vector3(0f, 1.25f, -hz - 0.2f), new Vector3(0.9f, 0.6f, 0.06f), system.signMaterial);
            // 공사 중인 땅 표시 (흙 더미)
            Part(parent, new Vector3(0f, 0.05f, 0f), new Vector3(sx * 0.9f, 0.1f, sz * 0.9f), system.fenceMaterial);
        }

        private static void Part(Transform parent, Vector3 localPos, Vector3 size, Material mat)
        {
            GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Collider col = g.GetComponent<Collider>();
            if (col != null) Destroy(col);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = size;
            if (mat != null) g.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}