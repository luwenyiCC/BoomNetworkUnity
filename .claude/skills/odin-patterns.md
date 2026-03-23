# Odin Inspector 使用规范

## 为什么用 Odin

Demo 不用 UGUI Canvas 搭 UI，全部通过 Odin Inspector 面板操作。
- 零 UI 搭建工作量
- Inspector 即控制面板
- 运行时可操作（Play 模式下点按钮）

## 安装位置

```
Assets/Plugins/Sirenix/
```

## 常用模式

### 表格展示列表

```csharp
[TitleGroup("Persons")]
[TableList(ShowIndexLabels = false, AlwaysExpanded = true)]
public List<PersonSlot> persons = new();

[Serializable]
public class PersonSlot
{
    [TableColumnWidth(100)]
    public InputMode inputMode;

    [TableColumnWidth(60)]
    public Color color;

    [ReadOnly, TableColumnWidth(80)]
    public string state;

    [ReadOnly, TableColumnWidth(40)]
    public int pid;

    [ReadOnly, TableColumnWidth(60)]
    public uint frame;

    [Button("Conn"), TableColumnWidth(70)]
    public void BtnConnect() { ... }

    [Button("Disc"), TableColumnWidth(70)]
    [GUIColor(0.8f, 0.3f, 0.3f)]
    public void BtnDisconnect() { ... }
}
```

### 操作按钮组

```csharp
[TitleGroup("Actions")]
[ButtonGroup("Actions/Btns")]
[GUIColor(0.3f, 0.8f, 0.3f)]
[Button("Connect All", ButtonSizes.Large)]
public void ConnectAll() { ... }

[ButtonGroup("Actions/Btns")]
[GUIColor(0.8f, 0.3f, 0.3f)]
[Button("Disconnect All", ButtonSizes.Large)]
public void DisconnectAll() { ... }
```

### 只读显示区

```csharp
[TitleGroup("Sync")]
[ShowInInspector, ReadOnly, HideLabel]
public string syncStatus => _syncStatusText;
```

### 配置区

```csharp
[TitleGroup("Config")]
public NetworkConfig config;

[TitleGroup("Config")]
[Range(0.1f, 10f)]
public float moveSpeed = 1f;

[TitleGroup("Config")]
public bool enablePrediction = false;
```

### 信息提示

```csharp
[InfoBox("Quick Start:\n1. BoomNetwork > Server Window > Start Server\n2. Play this scene\n3. Click [Connect All]\n4. Click [Start Game]\n5. WASD = Player 1, Arrows = Player 2")]
public class BasicPersonManager : MonoBehaviour { ... }
```

### 滚动日志

```csharp
[TitleGroup("Log")]
[ShowInInspector, ReadOnly, MultiLineProperty(5)]
public string logText => _logText;

[TitleGroup("Log")]
[Button("Clear Log")]
public void ClearLog() { _logText = ""; }
```

### 房间列表表格

```csharp
[TitleGroup("Room")]
[TableList(ShowIndexLabels = false)]
[ShowInInspector, ReadOnly]
public List<RoomEntry> roomList = new();

[Serializable]
public class RoomEntry
{
    [ReadOnly] public int roomId;
    [ReadOnly] public string players;
    [ReadOnly] public string status;

    [Button("Join All")]
    public void BtnJoinAll() { ... }

    [Button("Select")]
    public void BtnSelect() { ... }
}
```

## 注意事项

1. **MonoBehaviour 继承**：Odin 的 `SerializedMonoBehaviour` 不是必须的，普通 `MonoBehaviour` + Odin Attribute 就能用
2. **运行时按钮**：`[Button]` 在 Play 模式下可点击，适合做控制面板
3. **ReadOnly 刷新**：`[ShowInInspector]` + property getter 每帧刷新，适合显示状态
4. **GUIColor**：给按钮加颜色区分操作类型（绿=连接，红=断开，黄=开始）
5. **TableList**：展示列表数据最直观，每行一个 PersonSlot
6. **不要用 Odin 的序列化**：Demo 只用 Attribute 做显示，数据用标准 Unity 序列化
