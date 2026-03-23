# Go 帧同步服务器技能

## 服务器位置

```
/Users/boom/Demo/BoomNetwork/svr/
├── cmd/
│   ├── echo/main.go          ← Echo 测试服务器
│   ├── framesync/main.go     ← 帧同步服务器（Demo 用这个）
│   ├── stress/main.go        ← Go 压测工具（Go 客户端 + 内嵌服务器）
│   └── kcpstress/main.go     ← KCP 压测
├── codec/                     ← 消息编解码（和 C# 端线格式一致）
├── framesync/                 ← 帧同步核心（Room + RoomManager + Protocol）
├── session/                   ← Router + Handler
└── transport/                 ← TCP/KCP 服务器 + 安全配置
```

## 启动命令

```bash
# 开发用（Demo）
cd /Users/boom/Demo/BoomNetwork/svr
go run ./cmd/framesync/ -addr=:9000 -proto=tcp -ppr=2

# 参数说明
-addr=:9000     # 监听地址
-proto=tcp      # 协议: tcp 或 kcp
-ppr=2          # players per room to auto-start（设大数值则手动 RequestStart）
-token=xxx      # 鉴权 token（空=不鉴权）
-autoroom       # 启用 SessionBind 自动分房（压测用，Demo 不加）
```

## 协议常量（C# 和 Go 必须一致）

```
Cmd 分配:
  SessionBind       = 1
  SessionBindRsp    = 2
  RequestStart      = 3
  StartFrameSync    = 4
  FrameInput        = 5
  PushFrames        = 6
  StopFrameSync     = 21
  Heartbeat         = 7
  HeartbeatRsp      = 8
  Reconnect         = 9
  ReconnectRsp      = 10
  GetRooms          = 60
  GetRoomsRsp       = 61
  CreateRoom        = 62
  CreateRoomRsp     = 63
  JoinRoom          = 64
  JoinRoomRsp       = 65
  LeaveRoom         = 66
  LeaveRoomRsp      = 67
  UploadSnapshot    = 70
  UploadSnapshotRsp = 71
```

## Room 生命周期

```
CreateRoom → Room 创建（Waiting 状态）
JoinRoom → 玩家加入
RequestStart → Room.Start()（开始推帧 20fps）
玩家断线 → DisconnectKeepAlive=120s 保留
所有人离线超过 120s → Room 自动清理
```

## Room 帧缓冲

```go
type Room struct {
    frameBuffer    []*CachedFrame  // 环形缓冲区，最近 200 帧
    snapshotData   []byte          // 最新快照数据
    snapshotFrame  uint32          // 快照对应的帧号
}
```

- 快照: 客户端每 100 帧上传一次，Room 存最新一份
- 重连: 发快照 + 从快照帧开始补帧
- 帧缓冲大小 200 = 10 秒（20fps × 10s）

## 安全配置

```go
type SecurityConfig struct {
    MaxMessageSize    int    // 64KB
    MaxMessagesPerSec int    // 500（默认，Demo 用）
    BurstAllowance    int    // 20（突发容忍）
    RequireAuth       bool
    AuthToken         string
}
```

## 消息路由

```go
router := session.NewRouter()
router.On(framesync.CmdSessionBind, handleSessionBind)
router.On(framesync.CmdHeartbeat, handleHeartbeat)
router.On(framesync.CmdReconnect, handleReconnect)
router.On(framesync.CmdGetRooms, handleGetRooms)
router.On(framesync.CmdCreateRoom, handleCreateRoom)
router.On(framesync.CmdJoinRoom, handleJoinRoom)
router.On(framesync.CmdLeaveRoom, handleLeaveRoom)
router.On(framesync.CmdRequestStart, handleRequestStart)
router.On(framesync.CmdFrameInput, handleFrameInput)
router.On(framesync.CmdUploadSnapshot, handleUploadSnapshot)
```

## Unity Editor 集成

ServerWindow（`Assets/Scripts/Demo/Editor/ServerWindow.cs`）:
- 菜单: BoomNetwork > Server Window
- 点 "Start Server" → 在 macOS Terminal.app 中启动 Go 服务器
- 点 "Stop Server" → kill 端口 9000
- 不在 Unity 进程内管理 Go 进程（避免 domain reload 问题）

## 测试脚本

```bash
# 全量自动化测试（7 项）
cd /Users/boom/Demo/BoomNetwork && ./test.sh

# 一键验收（包含服务器启停）
./verify.sh

# 性能基准
./bench.sh

# 压测
cd svr && go run ./cmd/stress/ -rooms=750 -players=4 -duration=10s
```

## 踩坑

1. **-autoroom 不要在 Demo 中使用**: SessionBind 自动分房 + JoinRoom 手动分房会导致一个连接在两个房间
2. **服务器不要在 Unity 进程内启动**: domain reload 会杀死子进程
3. **限流 500 msg/sec**: 客户端输入必须节流到 20fps，否则触发限流断连
4. **房间清理 120s**: 断线后 120 秒房间才清理，测试时注意旧房间影响
