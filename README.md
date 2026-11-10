# DemonWeave

**面向策划与 Technical Designer 的 Unity 任务编排工具。**

DemonWeave 将任务走向、目标和生命周期组织为可编辑的图，并通过业务组件连接场景中的玩法。策划组合已有行为、选择对象、调整参数与分支；TD 使用 C# 实现能力，并控制哪些内容开放给作者配置。

项目目标是让人工编辑、文本配置与 AI 辅助修改使用同一份任务定义，使内容能够被理解、审查、修改和运行验证。

> **开发中 · 0.1.0**  
> 当前以可玩的救援白盒验证制作流程。作者体验、干净项目接入和 Player 构建仍在完善。

## 当前能力

- **任务图编辑** — Collection → Quest → Node 层级，前置连线、条件、目标、进入与完成行为。右键画布创建节点，再手动连接前置。
- **场景对象绑定** — 拖入业务组件，或按 Hierarchy 浏览、搜索场景对象；查看当前集合中共用实例的节点。
- **C# 能力声明** — 使用 `QuestEntity<TResult>` 声明参数和业务结果，每次激活拥有独立执行状态，并可从编辑器定位实现。
- **任务生命周期** — 条件重评估、目标进度、前置与父子关系、完成和失败处理。运行实例使用定义副本。
- **运行定位** — 查看节点前置状态、相关事实的当前值与期望值，以及最近执行记录。
- **内容交换** — JSON 任务定义、CSV 导出与差异导入，支持编辑撤销。
- **AI 提案入口** — 生成或粘贴局部内容提案，经过版本检查、校验和差异审查后应用。端到端 AI 制作能力仍待验收。

## 体验救援任务

本仓库包含 Unity 工程、嵌入式包和玩法样本。建议先打开完整工程体验。

```bash
git clone https://github.com/DemonViglu/DemonWeave.git
```

1. 使用 **Unity 6000.5.5f1** 打开仓库根目录，等待包解析和脚本编译。
2. 打开 `Assets/AreaRescue/Scenes/CourtyardRescue.unity`。
3. 双击 `Assets/AreaRescue/Content/RescueQuest.asset`，进入 `Rescue the captive` 的节点图。
4. 进入 Play，聚焦 Game View。点击地面移动，点击敌人靠近并攻击，点击同伴交互。WASD 可取消自动移动。

样本包含进入庭院、Timeline 开门、生成守卫、战斗、救出同伴、对白，以及玩家死亡后的恢复分支。

### 第一次修改

将“战败后自动回档”改为“等待玩家手动重试”。

1. 退出 Play，选择 `Return after defeat`。
2. 在 Configuration 中把 `Mode` 从 `Restore Immediately` 改成 `Wait For Player`。
3. 保存任务和场景。内部的重试提示文字可在 Prison Encounter 的 RescueEncounter 组件 Inspector 中修改。
4. 再次运行，让守卫击败玩家。检查提示文字，并点击重试恢复。

任务资产决定战败后的处理方式，绑定的遭遇实例保存提示文字。双击战败节点可以打开 `RescueRecoveryEntity`，其执行对象调用现有检查点逻辑。

## 创建自己的任务

Project → Create → DemonWeave → Gameplay Component 生成新的 Entity 模板。

- 使用结果枚举声明输出，例如 Done、Failed。
- Entity 的序列化字段保存作者配置，CheckConfiguration 检查引用和参数。
- CreateExecution 为每次激活返回独立执行对象。
- 执行对象实现 OnStart / OnTick / OnStop(reason)，用 Finish(结果) 报告一次业务结果。

编辑器只显示 Target、Configuration 和 Results。作者手动连结果引脚，完成与取消由框架管理。旧 QuestAction / QuestCondition 任务保留兼容路径。

TD 示例源码位于 `Assets/AreaRescue/Runtime/Quest`，当前仅保留 CourtyardRescue 作为人工验收场景。

## 编排与业务实现

| 作者要表达的内容 | 所在位置 |
| --- | --- |
| 哪些条件满足后开始、等待什么目标、完成后接哪一步 | Quest 定义 |
| 此次使用哪个业务对象、打开哪扇门、调用参数是多少 | 节点绑定与参数 |
| 战斗、移动、生成、动画、对白及异步过程如何执行 | C# / Timeline / 业务系统 |
| 世界恢复、取消和迟到回调如何清理 | 业务组件与宿主 |

一个 Node 绑定一个业务 Entity。条件和动作菜单只读取这个组件显式公开的能力。多个节点可以共用同一实例，也可以绑定同一类型的不同实例。

方法参数属于每次调用的配置。组件内部引用与资源通过场景或 Prefab 装配。修改实例字段会影响使用它的调用者，修改共享 C# 实现会影响使用该类型的实例。

## 数据与 AI

QuestBook 内保存 JSON 任务定义，图和 CSV 编辑同一组内容。前置关系保存在 `After` 字段，对象归属保存在 `EntityRole`，动作保存稳定能力 ID 与参数。场景中的实际对象引用由 `QuestSceneHost` 持有。

`More → Export CSV / Import CSV...` 提供文本内容交换，导入先预览差异。当前导入要求完整的目标配置；编辑器中尚未配完的空节点可以保存，但不能直接作为完整 CSV 内容导入运行。

`AI Changes` 提供上下文导出、提案审查及应用入口。模型连接读取本机环境变量 `DEMONWEAVE_LLM_ENDPOINT`、`DEMONWEAVE_LLM_MODEL` 和 `DEMONWEAVE_LLM_API_KEY`。密钥应保留在本机环境中。

AI 提案目前面向任务内容修改。新增业务代码需要编译，场景装配与实际效果需要 Unity 验证。人工制作路径是后续评估 AI 接手能力的基准。

## 在其他项目中使用

只安装 `Packages/com.demon.weave/package.json` 即可使用 Quest。它不依赖 Startup、UI、Dialogue 或 Timeline。官方依赖由包清单声明。

- Startup 是可选的项目启动入口。
- UI 是可选的 uGUI 窗口栈，不依赖 Startup。
- Dialogue 是可选的对话会话与基础表现，不依赖 Quest 或 UI 栈。
- `Assets/AreaRescue` 和 `Assets/Gameplay/Startup` 是本工程业务，不是安装 Quest 的必需内容。

通过 Package Manager 的 **Add package from disk** 安装需要的包。业务 Entity 由项目实现；保存格式、场景恢复、输入和光标策略也由项目决定。Quest 保留运行状态与条件接口，不包含项目存档协议。干净项目的完整人工制作流程仍待验收。

## 当前边界

- 对象绑定限同一已加载场景，能力目录来自场景 Entity；完整绑定校验需要场景上下文。
- 默认作者路径以事实查询为主。内核的事件计数能力与默认声明、上报入口尚未完全接通。
- 直接动作调用采用同步签名，长时操作由业务代码管理。
- 图布局随任务资产保存；拖动后使用 Save 保存。
- 当前验证主要覆盖 Unity Editor / Play，完整 IL2CPP Player 发布尚待验证。

近期优先完善独立 Quest 产品的人工制作、调试和跨项目接入。智能 NPC 与更多样的动态剧情属于后续研究方向。

## 目录

```text
Packages/
  com.demon.weave/             Quest 内核、Unity 适配与编辑器
  com.demonviglu.startup/      应用启动与生命周期
  com.demonviglu.ui/           UI 基础能力
  com.demonviglu.dialogue/     对白能力
Assets/
  AreaRescue/                 救援白盒与业务内容
  Gameplay/Startup/           样本应用入口
```

## 反馈

欢迎通过 [Issues](https://github.com/DemonViglu/DemonWeave/issues) 反馈制作过程中的问题。请附上 Unity 版本、复现步骤，以及“想完成什么、预期在哪里操作、实际发生了什么”。编辑器截图和第一条相关错误有助于定位问题。


Quest 包位于 `Packages/com.demon.weave`，仅依赖 Newtonsoft JSON。营救样例的战斗、Timeline、Checkpoint 和对白呈现位于 `Assets/AreaRescue/Support`，不随 Quest 包安装。场景的 World State 组件在 Play 时提供当前事实值开关，可直接验证条件暂停、继续与中断；不改写初始资产。
