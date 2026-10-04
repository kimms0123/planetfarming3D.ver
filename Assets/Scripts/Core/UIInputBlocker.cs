using System.Collections.Generic;
using UnityEngine;

namespace FarmingSystem.Core
{
    /// <summary>
    /// 상점처럼 게임 입력(이동, 농사, 핫바, 인벤토리)을 막아야 하는 UI가 열려 있는지 관리한다.
    /// 여러 UI가 동시에 막아도 꼬이지 않도록 요청한 쪽(owner) 단위로 기록한다.
    /// </summary>
    public static class UIInputBlocker
    {
        private static readonly HashSet<object> owners = new HashSet<object>();

        public static bool IsBlocked => owners.Count > 0;

        public static void Block(object owner)
        {
            if (owner != null) owners.Add(owner);
        }

        public static void Unblock(object owner)
        {
            if (owner != null) owners.Remove(owner);
        }

        // Enter Play Mode 설정에서 도메인 리로드를 꺼도 이전 플레이의 잠금이 남지 않게 초기화
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnLoad() => owners.Clear();
    }
}