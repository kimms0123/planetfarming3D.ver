using System;
using System.Collections.Generic;
using UnityEngine;

namespace FarmingSystem.World
{
    // 맵 자동 생성에 쓰는 알고리즘 모음 (모두 그리드 기반, 결과가 시드로 재현됨)
    // - Fbm                : 프랙탈 펄린 노이즈 (해안선 울퉁불퉁함, 언덕, 강 굽이침)
    // - SmoothCellular     : 셀룰러 오토마타 (자잘한 땅/물 점을 없애 해안선을 매끄럽게)
    // - KeepLargestRegion  : 플러드 필 (떨어진 작은 섬 제거, 본섬 하나만 남김)
    // - DistanceField      : 다중 시작점 BFS 거리장 (해안에서 몇 칸 떨어졌는지 등)
    // - FindPath           : A* 경로 탐색 (길, 강 경로)
    // - PoissonDisk        : 포아송 디스크 샘플링 (나무·바위를 뭉치지 않게 자연스럽게 흩뿌림)
    public static class MapAlgorithms
    {
        // 4방향 이웃 (북, 동, 남, 서) - 인덱스가 방향 번호로도 쓰임
        public static readonly Vector2Int[] Dirs4 =
        {
            new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0)
        };

        // 방향 번호 → Y축 회전 각도 (북 = 0도, 동 = 90도, 남 = 180도, 서 = 270도)
        public static float DirToYaw(int dir) => dir * 90f;

        // 두 칸 사이의 방향 번호 (인접한 칸이 아니면 -1)
        public static int DirBetween(Vector2Int from, Vector2Int to)
        {
            Vector2Int d = to - from;
            for (int i = 0; i < 4; i++)
                if (Dirs4[i] == d) return i;
            return -1;
        }

        // ---------- 노이즈 ----------

        // 프랙탈 브라운 운동(fBm): 크기가 다른 펄린 노이즈를 여러 겹 더해 자연스러운 지형 패턴을 만든다
        // 반환값은 대략 0~1
        public static float Fbm(float x, float z, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amplitude = 1f, frequency = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * Mathf.PerlinNoise(x * frequency, z * frequency);
                norm += amplitude;
                amplitude *= gain;
                frequency *= lacunarity;
            }
            return sum / norm;
        }

        // ---------- 셀룰러 오토마타 ----------

        // 주변 8칸 중 땅이 5칸 이상이면 땅, 3칸 이하면 물로 바꾸는 규칙을 여러 번 반복
        // forced가 true인 칸은 항상 땅으로 유지 (지역이 깎여 나가지 않도록)
        public static void SmoothCellular(bool[,] grid, int passes, bool[,] forced)
        {
            int w = grid.GetLength(0), h = grid.GetLength(1);
            for (int pass = 0; pass < passes; pass++)
            {
                bool[,] next = (bool[,])grid.Clone();
                for (int x = 0; x < w; x++)
                {
                    for (int z = 0; z < h; z++)
                    {
                        if (forced != null && forced[x, z]) { next[x, z] = true; continue; }

                        int count = 0;
                        for (int dx = -1; dx <= 1; dx++)
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                if (dx == 0 && dz == 0) continue;
                                int nx = x + dx, nz = z + dz;
                                if (nx >= 0 && nz >= 0 && nx < w && nz < h && grid[nx, nz]) count++;
                            }

                        if (count >= 5) next[x, z] = true;
                        else if (count <= 3) next[x, z] = false;
                    }
                }
                Array.Copy(next, grid, next.Length);
            }
        }

        // ---------- 플러드 필 ----------

        // 4방향으로 이어진 땅 덩어리 중 가장 큰 것만 남기고 나머지는 물로 바꾼다
        public static void KeepLargestRegion(bool[,] grid)
        {
            int w = grid.GetLength(0), h = grid.GetLength(1);
            int[,] label = new int[w, h];
            int bestLabel = 0, bestSize = 0, nextLabel = 0;
            var queue = new Queue<Vector2Int>();

            for (int x = 0; x < w; x++)
            {
                for (int z = 0; z < h; z++)
                {
                    if (!grid[x, z] || label[x, z] != 0) continue;

                    // 새 덩어리 발견 → BFS로 크기 측정
                    nextLabel++;
                    int size = 0;
                    label[x, z] = nextLabel;
                    queue.Enqueue(new Vector2Int(x, z));
                    while (queue.Count > 0)
                    {
                        Vector2Int c = queue.Dequeue();
                        size++;
                        foreach (Vector2Int d in Dirs4)
                        {
                            Vector2Int n = c + d;
                            if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= h) continue;
                            if (!grid[n.x, n.y] || label[n.x, n.y] != 0) continue;
                            label[n.x, n.y] = nextLabel;
                            queue.Enqueue(n);
                        }
                    }
                    if (size > bestSize) { bestSize = size; bestLabel = nextLabel; }
                }
            }

            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                    if (grid[x, z] && label[x, z] != bestLabel) grid[x, z] = false;
        }

        // ---------- 거리장 ----------

        // isSource가 true인 칸들로부터 4방향 BFS 거리 (시작 칸 = 0, 닿지 않는 칸 = int.MaxValue)
        public static int[,] DistanceField(int w, int h, Func<int, int, bool> isSource)
        {
            int[,] dist = new int[w, h];
            var queue = new Queue<Vector2Int>();
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                {
                    if (isSource(x, z)) { dist[x, z] = 0; queue.Enqueue(new Vector2Int(x, z)); }
                    else dist[x, z] = int.MaxValue;
                }

            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                foreach (Vector2Int d in Dirs4)
                {
                    Vector2Int n = c + d;
                    if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= h) continue;
                    if (dist[n.x, n.y] <= dist[c.x, c.y] + 1) continue;
                    dist[n.x, n.y] = dist[c.x, c.y] + 1;
                    queue.Enqueue(n);
                }
            }
            return dist;
        }

        // ---------- A* ----------

        // 우선순위 큐 (A*에서 가장 가능성 높은 칸을 먼저 꺼내기 위한 최소 힙)
        private class MinHeap
        {
            private readonly List<Vector2Int> cells = new List<Vector2Int>();
            private readonly List<float> priorities = new List<float>();
            public int Count => cells.Count;

            public void Push(Vector2Int cell, float priority)
            {
                cells.Add(cell);
                priorities.Add(priority);
                int i = cells.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (priorities[parent] <= priorities[i]) break;
                    Swap(i, parent);
                    i = parent;
                }
            }

            public Vector2Int Pop()
            {
                Vector2Int top = cells[0];
                int last = cells.Count - 1;
                cells[0] = cells[last];
                priorities[0] = priorities[last];
                cells.RemoveAt(last);
                priorities.RemoveAt(last);

                int i = 0;
                while (true)
                {
                    int left = i * 2 + 1, right = left + 1, smallest = i;
                    if (left < cells.Count && priorities[left] < priorities[smallest]) smallest = left;
                    if (right < cells.Count && priorities[right] < priorities[smallest]) smallest = right;
                    if (smallest == i) break;
                    Swap(i, smallest);
                    i = smallest;
                }
                return top;
            }

            private void Swap(int a, int b)
            {
                Vector2Int tc = cells[a]; cells[a] = cells[b]; cells[b] = tc;
                float tp = priorities[a]; priorities[a] = priorities[b]; priorities[b] = tp;
            }
        }

        // A* 경로 탐색: start에서 isGoal을 만족하는 칸까지 비용이 가장 적은 경로를 찾는다
        // stepCost(from, to)가 0보다 작으면 그 칸으로는 이동 불가
        // heuristic은 남은 비용의 추정치 (실제보다 작거나 같아야 최단 경로가 보장됨)
        // 경로를 못 찾으면 null
        public static List<Vector2Int> FindPath(int w, int h, Vector2Int start,
            Func<Vector2Int, bool> isGoal,
            Func<Vector2Int, Vector2Int, float> stepCost,
            Func<Vector2Int, float> heuristic)
        {
            float[,] g = new float[w, h];
            bool[,] closed = new bool[w, h];
            Vector2Int[,] cameFrom = new Vector2Int[w, h];
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                {
                    g[x, z] = float.MaxValue;
                    cameFrom[x, z] = new Vector2Int(-1, -1);
                }

            var open = new MinHeap();
            g[start.x, start.y] = 0f;
            open.Push(start, heuristic(start));

            while (open.Count > 0)
            {
                Vector2Int current = open.Pop();
                if (closed[current.x, current.y]) continue;
                closed[current.x, current.y] = true;

                if (isGoal(current))
                {
                    // 도착 → cameFrom을 거꾸로 따라가며 경로 복원
                    var path = new List<Vector2Int>();
                    Vector2Int c = current;
                    while (c.x >= 0)
                    {
                        path.Add(c);
                        if (c == start) break;
                        c = cameFrom[c.x, c.y];
                    }
                    path.Reverse();
                    return path;
                }

                foreach (Vector2Int d in Dirs4)
                {
                    Vector2Int next = current + d;
                    if (next.x < 0 || next.y < 0 || next.x >= w || next.y >= h) continue;
                    if (closed[next.x, next.y]) continue;

                    float cost = stepCost(current, next);
                    if (cost < 0f) continue;

                    float newG = g[current.x, current.y] + cost;
                    if (newG >= g[next.x, next.y]) continue;

                    g[next.x, next.y] = newG;
                    cameFrom[next.x, next.y] = current;
                    open.Push(next, newG + heuristic(next));
                }
            }
            return null;
        }

        // ---------- 포아송 디스크 샘플링 (Bridson 방식) ----------

        // 영역 안에 서로 최소 radius 이상 떨어진 점들을 고르게 뿌린다 (블루 노이즈)
        // accept가 false인 위치(길, 밭, 물 등)에는 점을 두지 않는다
        // 영역이 여러 조각으로 나뉘어 있어도 빠짐없이 채우도록 시작점을 여러 개 뿌린다
        public static List<Vector2> PoissonDisk(float width, float height, float radius,
            System.Random rng, Func<Vector2, bool> accept, int attemptsPerPoint = 30)
        {
            var points = new List<Vector2>();
            if (radius <= 0f) return points;

            float cellSize = radius / Mathf.Sqrt(2f);
            int gw = Mathf.CeilToInt(width / cellSize), gh = Mathf.CeilToInt(height / cellSize);
            int[,] grid = new int[gw, gh];
            for (int x = 0; x < gw; x++)
                for (int z = 0; z < gh; z++)
                    grid[x, z] = -1;

            var active = new List<int>();

            // 후보 위치가 다른 점과 너무 가까운지 검사
            bool IsFarEnough(Vector2 p)
            {
                int cx = (int)(p.x / cellSize), cz = (int)(p.y / cellSize);
                for (int x = Mathf.Max(0, cx - 2); x <= Mathf.Min(gw - 1, cx + 2); x++)
                    for (int z = Mathf.Max(0, cz - 2); z <= Mathf.Min(gh - 1, cz + 2); z++)
                    {
                        int idx = grid[x, z];
                        if (idx >= 0 && (points[idx] - p).sqrMagnitude < radius * radius) return false;
                    }
                return true;
            }

            void AddPoint(Vector2 p)
            {
                points.Add(p);
                active.Add(points.Count - 1);
                grid[(int)(p.x / cellSize), (int)(p.y / cellSize)] = points.Count - 1;
            }

            // 여러 시작점 (떨어진 숲 조각도 채우기 위해)
            int seedTries = Mathf.Max(30, Mathf.CeilToInt(width * height / (radius * radius)));
            for (int i = 0; i < seedTries; i++)
            {
                var p = new Vector2((float)rng.NextDouble() * width, (float)rng.NextDouble() * height);
                if (accept(p) && IsFarEnough(p)) AddPoint(p);
            }

            // 활성 점 주변 고리(radius ~ 2*radius)에 새 점을 시도하며 퍼져 나감
            while (active.Count > 0)
            {
                int ai = rng.Next(active.Count);
                Vector2 basePoint = points[active[ai]];
                bool found = false;

                for (int t = 0; t < attemptsPerPoint; t++)
                {
                    float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float dist = radius * (1f + (float)rng.NextDouble());
                    Vector2 candidate = basePoint + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;

                    if (candidate.x < 0f || candidate.y < 0f || candidate.x >= width || candidate.y >= height) continue;
                    if (!accept(candidate) || !IsFarEnough(candidate)) continue;

                    AddPoint(candidate);
                    found = true;
                    break;
                }

                if (!found) active.RemoveAt(ai);
            }
            return points;
        }
    }
}