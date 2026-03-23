# 待做工作

## 当前进度

Demo01-Basic: ✅ 基本可用（传统帧同步，无预测）
Demo02-Prediction: ⚠️ 代码完成，未 CR 验证

## 下一步任务（按优先级）

### 1. CR + 修复 Demo02 预测回滚
```
状态: 代码写完，未在 Unity 中验证
问题:
  - 预测帧率节流已实现但未测试
  - InputBuffer 复制已修复但未验证
  - DemoSimulation 用平行数组替代 Dictionary 已完成
文件:
  - Assets/Scripts/Demo02/PredictionPersonManager.cs
  - Assets/Scripts/Demo02/PredictionGameHUD.cs
  - Assets/Scripts/Demo02/DemoSimulation.cs
  - UPM: Runtime/Core/Prediction/PredictionManager.cs
  - UPM: Runtime/Core/Prediction/InputBuffer.cs
  - UPM: Runtime/Core/Prediction/SnapshotBuffer.cs
```

### 2. B: 帧同步正确性校验
```
目标: 双端 StateHash 对比，检测不一致
方案:
  - 每 N 帧计算 StateHash（所有实体位置的 hash）
  - 两个 Person 对比 hash
  - 不一致时在 HUD 显示红色警告
  - 不需要定点数（周期状态同步 + Hash 校验足够）
```

### 3. C: 帧录制回放
```
目标: 录制帧数据到文件，离线回放
方案:
  - 录制: 每帧写入 (frameNumber, inputs[]) 到文件
  - 回放: 读取文件，按帧率执行
  - 用途: 排查 desync、重现 bug
  - 需要 ISimulation 接口（Demo02 已有）
```

### 4. 移植回 RunningCat
```
目标: 替换 /Users/boom/work/HWMain_2022_Cat 中的 NetworkAdapter
路径: Assets/LocalResources/RunningCat/unPack/Script/
相关技能: frameroot-system, input-sync, actor-hsm
```

## 设计决策记录

### 为什么 Demo01 不加载快照
```
问题: 补帧和 live 帧在同一进程交错到达
      帧号去重无法区分来源
      补帧被 live 帧的高帧号 skip 掉
结论: Demo01 跳过快照加载，接受重连后轻微位置偏差
      正确的快照+补帧属于 Demo02 的预测模式
```

### 为什么不用定点数
```
问题: 能覆盖所有生产代码吗？
答案: 不能。Unity 物理/动画/寻路全是 float
结论: 用"关键路径整数 + 周期状态同步 + Hash 校验"方案
      Demo 中 float 可接受（同进程同平台）
```

### 为什么 Demo 用 2D 不用 3D
```
原因: 降低入门门槛
      3D 需要摄像机/Y轴/光照等无关复杂度
      2D 上下左右移动，所见即所得
```
