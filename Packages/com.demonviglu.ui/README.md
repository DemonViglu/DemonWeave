# DemonViglu UI 0.1

从 Firer 已有 UI 契约抽取的可选 UGUI 包，命名空间 `DemonViglu.UI`。运行时代码依赖 UGUI 与 Input System，不依赖 FirePlay、Player、活动、网络或旧全局服务容器。附带 Editor 验收入口依赖 Startup 的测试工具。

## 职责与配置

- UiRoot：显式 Canvas、GraphicRaycaster、HUD/Screen/Modal 三层、Modal 输入遮罩、EventSystem 与 InputSystemUIInputModule。
- UiCatalog：稳定 UI ID 对应 Prefab、层级、Modal、输入阻挡与 Normal/Stack/HideOther 策略。
- UiView：显式 CanvasGroup 与 Show/Hide/Focus/Blur/Close 生命周期，项目 View 继承后实现钩子。
- IUiService：Show/Hide/CloseOrReturn/Back/IsShown/TryGetView；不创建全局静态实例。

场景或应用的 Composition 明确持有 Root/Catalog，在正确生命周期调用 Initialize；项目通过 IUiService 使用。长寿命应用不能持有会随场景卸载的 Root。UiRoot 没有运行时 AddComponent 补依赖，也不从对象名称查找输入/相机/玩家。

`UiRoot.BlocksGameplayInput` 只表示当前窗口提出阻挡请求，不直接改变玩家输入或光标。项目适配层读取它，处理当前本地 Player、输入所有权、光标捕获和退出恢复。Root 停用释放请求，重新启用重新计算仍打开窗口的请求。框架不持有 PlayerLook 或 LocalPlayerContext。

Catalog/Prefab 必须显式配置，View 的 CanvasGroup 要绑定；UI ID 属于消费项目，不把 Firer 的 HUD/活动 ID 收进通用包。Cancel 输入由绑定 InputSystemUIInputModule 的 cancel action 请求 Back。

## 验收与迁移状态

菜单 Tools → Startup → Run UI Contract Acceptance。批处理入口 `DemonViglu.UI.Editor.UiContractVerifier.RunBatch`，不要传 -quit；验证独立 UI 合同后接续 Startup 核心和两次 Play 样例。

验证覆盖生命周期钩子、栈顶/非栈顶关闭、Back、HideOther 恢复、Modal 遮罩、HUD 焦点、输入阻挡请求、Root 停用/重启与 Hide。UI 合同测试是编辑器中直接调用，不代表真实鼠标/触屏或 Firer 活动/网络验收；后续 Play 是 Startup 时序样例。

正式 Firer 已切换到本包：七个业务 View 继承 UiView，调用者使用 IUiService，Catalog/Root Prefab/SnowValley 已更新脚本引用且保留资产 GUID。旧 Root/View/Catalog/接口与旧合同测试已在绑定和正式场景 UI 验证后删除；FirePlayUiIds 单独保留在项目层。

FirePlayUiBootstrap 承担项目光标适配，读取 BlocksGameplayInput 并跟随本地 Player 释放/恢复光标。服务使用正式 AppHost 的 ServiceBinding<IUiService>，消费者读取同一提供者。窗口与光标验证不等于触屏、活动结算或多人 UI 全部通过。
