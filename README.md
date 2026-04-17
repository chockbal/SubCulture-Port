UniTask & UniRx 기반 모듈형 전투 시스템 (Core Logic)
본 저장소는 유니티(Unity) 환경에서 고성능 전투 시스템을 구축하기 위해 설계된 핵심 모듈들을 포함하고 있습니다. 데이터-로직-액터 레이어의 엄격한 분리를 통해 유지보수성과 확장성을 극대화하는 데 집중했습니다.

## 📺 Gameplay Video & Portfolio
* 🎬 **[플레이 영상 보기 (YouTube/Drive)]**(https://youtu.be/N4EuXiwo5aY?si=AnNApsUWJ4IveE52)
* 📄 **[3D 서브컬쳐 게임 포트폴리오 PDF](./포트폴리오_3D_서브컬쳐게임.pdf)**

🛠 Tech Stack
Engine: Unity 2022.3+ (URP)

Libraries: UniTask, UniRx, Addressables, DOTween

Language: C#

🌟 Key Features
Async-Based System (UniTask): Update() 콜백을 최소화하고 비동기 루프 기반의 상태 머신을 구축하여 CPU 오버헤드를 최적화했습니다.

Reactive Architecture (UniRx): 상태 이상 및 데이터 변화를 이벤트 기반으로 전파하여 객체 간 결합도를 낮췄습니다.

Object Pooling & Warming-up: Addressables와 연동된 풀링 시스템을 통해 런타임 프레임 드랍(Spike)을 방지했습니다.

Bitmask State Control: 다중 상태 이상 로직을 비트 연산으로 통합 관리하여 연산 효율을 높였습니다.

📂 Module Descriptions
SCharacterData: 캐릭터 고유 능력치 및 세이브 데이터 구조 정의

DataOne: 비동기 리소스 로딩 및 전역 데이터 파이프라인 관리

SPoolManager: 프리로드(Preload) 전략이 포함된 오브젝트 풀링 시스템

StateController: 비트마스크 기반의 상태 이상 로직 및 옵저버 제공

Gun: 무기 레벨별 동적 오프셋 및 투사체 발사 제어

Character: 비동기 상태 머신을 활용한 최종 액터 제어