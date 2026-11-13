버섯 캐릭터 — 걷기 / 뛰기

통합 파일: Mushroom_Walk_Run.fbx (걷기와 뛰기 두 클립)
단독 파일: Mushroom_Walk_InPlace.fbx, Mushroom_Run_InPlace.fbx
편집 원본: Mushroom_Walk_Run_Source.blend (Blender 4.2)
텍스처: Textures/Mushroom_BaseColor.png (FBX에도 내장)
미리보기: Mushroom_Walk_Run_Preview.mp4 (왼쪽 걷기 / 오른쪽 뛰기)
검증 기록: Validation.json

공통: 30FPS, 제자리 반복 루프, 원본 정점/UV/텍스처 유지, 관절 및 가중치 보강.
걷기: 0.933333초 / 28프레임 간격, 작은 종종걸음과 약 2.5cm 몸통 바운스.
뛰기: 0.60초 / 18프레임 간격, 넓어진 보폭과 전방 기울기, 두 발이 함께 뜨는 구간.
머리 움직임에 작은 시간차를 주고 버섯 갓, 잎, 중앙 목걸이의 별도 흔들림은 제한했습니다.
짧은 다리와 의상의 형태, 접지를 우선했으므로 모든 관절 각도가 최초 요청 수치와 같지는 않습니다.
참고 영상은 직접 재생 확인하지 못해 사용자 설명에 맞춰 제작했습니다.

Unity 6
1. 통합 FBX와 Textures 폴더를 Assets에 넣습니다. 단독 FBX는 필요한 경우에만 사용합니다.
2. Rig > Animation Type: Humanoid > Avatar Definition: Create From This Model > Apply.
3. Configure에서 뼈 매핑을 확인하세요. Root는 Hips에 연결하지 않습니다.
   Hips, Spine, Chest, Neck, Head 및 Left/Right UpperArm, LowerArm, Hand,
   UpperLeg, LowerLeg, Foot를 확인하고 필요하면 Pose > Enforce T-Pose를 적용합니다.
4. Animation 탭에서 두 클립 각각 Loop Time / Loop Pose를 켭니다.
   가져온 전체 범위를 사용하세요. 걷기 28프레임 간격, 뛰기 18프레임 간격입니다.
   Root Transform Rotation / Position XZ는 Bake Into Pose를 켭니다.
   Position Y는 Bake Into Pose를 켜고 Based Upon: Feet로 확인합니다.
5. Animator의 Apply Root Motion은 끕니다. 각각 걷기/뛰기 상태에 연결합니다.
6. URP에서 재질이 분홍색이면 URP/Lit 재질의 Base Map에 PNG를 넣습니다.

권장 이동 속도 (원본 높이 약 0.94m / Animator Speed 1)
걷기 약 0.182m/s, 뛰기 약 0.735m/s.
모델 배율과 재생 속도를 바꾸면 이동 속도도 같은 비율로 조절합니다.
제자리 루프에서는 지지발이 몸 기준으로 뒤로 움직이며, 캐릭터의 전진 이동과 합쳐져 접지됩니다.
실제 이동 속도가 맞지 않으면 발 미끄러짐이 생기며 경사면은 별도의 Foot IK가 필요합니다.

Blender
Action Editor에서 Mushroom_Walk_InPlace 또는 Mushroom_Run_InPlace를 선택합니다.
걷기 1–29, 뛰기 1–19. 마지막 프레임은 첫 프레임과 같은 루프 끝점입니다.
무대/카메라/조명은 미리보기용이며 FBX에는 들어 있지 않습니다.

확인 범위
Blender에서 바닥 관통, 발목 연결, 루프 끝점, 원형/UV 보존 및 FBX 재가져오기를 확인했습니다.
Unity Editor의 Humanoid Avatar 유효성과 리타게팅 재생은 직접 검증하지 않았습니다.
Generic 설정으로 원래 모션을 비교할 수도 있습니다.
신규 뼈대이므로 기존 본 이름을 참조하는 스크립트는 다시 연결해야 합니다.
원본 업로드 파일은 변경하지 않았습니다.
