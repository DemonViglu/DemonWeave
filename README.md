# DemonWeave

**面向策划与 Technical Designer 的 Unity 任务编排工具。**

DemonWeave 将任务走向、目标和生命周期组织为可编辑的图，并通过普通 C# 逻辑和对象引用连接 Gameplay。策划组合已有行为、选择对象、调整参数与分支；TD 使用 C# 实现能力，并控制哪些内容开放给作者配置。

项目目标是让人工编辑、文本配置与 AI 辅助修改使用同一份任务定义，使内容能够被理解、审查、修改和运行验证。

> **开发中 · 0.6.0**
> 当前以可玩的救援白盒验证制作流程。作者体验、干净项目接入和 Player 构建仍在完善。

## 当前能力

- **任务图编辑** — Collection → Quest → Node 层级，前置连线、条件、NodeLogic 配置与结果分支。右键画布创建节点，再手动连接前置。
- **场景对象绑定** — 在节点引用参数中拖入对象/组件/资产，或按 Hierarchy 浏览、搜索并定位场景对象；逻辑本身不挂 GO。
- **C# 能力声明** — 使用普通 C# `QuestNodeLogic<TResult>` 与 `QuestLogic` 声明节点参数和业务结果，每次激活拥有独立执行状态，并可从编辑器定位实现。
- **任务生命周期** — 条件重评估、结果路由、前置与父子关系、完成和取消处理。运行实例使用定义副本。
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
2. 在节点面板把 `Mode` 从 `Restore Immediately` 改成 `Wait For Player`。
3. 保存任务和场景。内部的重试提示文字可在 Prison Encounter 的 RescueEncounter 组件 Inspector 中修改。
4. 再次运行，让守卫击败玩家。检查提示文字，并点击重试恢复。

任务资产保存 Mode、Encounter 引用等节点参数，实际 Gameplay 遭遇实例保存提示文字。双击战败节点可以打开 `RescueRecoveryLogic`，其执行对象调用现有检查点逻辑。

## 创建自己的任务

Project → Create → DemonWeave → Node Logic 生成新的 NodeLogic 模板。

- 使用结果枚举声明输出，例如 Done、Failed。
- 普通逻辑类的公开字段声明参数结构，节点 JSON 保存具体值。QuestLogic 声明稳定 ID 和名称。
- 直接在同一个类实现所需 OnStart / OnTick / OnStop；框架每次激活创建新实例，不需要工厂或嵌套类。
- 执行对象实现 OnStart / OnTick / OnStop(reason)，用 Finish(结果) 报告一次业务结果。

编辑器选择 Logic，显示常用参数，其他参数收于 Advanced；结果在图中连线。作者手动连结果引脚，完成与取消由框架管理。仅支持 NodeLogic 路径；旧方法属性、动作/目标列表和 Route/Switch 已移除。

TD 示例源码位于 `Assets/AreaRescue/Runtime/Quest`，当前仅保留 CourtyardRescue 作为人工验收场景。

## 编排与业务实现

| 作者要表达的内容 | 所在位置 |
| --- | --- |
| 哪些条件满足后开始、等待什么目标、完成后接哪一步 | Quest 定义 |
| 此次使用哪个业务对象、打开哪扇门、调用参数是多少 | 节点绑定与参数 |
| 战斗、移动、生成、动画、对白及异步过程如何执行 | C# / Timeline / 业务系统 |
| 世界恢复、取消和迟到回调如何清理 | 业务组件与宿主 |

一个 Node 选择一个普通逻辑类，输出引脚来自结果枚举。ShowInQuest 字段直接显示，其余字段在 Advanced。同一类可以用于多个节点，参数和运行状态独立。

节点参数与引用 ID 保存在 Quest JSON，真实对象由 Runner 映射。每次激活的临时状态在独立 逻辑运行实例。修改节点参数只影响该节点；修改共享 Gameplay 对象仍影响它的使用者。

## 数据与 AI

QuestBook 内保存 JSON 任务定义，图、CSV 与 AI 编辑同一组内容。节点用 `LogicId` 选择代码实现，`Parameters` 保存参数与对象引用 ID。前置关系保存在 `After` / `OpenWhen`；真实 Unity 对象的解析映射由 `QuestSceneHost` 持有。

`More → Export CSV / Import CSV...` 提供文本内容交换，导入先预览差异。当前导入要求完整的目标配置；编辑器中尚未配完的空节点可以保存，但不能直接作为完整 CSV 内容导入运行。

`AI Changes` 提供上下文导出、提案审查及应用入口。AI 同样可以局部修改节点 Parameters，类型与引用均需校验。模型连接读取本机环境变量 `DEMONWEAVE_LLM_ENDPOINT`、`DEMONWEAVE_LLM_MODEL` 和 `DEMONWEAVE_LLM_API_KEY`。密钥应保留在本机环境中。

AI 提案目前面向任务内容修改。新增业务代码需要编译，场景装配与实际效果需要 Unity 验证。人工制作路径是后续评估 AI 接手能力的基准。

## 在其他项目中使用

只安装 `Packages/com.demon.weave/package.json` 即可使用 Quest。它不依赖 Startup、UI、Dialogue 或 Timeline。官方依赖由包清单声明。

- Startup 是可选的项目启动入口。
- UI 是可选的 uGUI 窗口栈，不依赖 Startup。
- Dialogue 是可选的对话会话与基础表现，不依赖 Quest 或 UI 栈。
- `Assets/AreaRescue` 和 `Assets/Gameplay/Startup` 是本工程业务，不是安装 Quest 的必需内容。

通过 Package Manager 的 **Add package from disk** 安装需要的包。业务 NodeLogic 由项目实现；保存格式、场景恢复、输入和光标策略也由项目决定。Quest 保留运行状态与条件接口，不包含项目存档协议。干净项目的完整人工制作流程仍待验收。

## 当前边界

- 对象绑定限同一已加载场景，逻辑目录来自带 QuestLogic 标签的 C# 类型；完整绑定校验需要场景上下文。
- 计数、监听 Gameplay 事件和业务完成判定由 NodeLogic 实现；内核不提供第二套目标解释器。
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


### Minimal Quest sample

Import **Minimal Quest** from Package Manager → Demon Weave Quest → Samples. Open `MinimalQuest.unity` and press Play: the cube rotates, then the quest completes. The sample demonstrates typed scene references, Advanced parameters and an optional AudioSource in one NodeLogic class. It is not registered until imported.

This development project already includes the sample under `Assets/Samples/Demon Weave Quest/0.6.0/MinimalQuest`.
