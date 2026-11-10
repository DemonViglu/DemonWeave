# Dialogue：对话数据与会话

纯 C# DialogueGraph/DialogueSession 管理台词跳转、选择、条件/命令端口及取消；不依赖 Quest、Startup 或 UI 栈。过期 revision 的点击不能推进新会话。

Unity 适配提供 DialogueCharacter、DialogueAsset、DialoguePlayer 和基础 uGUI DialogueView。Create → Dialogue 创建资产；对话 Inspector 配置台词、角色和分支，稳定 ID 保持重排后的跳转。

基础播放器需要项目搭建 View，绑定姓名、正文、选项容器/模板及继续/取消按钮，并绑定 Conversation。PlayOnStart 可关闭，由项目调用 Play/Cancel。基础 View 是可选表现；自定义界面可直接使用 DialogueSession。

项目负责 EventSystem、输入、光标、相机与角色控制。使用 OnStarted/OnCompleted/OnCancelled 接入项目策略，正常结束与取消都应释放输入占用。包不再自行修改全局光标，也不包含默认 Prefab、示例场景或创建窗口菜单。

CourtyardRescue 使用 Assets/AreaRescue/Support/Presentation 的项目对话表现与 UI 栈；这些不属于 Dialogue 的安装依赖。没有存档、本地化或世界恢复策略。
