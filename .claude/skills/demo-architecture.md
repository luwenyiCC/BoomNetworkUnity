# Demo 架构技能

## 项目概述

BoomNetworkUnity 是 BoomNetwork 帧同步网络库的 Unity Demo 项目。
- Unity 版本: 2022.3 LTS
- 网络库: BoomNetwork（通过 UPM git URL 导入）
- UI 框架: Odin Inspector（不用 UGUI Canvas）
- 渲染: 2D（SpriteRenderer 方块 + 正交摄像机）

## 仓库关系

```
BoomNetwork (库)                    BoomNetworkUnity (Demo)
/Users/boom/Demo/BoomNetwork        /Users/boom/Demo/BoomNetworkUnity
github: luwenyiCC/BoomNetwork       github: luwenyiCC/BoomNetworkUnity
branch: dev1.0                      branch: main

UPM 包路径: unity/com.boom.boomnetwork
Unity manifest: "com.boom.boomnetwork": "https://github.com/luwenyiCC/BoomNetwork.git?path=unity/com.boom.boomnetwork#dev1.0"
```

## 目录结构

```
Assets/Scripts/
├── Demo/                          ← 共享代码（两个 Demo 都用）
│   ├── Config/
│   │   └── NetworkConfig.cs       ← ScriptableObject 网络配置
│   ├── Network/
│   │   └── Person.cs              ← 网络身份（一个 TCP 连接 = 一个 Person）
│   ├── Entity/
│   │   └── PlayerEntity.cs        ← 2D 方块渲染（SpriteRenderer + 方向箭头）
│   ├── Input/
│   │   ├── IInputProvider.cs      ← 输入接口
│   │   ├── KeyboardInput.cs       ← WASD / Arrows / IJKL
│   │   ├── BotInput.cs            ← AI 随机移动
│   │   ├── NoneInput.cs           ← 空输入
│   │   └── InputProviderFactory.cs
│   └── Editor/
│       └── ServerWindow.cs        ← Go 服务器启动窗口
│
├── Demo01/                        ← 基础帧同步（无预测）
│   ├── BasicPersonManager.cs      ← 控制面板 + 帧驱动
│   └── BasicGameHUD.cs            ← OnGUI 状态显示
│
└── Demo02/                        ← 预测回滚（进阶）
    ├── PredictionPersonManager.cs ← 含预测逻辑的控制面板
    ├── PredictionGameHUD.cs       ← 含回滚统计的 HUD
    └── DemoSimulation.cs          ← ISimulation 实现
```

## 核心类职责

### Person（网络身份）
```
一个 Person = 一个完整的网络客户端实例
- 持有: Transport + Session + ConnectionManager + RoomClient + FrameSyncClient
- 状态: Idle → Connecting → Connected → InRoom → Syncing → Disconnected
- 断线保留: PlayerId / RoomId / LastFrameNumber（用于重连）
- 重连: 有旧身份时发 Reconnect 命令恢复，不重新分配 PID
```

### PersonManager / BasicPersonManager
```
组装 Person + Input + Entity 的管理器
- Odin Inspector 面板控制
- 帧号去重: _lastProcessedFrame，防止多 Person 重复处理同一帧
- 帧同步驱动: OnFrame → DecodeInput → ApplyMove → UpdateEntity
- 输入节流: 按服务器帧率（20fps）发送，不是 Unity Update 率
- 空输入不发送: dir.sqrMagnitude < 0.001f 时跳过 SendInput
```

### PlayerEntity（渲染）
```
纯渲染组件，不知道网络
- 运行时生成 Sprite（不依赖外部资源）
- 彩色方块 + 方向三角形箭头
- 屏幕环绕: 超出正交摄像机边界从另一端出现
- SetPosition(x, y) / SetDirection(dx, dy)
```

## 帧同步数据流

```
传统模式（Demo01）:
  InputProvider.GetMoveInput() → Vector2(dx, dy)
  → EncodeInput (8 bytes: 2 floats)
  → Person.SendInput → 发给服务器
  → 服务器组帧 PushFrames → 广播
  → Person.OnFrame → DecodeInput → ApplyMove → Entity.SetPosition

预测模式（Demo02，未验证）:
  InputProvider.GetMoveInput() → EncodeInput
  → PredictionManager.PredictFrame（本地预测执行）
  → Person.SendInput → 发给服务器
  → 服务器帧到达 → PredictionManager.ProcessServerFrames
  → 一致: 推进 confirmed frame
  → 不一致: LoadState → 重新执行 → 修正位置
```

## 输入编解码

```csharp
// 编码: Vector2 → 8 bytes
static void EncodeInput(Vector2 dir, byte[] buf)
{
    BitConverter.TryWriteBytes(buf.AsSpan(0, 4), dir.x);  // [0-3] float x
    BitConverter.TryWriteBytes(buf.AsSpan(4, 4), dir.y);  // [4-7] float y
}

// 解码: 8 bytes → Vector2
static Vector2 DecodeInput(ReadOnlySpan<byte> buf)
{
    float x = BitConverter.ToSingle(buf.Slice(0, 4));
    float y = BitConverter.ToSingle(buf.Slice(4, 4));
    return new Vector2(x, y);
}
```

## 快照格式

```
TakeWorldSnapshot → byte[]
  [PlayerCount: 2 bytes]
  per player:
    [PlayerId: 4 bytes]
    [PosX: 4 bytes (float)]
    [PosY: 4 bytes (float)]

LoadWorldSnapshot ← byte[]
  遍历解析，设置每个 Entity 的位置
```

## 重连流程（Demo01）

```
1. Person.Disconnect() → 保留 PlayerId/RoomId/LastFrame
2. Person.Connect() → 检测到有旧身份 → 发 Reconnect(playerId, lastFrame)
3. 服务器认可 → ReconnectRsp(success=1, roomId, serverFrame, snapshotFrame, snapshotData)
4. Demo01: 不加载快照（避免重置在线玩家位置）
5. Person 恢复为 Syncing，继续收帧
6. 补帧通过帧号去重自然处理
```

## 已知限制

- Demo01 重连不加载快照（live帧和补帧去重冲突）
- Demo02 预测模式未验证（默认 enablePrediction=false）
- 输入用 float（跨平台不确定性，Demo 可接受）
- 同一 Unity 进程两个客户端（非真实多设备）
