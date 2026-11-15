상어 캐릭터 — Bouncy Walk In-Place

포함 파일
- Shark_BouncyWalk_InPlace.fbx: 모델, 보강한 뼈대, 걷기 애니메이션, 내장 텍스처
- Textures/Shark_BaseColor.png: 원본 FBX의 텍스처 추출본
- Shark_BouncyWalk_Source.blend: 수정 가능한 Blender 4.2 원본 및 미리보기 무대
- Shark_BouncyWalk_Preview.mp4: 사선 / 측면 반복 미리보기
- Motion_Data.json / Validation.json: 타이밍, 보폭 및 Blender 확인 결과

모션
30 FPS, 1–29 프레임(28프레임 간격 = 0.933333초). 29프레임은 1프레임과 동일한 루프 끝점입니다.
제자리 걷기이며 루트의 전후 이동은 없습니다. 좌우 발 교대, 약 2cm 몸통 바운스,
12도 발끝 외회전, 작은 골반 회전과 지연된 머리 움직임을 적용했습니다.
바운스 중 꼬리가 지면을 뚫지 않도록 꼬리는 살짝 들어 유지합니다.
원본의 짧은 지느러미와 발에 맞춰 관절과 가중치를 새로 구성했습니다.
매우 짧은 다리의 접지를 우선했으므로 무릎 등의 각도는 요청 수치와 정확히 같지 않습니다.
모델의 정점 위치/UV/텍스처는 유지했으며 셔츠와 액세서리에는 별도 흔들림을 넣지 않았습니다.
참고 YouTube 영상은 직접 재생 확인하지 못해 요청서의 움직임 설명을 기준으로 제작했습니다.

Unity 6 가져오기
1. FBX와 Textures 폴더를 Assets 아래에 함께 넣습니다.
2. FBX 선택 > Rig > Animation Type: Humanoid.
3. Avatar Definition: Create From This Model > Apply.
4. Configure에서 Hips, Spine, Chest, Neck, Head 및 좌우 팔/다리의 매핑을 확인합니다.
   뼈 이름은 LeftUpperArm, LeftLowerArm, LeftHand, LeftUpperLeg, LeftLowerLeg,
   LeftFoot 등입니다. 반대쪽은 Right 접두사입니다. Root는 Hips에 매핑하지 않습니다.
   필요하면 Pose > Enforce T-Pose 후 Apply/Done을 누릅니다.
5. Animation 탭에서 Import Animation, Loop Time, Loop Pose를 켭니다.
   프레임 범위는 가져온 클립 전체(28프레임 간격)를 사용합니다.
   Root Transform Rotation과 Position XZ는 Bake Into Pose를 켭니다.
   Position Y는 Bake Into Pose를 켜고 Based Upon: Feet로 확인합니다.
6. Animator에 클립을 넣고 Apply Root Motion은 끕니다.
7. URP에서 재질이 분홍색이면 URP/Lit 재질을 만들고 동봉 PNG를 Base Map에 연결합니다.

이동 속도와 발 미끄러짐
원본 크기(높이 약 0.95m), Animator Speed 1 기준 권장 이동 속도는 약 0.129m/s입니다.
모델 배율이나 재생 속도를 바꾸면 이동 속도도 같은 비율로 조절합니다.
제자리 걷기는 지지발이 몸 기준으로 뒤로 이동합니다. 캐릭터의 전진 이동과 합쳐져 접지됩니다.
정지 상태에서 루프만 재생하면 지지발이 뒤로 움직이는 것이 정상입니다.
완전한 월드 접지는 실제 이동 속도와 함께 확인해야 하며, 경사면에는 별도 Foot IK가 필요합니다.

확인 범위
Blender에서 루프 끝점, 발바닥 높이 및 FBX 재가져오기를 확인했습니다.
Unity Editor의 Humanoid Avatar 유효성과 리타게팅 결과는 직접 검증하지 않았습니다.
원본의 단순 뼈대를 보강한 신규 리그이므로 기존 뼈 이름을 참조한 스크립트는 다시 연결해야 합니다.
원본 업로드 파일은 변경하지 않았습니다.
