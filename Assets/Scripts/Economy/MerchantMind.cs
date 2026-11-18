using System;
using System.Collections.Generic;
using UnityEngine;
using FarmingSystem.Core;
using FarmingSystem.Farming;

namespace FarmingSystem.Economy
{
    // 상인의 머리 (FCM + RBFN). 게임 내내 플레이어의 판매 행동을 보고 배운다.
    //
    // [1] FCM (퍼지 C-평균) : 플레이어 성향 4가지에 대한 소속도 (합 = 1)
    //     - 판매할 때마다 행동 표본 1개 (6가지 값, 0~1) → 플레이어 프로필(최근 행동 평균)
    //     - 프로필과 각 성향 중심 사이 거리 = 마할라노비스 거리 (값들 사이 상관·흩어짐 반영)
    //     - 소속도 u_j = 1 / Σ_k (d_j / d_k)^(2/(m-1)),  m = 2
    //     - 중심은 표본이 들어올 때마다 조금씩 이동 (온라인 FCM), 원래 성향 뜻을 잃지 않게 처음 위치로 살짝 당김
    //
    // [2] RBFN ① 버티기 예측 : "흥정을 거절당해도 원래 가격에 팔고 갈 확률"
    //     - 입력: 성향 소속도 4 + 제안 배율·거래 규모·품질·시도 횟수 (8개)
    //     - 가우시안 은닉 노드 8개 (성향 4 x 배율 낮음/높음) → 시그모이드
    //     - 정답: 거절 뒤 그냥 팔았으면 1, 물건을 빼거나 창을 닫으면 0 → 경사하강으로 한 번씩 학습
    //     - 버틸 사람이면 상인이 깐깐해지고, 떠날 사람이면 조금 너그러워짐
    //
    // [3] RBFN ② 수요 예측 : "이 플레이어가 다음에 각 작물을 얼마나 팔지" (금액 비중)
    //     - 입력: 성향 소속도 4 + 계절 4 → 가우시안 은닉 노드 16개 (성향 x 계절)
    //     - 판매할 때마다 실제로 판 작물 비중을 정답으로 학습 (LMS)
    //     - 오늘의 특별 상품을 고를 때 씀
    //
    // 거래 1번 = 학습 1단계 (온라인 학습). 상태는 세이브 파일에 같이 저장됨.
    public enum MerchantType { Instant = 0, Holder = 1, Haggler = 2, Opportunist = 3 }

    /// <summary>판매 한 번의 기록 (ShopSellPanelUI가 만들어 넘김)</summary>
    public struct SaleRecord
    {
        public float SellFraction;   // 갖고 있던 작물 중 이번에 판 비율
        public bool Bargained;       // 이번 판매에서 흥정을 했는지
        public float OfferRatio;     // 마지막으로 부른 배율 (흥정 안 했으면 1)
        public float AvgQuality;     // 0~1
        public float SpecialShare;   // 금액 중 오늘의 특별 상품 비중
        public long Value;           // 받은 금액
        public Dictionary<CropData, float> CropShares; // 작물별 금액 비중
    }

    public static class MerchantMind
    {
        public const int TypeCount = 4;
        public const int ProfileDim = 6;
        public const int MinSalesForPersona = 3;   // 이만큼 팔아 봐야 성향을 말함

        public static readonly string[] TypeNames = { "즉시 판매형", "장기 보유형", "흥정 집착형", "기회주의형" };
        public static readonly string[] ProfileNames = { "판매 비율", "흥정 빈도", "제안 배율", "평균 품질", "특별 상품 비중", "거래 규모" };

        // 성향별 처음 중심 (프로필 6가지 값 순서)
        private static readonly float[][] Prototypes =
        {
            new[] { 0.90f, 0.10f, 0.05f, 0.40f, 0.20f, 0.30f }, // 즉시 판매형: 있는 대로 바로 팜, 흥정 거의 안 함
            new[] { 0.30f, 0.20f, 0.10f, 0.80f, 0.20f, 0.60f }, // 장기 보유형: 조금씩, 좋은 품질로 크게
            new[] { 0.60f, 0.90f, 0.50f, 0.40f, 0.20f, 0.40f }, // 흥정 집착형: 매번 흥정, 높게 부름
            new[] { 0.50f, 0.30f, 0.15f, 0.40f, 0.80f, 0.40f }, // 기회주의형: 특별 상품 위주
        };

        // ---------- 조절값 ----------
        private const float FuzzyM = 2f;          // 퍼지 지수 m
        private const float ProfileAlpha = 0.3f;  // 프로필이 새 표본을 따라가는 정도
        private const float CovBeta = 0.1f;       // 공분산 갱신 정도
        private const float CovReg = 0.02f;       // 공분산 정규화 (역행렬 안정)
        private const float CenterLR = 0.05f;     // 중심 이동 학습률
        private const float CenterAnchor = 0.02f; // 처음 위치로 당기는 정도
        private const float StaySigma = 0.5f, StayLR = 0.5f, StayPriorBias = 0.85f;
        private const float DemandSigma = 0.7f, DemandLR = 0.3f;
        private const int StayHidden = 8, DemandHidden = 16;

        [Serializable]
        private class DemandRow
        {
            public string crop;
            public float[] w = new float[DemandHidden];
        }

        [Serializable]
        private class State
        {
            public int sales;
            public float[] profile = new float[ProfileDim];
            public float[] centers = new float[TypeCount * ProfileDim];
            public float[] mean = new float[ProfileDim];
            public float[] cov = new float[ProfileDim * ProfileDim];
            public float[] stayW = new float[StayHidden];
            public float stayB = StayPriorBias;
            public int stayLabels;
            public List<DemandRow> demand = new List<DemandRow>();
        }

        private static State s;
        private static State St { get { if (s == null) Reset(); return s; } }

        // 화면(디버그 창) 표시용: 마지막 흥정 판단
        public static float LastStay = -1f, LastMaxRatio = -1f, LastChance = -1f;

        public static int Sales => St.sales;
        public static int StayLabels => St.stayLabels;
        public static float[] Profile => (float[])St.profile.Clone();

        // ---------- 초기화 · 저장 ----------

        public static void Reset()
        {
            s = new State();
            for (int j = 0; j < TypeCount; j++)
                for (int i = 0; i < ProfileDim; i++)
                {
                    s.centers[j * ProfileDim + i] = Prototypes[j][i];
                    s.profile[i] += Prototypes[j][i] / TypeCount; // 처음엔 네 성향의 한가운데
                }
            Array.Copy(s.profile, s.mean, ProfileDim);
            for (int i = 0; i < ProfileDim; i++) s.cov[i * ProfileDim + i] = 0.05f;
            LastStay = LastMaxRatio = LastChance = -1f;
        }

        public static string ToJson() => JsonUtility.ToJson(St);

        public static void FromJson(string json)
        {
            if (string.IsNullOrEmpty(json)) { Reset(); return; }
            try
            {
                State loaded = JsonUtility.FromJson<State>(json);
                if (loaded == null || loaded.centers == null || loaded.centers.Length != TypeCount * ProfileDim ||
                    loaded.cov == null || loaded.cov.Length != ProfileDim * ProfileDim ||
                    loaded.stayW == null || loaded.stayW.Length != StayHidden) { Reset(); return; }
                if (loaded.demand == null) loaded.demand = new List<DemandRow>();
                s = loaded;
            }
            catch (Exception e) { Debug.LogWarning("[상인 AI] 불러오기 실패, 처음부터: " + e.Message); Reset(); }
        }

        // ---------- [1] FCM ----------

        /// <summary>지금 플레이어의 성향 소속도 4개 (합 = 1)</summary>
        public static float[] Memberships => ComputeMemberships(St.profile);

        public static bool PersonaReady
        {
            get { float[] u = Memberships; return St.sales >= MinSalesForPersona && u[(int)ArgMax(u)] >= 0.35f; }
        }

        public static MerchantType Dominant => (MerchantType)ArgMax(Memberships);

        private static float[] ComputeMemberships(float[] x)
        {
            float[] inv = InverseCov();
            var d2 = new float[TypeCount];
            for (int j = 0; j < TypeCount; j++)
            {
                d2[j] = Mahalanobis2(x, j, inv);
                if (d2[j] < 1e-8f) // 중심과 딱 겹치면 그 성향 100%
                {
                    var hard = new float[TypeCount];
                    hard[j] = 1f;
                    return hard;
                }
            }
            var u = new float[TypeCount];
            float p = 1f / (FuzzyM - 1f); // (d_j/d_k)^(2/(m-1)) = (d2_j/d2_k)^(1/(m-1))
            for (int j = 0; j < TypeCount; j++)
            {
                float sum = 0f;
                for (int k = 0; k < TypeCount; k++) sum += Mathf.Pow(d2[j] / d2[k], p);
                u[j] = 1f / sum;
            }
            return u;
        }

        private static float Mahalanobis2(float[] x, int j, float[] inv)
        {
            var diff = new float[ProfileDim];
            for (int i = 0; i < ProfileDim; i++) diff[i] = x[i] - St.centers[j * ProfileDim + i];
            float sum = 0f;
            for (int a = 0; a < ProfileDim; a++)
                for (int b = 0; b < ProfileDim; b++)
                    sum += diff[a] * inv[a * ProfileDim + b] * diff[b];
            return Mathf.Max(0f, sum);
        }

        // (공분산 + 정규화) 역행렬 - 가우스-조던 소거
        private static float[] InverseCov()
        {
            int n = ProfileDim;
            var a = new double[n, 2 * n];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++) a[r, c] = St.cov[r * n + c] + (r == c ? CovReg : 0f);
                a[r, n + r] = 1.0;
            }
            for (int col = 0; col < n; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < n; r++) if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
                if (Math.Abs(a[pivot, col]) < 1e-12) return Identity(); // 혹시 깨지면 유클리드 거리로
                if (pivot != col) for (int c = 0; c < 2 * n; c++) { double t = a[col, c]; a[col, c] = a[pivot, c]; a[pivot, c] = t; }
                double div = a[col, col];
                for (int c = 0; c < 2 * n; c++) a[col, c] /= div;
                for (int r = 0; r < n; r++)
                {
                    if (r == col) continue;
                    double f = a[r, col];
                    if (f == 0) continue;
                    for (int c = 0; c < 2 * n; c++) a[r, c] -= f * a[col, c];
                }
            }
            var inv = new float[n * n];
            for (int r = 0; r < n; r++) for (int c = 0; c < n; c++) inv[r * n + c] = (float)a[r, n + c];
            return inv;
        }

        private static float[] Identity()
        {
            var m = new float[ProfileDim * ProfileDim];
            for (int i = 0; i < ProfileDim; i++) m[i * ProfileDim + i] = 1f;
            return m;
        }

        // ---------- 판매 기록 → 학습 ----------

        /// <summary>판매 한 번 = 학습 한 단계 (FCM 프로필·중심, 수요 RBFN)</summary>
        public static void RecordSale(SaleRecord r)
        {
            State st = St;
            float[] x =
            {
                Mathf.Clamp01(r.SellFraction),
                r.Bargained ? 1f : 0f,
                r.Bargained ? Mathf.Clamp01((r.OfferRatio - 1f) * 2f) : 0f, // 1.5배 = 1
                Mathf.Clamp01(r.AvgQuality),
                Mathf.Clamp01(r.SpecialShare),
                Mathf.Clamp01(r.Value / (r.Value + 5000f)),
            };

            // 수요 학습은 '판매 전' 성향으로 (그 성향일 때 이걸 팔았다)
            float[] uBefore = Memberships;
            LearnDemand(uBefore, r.CropShares);

            // 공분산 (지수 이동 평균)
            for (int i = 0; i < ProfileDim; i++) st.mean[i] += CovBeta * (x[i] - st.mean[i]);
            for (int a = 0; a < ProfileDim; a++)
                for (int b = 0; b < ProfileDim; b++)
                {
                    float v = (x[a] - st.mean[a]) * (x[b] - st.mean[b]);
                    st.cov[a * ProfileDim + b] = (1f - CovBeta) * st.cov[a * ProfileDim + b] + CovBeta * v;
                }

            // 프로필 (첫 판매는 표본 쪽으로 크게)
            float alpha = st.sales == 0 ? 0.6f : ProfileAlpha;
            for (int i = 0; i < ProfileDim; i++) st.profile[i] += alpha * (x[i] - st.profile[i]);

            // 온라인 FCM: 표본의 소속도^m 만큼 중심을 표본 쪽으로
            float[] ux = ComputeMemberships(x);
            for (int j = 0; j < TypeCount; j++)
            {
                float w = CenterLR * Mathf.Pow(ux[j], FuzzyM);
                for (int i = 0; i < ProfileDim; i++)
                {
                    int k = j * ProfileDim + i;
                    st.centers[k] += w * (x[i] - st.centers[k]) + CenterAnchor * (Prototypes[j][i] - st.centers[k]);
                }
            }
            st.sales++;

            float[] u = Memberships;
            Debug.Log($"[상인 AI] 판매 {st.sales}회째 → " +
                      $"{TypeNames[0]} {u[0]:P0} / {TypeNames[1]} {u[1]:P0} / {TypeNames[2]} {u[2]:P0} / {TypeNames[3]} {u[3]:P0}");
        }

        // ---------- [2] RBFN ① 버티기 예측 ----------

        private static float[] StayInput(float[] u, BargainContext ctx)
        {
            float[] f = ctx != null ? ctx.Features : new float[BargainContext.FeatureNames.Length];
            return new[] { u[0], u[1], u[2], u[3], f[0], f[1], f[2], f[5] };
        }

        // 은닉 노드 중심: 성향(원-핫) x 제안 배율 낮음(0.1)/높음(0.4), 나머지는 보통 값
        private static float[] StayCenter(int k)
        {
            var c = new float[8];
            c[k / 2] = 1f;
            c[4] = (k % 2 == 0) ? 0.1f : 0.4f;
            c[5] = 0.3f; c[6] = 0.5f; c[7] = 0.2f;
            return c;
        }

        private static float[] StayHiddenOut(float[] z)
        {
            var phi = new float[StayHidden];
            float sum = 0f;
            for (int k = 0; k < StayHidden; k++)
            {
                phi[k] = Gaussian(z, StayCenter(k), StaySigma);
                sum += phi[k];
            }
            if (sum > 1e-6f) for (int k = 0; k < StayHidden; k++) phi[k] /= sum; // 정규화 RBF
            return phi;
        }

        /// <summary>거절당해도 원래 가격에 팔고 갈 확률 (0~1)</summary>
        public static float PredictStay(BargainContext ctx)
        {
            float[] phi = StayHiddenOut(StayInput(Memberships, ctx));
            float a = St.stayB;
            for (int k = 0; k < StayHidden; k++) a += St.stayW[k] * phi[k];
            return Sigmoid(a);
        }

        /// <summary>흥정 거절 뒤 플레이어가 어떻게 했는지 알려줌 (팔았으면 true, 떠났으면 false)</summary>
        public static void LearnStay(BargainContext ctx, bool stayed)
        {
            if (ctx == null) return;
            float[] phi = StayHiddenOut(StayInput(Memberships, ctx));
            float a = St.stayB;
            for (int k = 0; k < StayHidden; k++) a += St.stayW[k] * phi[k];
            float err = (stayed ? 1f : 0f) - Sigmoid(a); // 로지스틱 손실의 기울기
            for (int k = 0; k < StayHidden; k++) St.stayW[k] += StayLR * err * phi[k];
            St.stayB += StayLR * 0.2f * err;
            St.stayLabels++;
            Debug.Log($"[상인 AI] 거절 뒤 {(stayed ? "그냥 팔았음" : "안 팔고 떠남")} → 버티기 예측 학습 ({St.stayLabels}회)");
        }

        // ---------- [3] RBFN ② 수요 예측 ----------

        private static float[] DemandHiddenOut(float[] u, Season season)
        {
            var z = new float[8];
            for (int j = 0; j < TypeCount; j++) z[j] = u[j];
            z[4 + (int)season] = 1f;
            var phi = new float[DemandHidden];
            float sum = 0f;
            for (int k = 0; k < DemandHidden; k++)
            {
                var c = new float[8];
                c[k / 4] = 1f;          // 성향
                c[4 + k % 4] = 1f;      // 계절
                phi[k] = Gaussian(z, c, DemandSigma);
                sum += phi[k];
            }
            if (sum > 1e-6f) for (int k = 0; k < DemandHidden; k++) phi[k] /= sum;
            return phi;
        }

        private static Season NowSeason => GameClock.Instance != null ? GameClock.Instance.CurrentSeason : Season.Spring;

        /// <summary>이 작물을 다음 판매 때 얼마나 팔지 (금액 비중 예측, 0~1)</summary>
        public static float PredictDemand(CropData crop) => PredictDemand(crop, Memberships, NowSeason);

        private static float PredictDemand(CropData crop, float[] u, Season season)
        {
            if (crop == null) return 0f;
            DemandRow row = St.demand.Find(d => d.crop == crop.name);
            if (row == null) return 0f;
            float[] phi = DemandHiddenOut(u, season);
            float y = 0f;
            for (int k = 0; k < DemandHidden; k++) y += row.w[k] * phi[k];
            return Mathf.Clamp01(y);
        }

        private static void LearnDemand(float[] u, Dictionary<CropData, float> shares)
        {
            if (shares == null || shares.Count == 0) return;
            float[] phi = DemandHiddenOut(u, NowSeason);
            foreach (KeyValuePair<CropData, float> kv in shares)
                if (kv.Key != null && St.demand.Find(d => d.crop == kv.Key.name) == null)
                    St.demand.Add(new DemandRow { crop = kv.Key.name });

            foreach (DemandRow row in St.demand)
            {
                float target = 0f;
                foreach (KeyValuePair<CropData, float> kv in shares)
                    if (kv.Key != null && kv.Key.name == row.crop) { target = kv.Value; break; }
                float y = 0f;
                for (int k = 0; k < DemandHidden; k++) y += row.w[k] * phi[k];
                float err = target - y;
                for (int k = 0; k < DemandHidden; k++) row.w[k] += DemandLR * err * phi[k];
            }
        }

        /// <summary>
        /// 오늘의 특별 상품 고르기: 이번 계절 작물 중, 이 플레이어가 팔 것 같은 작물일수록 잘 뽑힘
        /// (판매 기록이 없으면 그냥 무작위)
        /// </summary>
        public static CropData PickSpecial(IList<CropData> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;
            Season season = NowSeason;
            var pool = new List<CropData>();
            foreach (CropData c in candidates) if (c != null && c.CanGrowIn(season)) pool.Add(c);
            if (pool.Count == 0) foreach (CropData c in candidates) if (c != null) pool.Add(c);
            if (pool.Count == 0) return null;
            if (St.sales == 0) return pool[UnityEngine.Random.Range(0, pool.Count)];

            float[] u = Memberships;
            var weights = new float[pool.Count];
            float total = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                weights[i] = 0.15f + PredictDemand(pool[i], u, season); // 0.15 = 안 팔던 작물도 가끔 뽑힘
                total += weights[i];
            }
            float r = UnityEngine.Random.value * total;
            for (int i = 0; i < pool.Count; i++)
            {
                r -= weights[i];
                if (r <= 0f) return pool[i];
            }
            return pool[pool.Count - 1];
        }

        /// <summary>디버그 창용: 이번 계절 수요 예측 상위 n개</summary>
        public static List<KeyValuePair<string, float>> TopDemand(int n)
        {
            var list = new List<KeyValuePair<string, float>>();
            float[] u = Memberships;
            float[] phi = DemandHiddenOut(u, NowSeason);
            foreach (DemandRow row in St.demand)
            {
                float y = 0f;
                for (int k = 0; k < DemandHidden; k++) y += row.w[k] * phi[k];
                list.Add(new KeyValuePair<string, float>(row.crop, Mathf.Clamp01(y)));
            }
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            if (list.Count > n) list.RemoveRange(n, list.Count - n);
            return list;
        }

        // ---------- 공통 ----------

        private static float Gaussian(float[] z, float[] c, float sigma)
        {
            float d = 0f;
            for (int i = 0; i < z.Length; i++) { float t = z[i] - c[i]; d += t * t; }
            return Mathf.Exp(-d / (2f * sigma * sigma));
        }

        private static float Sigmoid(float a) => 1f / (1f + Mathf.Exp(-a));

        private static int ArgMax(float[] v)
        {
            int best = 0;
            for (int i = 1; i < v.Length; i++) if (v[i] > v[best]) best = i;
            return best;
        }
    }
}
