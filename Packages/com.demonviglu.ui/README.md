# DemonViglu UI 0.1

从 Firer 已有 UI 契约抽取的可选 UGUI 包，命名空间 `DemonViglu.UI`。运行时代码依赖 UGUI 与 Input System，不依赖 FirePlay、Player、活动、网络或旧全局服务容器。不依赖 Startup。

## 职责与配置

- UiRoot：显式 Canvas、GraphicRaycaster、HUD/Screen/Modal 三层、Modal 输入遮罩、EventSystem 与 InputSystemUIInputModule。
- UiCatalog：稳定 UI ID 对应 Prefab、层级、Modal、输入阻挡与 Normal/Stack/HideOther 策略。
- UiView：显式 CanvasGroup 与 Show/Hide/Focus/Blur/Close 生命周期，项目 View 继承后实现钩子。
- IUiService：Show/Hide/CloseOrReturn/Back/IsShown/TryGetView；不创建全局静态实例。

场景或应用的 Composition 明确持有 Root/Catalog，在正确生命周期调用 Initialize；项目通过 IUiService 使用。长寿命应用不能持有会随场景卸载的 Root。UiRoot 没有运行时 AddComponent 补依赖，也不从对象名称查找输入/相机/玩家。

`UiRoot.BlocksGameplayInput` 只表示当前窗口提出阻挡请求，不直接改变玩家输入或光标。项目适配层读取它，处理当前本地 Player、输入所有权、光标捕获和退出恢复。Root 停用释放请求，重新启用重新计算仍打开窗口的请求。框架不持有 PlayerLook 或 LocalPlayerContext。

Catalog/Prefab 必须显式配置，View 的 CanvasGroup 要绑定；UI ID 属于消费项目，不把 Firer 的 HUD/活动 ID 收进通用包。Cancel 输入由绑定 InputSystemUIInputModule 的 cancel action 请求 Back。

## 项目接入边界

项目自己创建并绑定 Root、Catalog、View Prefab，调用 Initialize；可以从 GameManager 或普通 MonoBehaviour 接入，无需 Startup。业务 UI ID、光标、玩家输入及场景生命周期由项目管理。

CourtyardRescue 的 DialoguePanel、RescueHud、检查点与 UI 资产位于 Assets/AreaRescue，不随本包安装。包内没有独立示例或验收菜单；本工程通过救援样本验证栈和对白使用路径。
