using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace FarmingSystem.Economy
{
    // 흥정 기록을 CSV로 저장 (나중에 FCM/RBFN 학습 데이터로 씀)
    // ShopSellPanelUI의 "Log Bargains"를 켜면 흥정할 때마다 한 줄씩 쌓임
    // 파일 위치: 에디터 = 프로젝트 폴더/BargainLog/bargain_log.csv, 빌드 = Application.persistentDataPath/bargain_log.csv
    // 열: 시각, 제안 금액, 원래 금액, 배율, 수락 확률, 결과(1/0), 특징값 8개
    public static class BargainLogger
    {
        public static string FilePath
        {
            get
            {
                string dir = Application.isEditor
                    ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "BargainLog"))
                    : Application.persistentDataPath;
                return Path.Combine(dir, "bargain_log.csv");
            }
        }

        public static void Write(BargainContext ctx, float chance, bool accepted)
        {
            if (ctx == null) return;
            try
            {
                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                bool header = !File.Exists(path);
                var sb = new StringBuilder();
                if (header)
                {
                    sb.Append("time,offer,base,ratio,chance,accepted");
                    foreach (string n in BargainContext.FeatureNames) sb.Append(',').Append(n);
                    sb.AppendLine();
                }
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(',')
                  .Append(ctx.Offer).Append(',').Append(ctx.BaseTotal).Append(',')
                  .Append(ctx.OfferRatio.ToString("0.###")).Append(',')
                  .Append(chance.ToString("0.###")).Append(',')
                  .Append(accepted ? 1 : 0);
                foreach (float f in ctx.Features) sb.Append(',').Append(f.ToString("0.###"));
                sb.AppendLine();
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception e) { Debug.LogWarning("[흥정 기록] 저장 실패: " + e.Message); }
        }
    }
}