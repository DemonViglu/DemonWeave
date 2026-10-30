# Dialogue：配置台词，绑定场景即可播放

## 直接看示例

菜单 **Tools → Dialogue → 打开配置式示例**，然后点击Unity的Play。
示例位于包内 `Samples/Minimal/Dialogue.unity`，展示角色、连续台词、两个选项和分支结尾。最后一句会等待点击“结束对话”，不会一出现就关闭。

## 创建自己的对话

1. Project窗口右键 → **Create → Dialogue → 角色**，填写显示名、头像和姓名颜色；同一角色资产可供多个对话使用。
2. 右键 → **Create → Dialogue → 对话**。在Inspector添加台词、拖入角色（留空为旁白），填写文字。
3. 普通台词通过“继续后前往”选择下一句；需要分支则“添加选项”，填写选项文字和“选择后前往”。目标用下拉选择，不用填写ID。第一句是开场；“结束对话”表示读完或选择后结束。
4. 菜单 **GameObject → Dialogue → 添加对话播放器和窗口**，在播放器的Conversation字段拖入对话资产。默认Play On Start开启，进入场景即可播放。选中对话资产再执行菜单，会自动绑定该资产。

台词、角色、选项以及顺序修改都不需要写代码。移动台词不会改变已有跳转；删除仍被引用的台词会在Inspector报错，需重新选择目标。

## 放进已有玩法场景

- 关闭Play On Start；将现有按钮/交互组件的UnityEvent连接到`DialoguePlayer.Play()`，即可由交互开始。`Cancel()`用于外部中断。
- `OnStarted / OnCompleted / OnCancelled`可在Inspector绑定移动控制、相机或其他项目行为。包不认识Firer，也不会擅自锁定项目角色；需要锁移动时同时配置正常结束和取消后的恢复。
- 窗口为普通uGUI Prefab，所有引用在Inspector可见。可复制到自己的内容目录修改布局、字体和颜色，然后绑定View。长台词和选项列表支持滚动；头像可选。
- 一个View同时只接受一个播放器。再次Play不会重入当前会话；播放器或窗口禁用会取消会话。窗口显示时释放光标，结束后恢复原光标状态。
- 场景需要EventSystem。添加菜单只在场景中不存在EventSystem时创建Input System UI模块；已有EventSystem沿用原配置。

## 包边界

Runtime纯C#会话负责节点、选择、条件/命令端口与继续/取消；Runtime/Unity负责角色/对话资产、播放器和窗口。仅Unity适配依赖uGUI；编辑器添加菜单使用Input System。无需Startup、Flow或Firer。

条件/命令端口属于程序扩展；本次Inspector工作流覆盖台词、角色、选项和分支，不包含可视化条件表达式、存档恢复、本地化或网络对话。旧IMGUI演示和旧验证脚本已移除，不生成外部验证工程或Assets根目录示例。
