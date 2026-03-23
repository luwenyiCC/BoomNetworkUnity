# 踩坑记录技能

开发过程中遇到的所有 bug 和陷阱，新会话必读。

## 网络层

### InputBuffer 存引用不存值
```
现象: 预测永远不匹配，每帧都回滚
原因: InputBuffer.Set(frame, pid, input) 存了 input 的引用
      外部 _inputBuf 是复用的，所有帧指向同一个 buffer
修复: Set 时 Array.Copy 复制数据
教训: 帧级数据必须值语义
```

### 预测帧率 60fps 和服务器 20fps 不对齐
```
现象: 预测 8 帧后停止，服务器帧到达时全部触发回滚
原因: PredictFrame 在 Unity Update 中调用（60fps）
      服务器帧率 20fps，帧号不对齐
修复: PredictionManager 内部用 _frameAccumulator 节流到服务器帧率
教训: 预测频率必须和服务器帧率一致
```

### SessionBind 自动分房 + JoinRoom 重复
```
现象: 一个连接同时在两个房间，收到两个房间的帧数据
原因: handleSessionBind 内有 AutoAssignRoom，JoinRoom 又创建新房间
修复: handleSessionBind 不再自动分房（去掉 AutoAssignRoom）
      服务器启动不加 -autoroom 参数
教训: 分房策略必须单一入口
```

### 客户端 FrameInput 60fps 触发限流
```
现象: 帧同步开始 200ms 后断连
原因: SendInput 在 Unity Update 中调用（60fps），超过服务器限流
修复: 输入节流到服务器帧率（50ms 间隔）
      服务器限流提高到 500 msg/sec + 20 burst
教训: 客户端发送频率必须受控
```

## Unity 兼容性

### Dictionary 遍历中修改导致崩溃
```
现象: Unity PlayMode 测试全部崩溃
原因: foreach 遍历 Dictionary 时修改值（_pendingRequests[key] = req）
      .NET 8 可以容忍，Unity Mono 不行
修复: 三阶段处理（收集 key → 更新 → 处理超时）
教训: Unity Mono 比 .NET 8 更严格
```

### BinaryPrimitives.WriteSingleLittleEndian 不存在
```
现象: Unity 编译错误
原因: .NET 6+ API，Unity 2022 的 .NET Standard 2.1 没有
修复: 改用 BitConverter.TryWriteBytes
教训: UPM 包代码必须兼容 Unity 的 .NET 版本
```

### Nullable tuple .Value 访问
```
现象: Unity 编译错误 CS1061
原因: (int, int, int)? 在 Unity 2022 中需要 .Value 才能访问成员
修复: stats.Value.ahead 而不是 stats.ahead
教训: nullable value type 在 Unity 中更严格
```

### UPM 包缺 .meta 文件
```
现象: "has no meta file, asset will be ignored"
原因: git 仓库中的 UPM 包没有 .meta 文件
修复: Python 脚本批量生成 .meta
教训: UPM 包的每个文件和目录都需要 .meta
```

## Demo 层

### OnReady 回调不是一次性
```
现象: 手动重连 Arrows 时 WASD 也被连上
原因: ConnectSequential 注册的 OnReady 回调永久挂在 Person 上
      重连触发 OnReady → 旧的 ConnectSequential 回调连下一个
修复: OnReady 注册一次性回调（lambda 赋 null 后再 invoke）
教训: 事件回调生命周期要和注册者一致
```

### Authority 迁移遗漏
```
现象: 第一个 Person 断线后方块不动
原因: OnFrame 只绑定在 authority Person 上，断线后没人驱动
修复: 改为帧号去重（_lastProcessedFrame），任何 Person 的帧都处理
教训: 不要把驱动逻辑绑定在可能断线的对象上
```

### AutoAssignRoom 竞态
```
现象: 两个并发 JoinRoom 各创建一个新房间
原因: AutoAssignRoom 内解锁→CreateRoom→加锁的间隙
修复: createRoomLocked 在锁内完成
教训: 查找+创建必须原子
```

### 快照重连重置在线玩家位置
```
现象: A 重连后 B 的位置跳回旧位置
原因: LoadWorldSnapshot 设置所有 Entity 位置（包括在线的 B）
      快照是旧的，B 在快照后移动了
修复: Demo01 不加载快照（live 帧和补帧的帧号去重冲突）
      补帧从 snapshotFrame 开始但和 live 帧交错导致去重失效
教训: 快照恢复 + 补帧回放是原子操作，中间不能穿插 live 帧
```

## Go 服务器

### 房间清理后玩家仍然尝试重连
```
现象: 重连成功但房间不推帧
原因: 房间被清理后 playerRoomMap 没清理
      重连找到已停止的 Room 对象
修复: 清理房间时同步清理 playerRoomMap
教训: 映射关系双向清理
```

### Go 服务器在 Unity 进程内启动
```
现象: Play 时服务器停止
原因: Unity domain reload 触发 OnDisable → StopServer
      EditorPrefs 的 PID 在 reload 后丢失
修复: 改为在 Terminal.app 中启动（独立进程）
教训: 不要在 Unity Editor 进程内管理外部长生命周期进程
```
