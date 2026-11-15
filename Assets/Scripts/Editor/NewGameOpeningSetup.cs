#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FarmingSystem.Building;
using FarmingSystem.Intro;
using FarmingSystem.Player;

namespace FarmingSystem.EditorTools
{
    // 새 게임 오프닝 메뉴 (Tools → 행성농사일지 → 새 게임 오프닝)
    //  ★ PV 씬에서 오프닝 가져오기: PV 프로젝트 폴더의 씬 파일(.unity)을 골라 주면 PV_ShipLanding·PV_Opening 카메라 점을
    //    이 게임 씬에 똑같이 만듦 → NewGame_ShipLanding + NewGame_OpeningCamera (우주선 방향만 PV의 정반대)
    //    PV 프로젝트에 아무것도 넣을 필요 없음 (씬 파일을 글자로 읽기만 함, PV 프로젝트는 열지 않아도 됨)
    //  - 만들기 (PV 오프닝): PV 씬 없이 PV와 같은 계산으로 카메라 점을 새로 배치 (착륙 자리는 직접 옮겨야 함)
    public static class NewGameOpeningSetup
    {
        [MenuItem("Tools/행성농사일지/새 게임 오프닝/만들기 (PV 오프닝)")]
        private static void Create()
        {
            IntroShipLanding landing = Object.FindAnyObjectByType<IntroShipLanding>(FindObjectsInactive.Include);
            bool newLanding = landing == null;
            if (newLanding)
            {
                PlayerController pc = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
                Vector3 pos = pc != null ? pc.transform.position + Flat(pc.transform.forward) * 8f : Vector3.zero;
                float yaw = pc != null ? pc.transform.eulerAngles.y : 0f;
                var go = new GameObject("NewGame_ShipLanding");
                Undo.RegisterCreatedObjectUndo(go, "새 게임 오프닝");
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, Mathf.Round(yaw / 90f) * 90f, 0f));
                landing = go.AddComponent<IntroShipLanding>();

                var so = new SerializedObject(landing);
                so.FindProperty("shipBuilding").objectReferenceValue = FindShip();
                so.ApplyModifiedPropertiesWithoutUndo();
                landing.EnsureExitPoint();
            }

            IntroCameraPath path = Object.FindAnyObjectByType<IntroCameraPath>(FindObjectsInactive.Include);
            if (path == null)
            {
                var go = new GameObject("NewGame_OpeningCamera");
                Undo.RegisterCreatedObjectUndo(go, "새 게임 오프닝");
                path = go.AddComponent<IntroCameraPath>();
            }
            path.AutoOpening();

            EditorSceneManager.MarkSceneDirty(landing.gameObject.scene);
            Selection.activeObject = landing.gameObject;
            string msg = (newLanding ? "착륙 자리(NewGame_ShipLanding)를 캐릭터 앞에 만들었어요.\n" : "") +
                         "카메라 점 4개를 PV와 같은 방식으로 배치했어요.\n\n" +
                         "1. NewGame_ShipLanding을 평평한 빈 땅으로 옮기기 (흰 테두리 = 우주선이 차지하는 칸)\n" +
                         "2. Y 회전으로 주황 선(날아오는 길)이 산을 지나가지 않게 (바다 쪽에서 오게)\n" +
                         "3. NewGame_OpeningCamera ⋮ → '자동 배치: 오프닝' 다시 누르기\n" +
                         "4. Ctrl+S로 씬 저장";
            Debug.Log("[새 게임 오프닝] " + msg);
            EditorUtility.DisplayDialog("새 게임 오프닝", msg, "확인");
        }

        // ───────────────────────── PV 오프닝 가져오기 ─────────────────────────
        // PV 프로젝트의 씬 파일(.unity)을 직접 읽어서 (PV 스크립트 필요 없음, PV 프로젝트에 아무것도 안 넣어도 됨)
        // PV_ShipLanding(착륙 자리·캐릭터 나타날 자리·비행 값)과 오프닝 카메라 점(위치·방향·시간·화각·흔들림·착륙 연결)을
        // 이 게임 씬에 똑같이 만듦 (우주선 방향만 PV의 정반대)
        // 씬 파일은 글자(YAML)로 저장돼 있어서, PV 스크립트가 없어도 필드 이름으로 값을 찾아 읽을 수 있음

        private class OpeningFile
        {
            public float[] landingPos, landingRot, exitPos;
            public float shipYaw, shipWidth = 9f, startHeight = 40f, startDistance = 60f, flyTime = 5f, settleTime = 1.2f,
                         landShake = 0.25f, shakeTime = 0.8f, playerDelay = 1.2f;
            public float curveAmount = 0.4f, minGroundClearance = 1.2f, liftRamp = 12f;
            public bool avoidTrees = true;
            public List<PointData> points = new List<PointData>();
        }

        private class PointData
        {
            public string name;
            public float[] pos, rot;
            public float moveTime, holdTime, fov, shake, afterLandingHold = 0.8f;
            public int ease;
            public bool waitForLanding, startsLanding;
        }

        [MenuItem("Tools/행성농사일지/새 게임 오프닝/PV 씬에서 오프닝 가져오기")]
        private static void ImportPV()
        {
            string file = EditorUtility.OpenFilePanel("PV 프로젝트의 씬 파일 고르기 (PV 영상을 찍은 씬 .unity)", System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments), "unity");
            if (string.IsNullOrEmpty(file)) return;
            OpeningFile f;
            string error;
            try { f = PVSceneReader.Read(System.IO.File.ReadAllText(file), out error); }
            catch (System.Exception e) { f = null; error = "씬 파일을 읽다가 문제가 생겼어요: " + e.Message; }
            if (f == null)
            {
                EditorUtility.DisplayDialog("PV 오프닝 가져오기", error, "확인");
                return;
            }

            // 착륙 자리
            IntroShipLanding landing = Object.FindAnyObjectByType<IntroShipLanding>(FindObjectsInactive.Include);
            if (landing == null)
            {
                var go = new GameObject("NewGame_ShipLanding");
                Undo.RegisterCreatedObjectUndo(go, "PV 오프닝 가져오기");
                landing = go.AddComponent<IntroShipLanding>();
            }
            Undo.RecordObject(landing.transform, "PV 오프닝 가져오기");
            Quaternion lrot = Q(f.landingRot);
            landing.transform.SetPositionAndRotation(V(f.landingPos), lrot);

            // 우주선 모델 방향: PV(착륙 자리 방향 + Ship Yaw)의 정반대 → 90도 단위 칸에 맞춤
            int steps = Mathf.RoundToInt(lrot.eulerAngles.y / 90f);
            int pvSteps = Mathf.RoundToInt((lrot.eulerAngles.y + f.shipYaw) / 90f);
            int facing = ((pvSteps - steps + 2) % 4 + 4) % 4;

            var ls = new SerializedObject(landing);
            if (ls.FindProperty("shipBuilding").objectReferenceValue == null) ls.FindProperty("shipBuilding").objectReferenceValue = FindShip();
            ls.FindProperty("shipFacing").intValue = facing;
            ls.FindProperty("startHeight").floatValue = f.startHeight;
            ls.FindProperty("startDistance").floatValue = f.startDistance;
            ls.FindProperty("flyTime").floatValue = f.flyTime;
            ls.FindProperty("settleTime").floatValue = f.settleTime;
            ls.FindProperty("landShake").floatValue = f.landShake;
            ls.FindProperty("shakeTime").floatValue = f.shakeTime;
            ls.FindProperty("playerDelay").floatValue = f.playerDelay;
            ls.ApplyModifiedProperties();

            Transform exit = landing.EnsureExitPoint();
            Undo.RecordObject(exit, "PV 오프닝 가져오기");
            exit.position = V(f.exitPos);

            // 카메라 경로 + 점
            IntroCameraPath path = Object.FindAnyObjectByType<IntroCameraPath>(FindObjectsInactive.Include);
            if (path == null)
            {
                var go = new GameObject("NewGame_OpeningCamera");
                Undo.RegisterCreatedObjectUndo(go, "PV 오프닝 가져오기");
                path = go.AddComponent<IntroCameraPath>();
            }
            for (int i = path.transform.childCount - 1; i >= 0; i--) Undo.DestroyObjectImmediate(path.transform.GetChild(i).gameObject);
            var cs = new SerializedObject(path);
            cs.FindProperty("curveAmount").floatValue = f.curveAmount;
            cs.FindProperty("minGroundClearance").floatValue = f.minGroundClearance;
            cs.FindProperty("liftRamp").floatValue = f.liftRamp;
            cs.FindProperty("avoidTrees").boolValue = f.avoidTrees;
            cs.ApplyModifiedProperties();

            foreach (PointData d in f.points)
            {
                var go = new GameObject(d.name);
                Undo.RegisterCreatedObjectUndo(go, "PV 오프닝 가져오기");
                go.transform.SetParent(path.transform, false);
                go.transform.SetPositionAndRotation(V(d.pos), Q(d.rot));
                IntroCamPoint p = go.AddComponent<IntroCamPoint>();
                p.moveTime = d.moveTime;
                p.holdTime = d.holdTime;
                p.fov = d.fov;
                p.shake = d.shake;
                p.ease = (IntroCamPoint.EaseType)d.ease;
                p.waitForLanding = d.waitForLanding;
                p.afterLandingHold = d.afterLandingHold;
                if (d.startsLanding) UnityEditor.Events.UnityEventTools.AddPersistentListener(p.onArrive, landing.Play);
                EditorUtility.SetDirty(p);
            }

            landing.SyncExitReference(); // 지금 캐릭터 자리 기준 → 이후 ExitPoint를 옮기면 캐릭터 비추는 카메라가 따라감
            EditorSceneManager.MarkSceneDirty(landing.gameObject.scene);
            Selection.activeObject = path.gameObject;

            // 우주선 크기 비교 (PV는 가로 길이로, 게임은 건물 칸 수로 크기가 정해짐)
            BuildingData ship = ls.FindProperty("shipBuilding").objectReferenceValue as BuildingData;
            string size = "";
            if (ship != null)
            {
                float now = Mathf.Max(ship.footprint.x, ship.footprint.y) * 1.94f * ship.fill;
                int suggest = Mathf.Max(1, Mathf.RoundToInt(f.shipWidth / (1.94f * ship.fill)));
                if (Mathf.Abs(now - f.shipWidth) > 1f)
                    size = $"\n\n※ 우주선 크기: PV {f.shipWidth:0.#}m / 지금 {now:0.#}m\n   PV와 같게 하려면 Building_player_spaceship의 Footprint를 {suggest} x {suggest}로";
            }
            string msg = $"완료! PV 씬에서 착륙 자리·카메라 점 {f.points.Count}개를 똑같이 가져왔어요.\n우주선 방향은 PV의 정반대 (Ship Facing {facing}).{size}\n\nCtrl+S로 씬 저장 → 빈 칸에서 새 게임";
            Debug.Log("[새 게임 오프닝] " + msg);
            EditorUtility.DisplayDialog("PV 오프닝 가져오기", msg, "확인");
        }

        // PV 씬 파일(.unity, 글자 형식)에서 오프닝 값만 골라 읽음
        //  - PVShipLanding = startDistance·settleTime·shipWidth 필드가 있는 스크립트
        //  - PVCameraPath  = curveAmount·liftRamp 필드가 있는 스크립트 (이름에 Opening이 들어간 것 우선)
        //  - PVCamPoint    = 카메라 경로의 자식들 (Hierarchy 순서 그대로), moveTime·holdTime 필드
        private static class PVSceneReader
        {
            private class Doc { public int cls; public string id; public string body; }

            private static Dictionary<string, Doc> docs;

            public static OpeningFile Read(string text, out string error)
            {
                error = null;
                docs = new Dictionary<string, Doc>();
                foreach (string part in text.Replace("\r\n", "\n").Split(new[] { "\n--- !u!" }, System.StringSplitOptions.None))
                {
                    Match m = Regex.Match(part, @"^(\d+) &(-?\d+)");
                    if (!m.Success) continue;
                    docs[m.Groups[2].Value] = new Doc { cls = int.Parse(m.Groups[1].Value), id = m.Groups[2].Value, body = part };
                }
                if (docs.Count == 0) { error = "씬 파일을 글자로 읽을 수 없어요. (PV 프로젝트의 Project Settings → Editor → Asset Serialization이 Force Text여야 해요)"; return null; }

                // 착륙 자리 (PVShipLanding)
                Doc landing = null;
                foreach (Doc d in docs.Values)
                    if (d.cls == 114 && Has(d, "startDistance") && Has(d, "settleTime") && Has(d, "shipWidth")) { landing = d; break; }
                if (landing == null) { error = "이 씬에서 PV_ShipLanding(우주선 착륙)을 못 찾았어요. PV 영상을 찍은 씬 파일을 골라 주세요."; return null; }

                // 오프닝 카메라 경로 (PVCameraPath)
                Doc path = null;
                foreach (Doc d in docs.Values)
                {
                    if (d.cls != 114 || !Has(d, "curveAmount") || !Has(d, "liftRamp")) continue;
                    bool opening = GoName(Ref(d, "m_GameObject")).Contains("Opening");
                    if (path == null || opening) path = d;
                    if (opening) break;
                }
                if (path == null) { error = "이 씬에서 오프닝 카메라 경로(PV_Opening)를 못 찾았어요."; return null; }

                var f = new OpeningFile();
                Doc lt = TransformOf(Ref(landing, "m_GameObject"));
                Vector3 lp; Quaternion lr;
                World(lt, out lp, out lr);
                f.landingPos = A(lp);
                f.landingRot = A(lr);
                f.shipYaw = F(landing, "shipYaw", 0f);
                f.shipWidth = F(landing, "shipWidth", 9f);
                f.startHeight = F(landing, "startHeight", 40f);
                f.startDistance = F(landing, "startDistance", 60f);
                f.flyTime = F(landing, "flyTime", 5f);
                f.settleTime = F(landing, "settleTime", 1.2f);
                f.landShake = F(landing, "landShake", 0.25f);
                f.shakeTime = F(landing, "shakeTime", 0.8f);
                f.playerDelay = F(landing, "playerDelay", 1.2f);

                // 캐릭터 나타날 자리 (ExitPoint 자식, 없으면 PV 기본 자리)
                Vector3 exitPos = lp + lr * new Vector3(0f, 0f, -f.shipWidth * 0.5f - 1f);
                foreach (string c in Children(lt))
                {
                    Doc ct = Get(c);
                    if (ct == null || GoName(Ref(ct, "m_GameObject")) != "ExitPoint") continue;
                    Quaternion unused;
                    World(ct, out exitPos, out unused);
                    break;
                }
                f.exitPos = A(exitPos);

                f.curveAmount = F(path, "curveAmount", 0.4f);
                f.minGroundClearance = F(path, "minGroundClearance", 1.2f);
                f.liftRamp = F(path, "liftRamp", 12f);
                f.avoidTrees = F(path, "avoidTrees", 1f) > 0.5f;

                // 카메라 점 (Hierarchy 순서, 꺼진 점은 제외)
                foreach (string c in Children(TransformOf(Ref(path, "m_GameObject"))))
                {
                    Doc ct = Get(c);
                    if (ct == null) continue;
                    string go = Ref(ct, "m_GameObject");
                    Doc goDoc = Get(go);
                    if (goDoc != null && Str(goDoc, "m_IsActive") == "0") continue;
                    Doc pt = null;
                    foreach (Doc d in docs.Values)
                        if (d.cls == 114 && Ref(d, "m_GameObject") == go && Has(d, "moveTime") && Has(d, "holdTime")) { pt = d; break; }
                    if (pt == null) continue;

                    Vector3 pp; Quaternion pr;
                    World(ct, out pp, out pr);
                    var p = new PointData
                    {
                        name = GoName(go),
                        pos = A(pp),
                        rot = A(pr),
                        moveTime = F(pt, "moveTime", 3f),
                        holdTime = F(pt, "holdTime", 0f),
                        fov = F(pt, "fov", 0f),
                        shake = F(pt, "shake", 0f),
                        ease = (int)F(pt, "ease", 0f),
                        waitForLanding = F(pt, "waitForLanding", 0f) > 0.5f,
                        afterLandingHold = F(pt, "afterLandingHold", 0.8f),
                    };
                    // 도착 이벤트에 "우주선 착륙 Play"가 연결돼 있는지
                    foreach (Match m in Regex.Matches(pt.body, @"m_Target: \{fileID: (-?\d+)\}[\s\S]*?m_MethodName: (\w+)"))
                        if (m.Groups[1].Value == landing.id && m.Groups[2].Value == "Play") p.startsLanding = true;
                    f.points.Add(p);
                }
                if (f.points.Count == 0) { error = "오프닝 카메라 경로에 카메라 점이 없어요."; return null; }
                return f;
            }

            private static Doc Get(string id)
            {
                Doc d;
                return id != null && docs.TryGetValue(id, out d) ? d : null;
            }

            private static bool Has(Doc d, string field) => Regex.IsMatch(d.body, @"\n\s*" + field + ":");

            private static string Str(Doc d, string field)
            {
                Match m = Regex.Match(d.body, @"\n\s*" + field + @": ?(.*)");
                return m.Success ? m.Groups[1].Value.Trim() : null;
            }

            private static float Num(string s, float fallback)
            {
                float v;
                return s != null && float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
            }

            private static float F(Doc d, string field, float fallback) => Num(Str(d, field), fallback);

            private static string Ref(Doc d, string field)
            {
                if (d == null) return null;
                Match m = Regex.Match(d.body, field + @": \{fileID: (-?\d+)");
                return m.Success ? m.Groups[1].Value : null;
            }

            private static string GoName(string goId)
            {
                Doc g = Get(goId);
                return g != null ? (Str(g, "m_Name") ?? "") : "";
            }

            private static Doc TransformOf(string goId)
            {
                foreach (Doc d in docs.Values)
                    if (d.cls == 4 && Ref(d, "m_GameObject") == goId) return d;
                return null;
            }

            private static List<string> Children(Doc t)
            {
                var list = new List<string>();
                if (t == null) return list;
                Match block = Regex.Match(t.body, @"m_Children:\s*\n((?:\s*- \{fileID: -?\d+\}[^\n]*\n?)*)");
                if (!block.Success) return list;
                foreach (Match m in Regex.Matches(block.Groups[1].Value, @"fileID: (-?\d+)"))
                    list.Add(m.Groups[1].Value);
                return list;
            }

            private static Vector4 Vec(Doc d, string field, Vector4 fallback)
            {
                Match m = Regex.Match(d.body, field + @": \{x: ([^,]+), y: ([^,]+), z: ([^,}]+)(?:, w: ([^}]+))?\}");
                if (!m.Success) return fallback;
                return new Vector4(Num(m.Groups[1].Value, 0f), Num(m.Groups[2].Value, 0f), Num(m.Groups[3].Value, 0f),
                                   m.Groups[4].Success ? Num(m.Groups[4].Value, 1f) : 1f);
            }

            // 부모를 따라 올라가며 실제(월드) 위치·방향 계산
            private static void World(Doc t, out Vector3 pos, out Quaternion rot)
            {
                Matrix4x4 m = Matrix4x4.identity;
                Quaternion r = Quaternion.identity;
                int guard = 0;
                while (t != null && guard++ < 64)
                {
                    Vector4 lp = Vec(t, "m_LocalPosition", Vector4.zero);
                    Vector4 lq = Vec(t, "m_LocalRotation", new Vector4(0f, 0f, 0f, 1f));
                    Vector4 ls = Vec(t, "m_LocalScale", Vector4.one);
                    Quaternion q = new Quaternion(lq.x, lq.y, lq.z, lq.w);
                    m = Matrix4x4.TRS(new Vector3(lp.x, lp.y, lp.z), q, new Vector3(ls.x, ls.y, ls.z)) * m;
                    r = q * r;
                    string father = Ref(t, "m_Father");
                    t = father != null && father != "0" ? Get(father) : null;
                }
                pos = m.GetColumn(3);
                rot = r;
            }

            private static float[] A(Vector3 v) => new[] { v.x, v.y, v.z };
            private static float[] A(Quaternion q) => new[] { q.x, q.y, q.z, q.w };
        }

        private static Vector3 V(float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.zero;
        private static Quaternion Q(float[] a) => a != null && a.Length >= 4 ? new Quaternion(a[0], a[1], a[2], a[3]) : Quaternion.identity;

        private static BuildingData FindShip()
        {
            foreach (string g in AssetDatabase.FindAssets("Building_player_spaceship t:BuildingData"))
            {
                var d = AssetDatabase.LoadAssetAtPath<BuildingData>(AssetDatabase.GUIDToAssetPath(g));
                if (d != null) return d;
            }
            return null;
        }

        private static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude < 0.0001f ? Vector3.forward : v.normalized;
        }
    }
}
#endif