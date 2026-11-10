# Startup：一个明确的游戏入口

在场景根物体上挂项目自己的 GameManager，继承 AppHost。名称由项目决定，不需要额外装配资产。

```csharp
using DemonViglu.Startup;
using DemonViglu.Startup.Unity;

public sealed class GameManager : AppHost
{
    protected override void Configure(ServiceRegistry services)
    {
        // services.Add(new MyService(), takeOwnership: true);
    }
    protected override void StartServices() { /* 开始项目业务 */ }
    protected override void StopServices() { /* 先停止项目业务 */ }
}
```

执行顺序：Awake → Configure → 冻结注册 → StartServices；停用/销毁 → StopServices → 逆序释放拥有的服务 → 关闭事件。启动失败也清理已注册资源。AppHost 当前选择单实例、根物体、跨场景保留；如果项目不接受这些选择，可直接使用纯 C# ServiceRegistry/EventBus，由自己的宿主管理，不必继承 AppHost。

- ServiceRegistry：显式注册/查询，takeOwnership 控制是否释放 IDisposable；没有自动扫描、反射创建或依赖排序。
- EventBus：Subscribe 返回退订令牌，Publish 同步派发，Post 排队并返回取消令牌。AppHost.LateUpdate 自动 Flush 一批；独立使用时由项目调用 Flush。
- ServiceBinding：仅供需要切换提供者的服务使用，普通固定服务不需要。

全部事件操作限所属线程。默认队列容量 1024，一批最多 128 条；回调产生的新消息留到下一批。异常不会跳过其余监听者，最终聚合报告；退出丢弃未派发消息。

玩法使用普通 MonoBehaviour.Update/FixedUpdate 或项目自己的驱动。没有 Loop、Scheduler、Feature、存档或场景流程管理。派生 AppHost 不要覆盖 Awake、LateUpdate、OnDisable、OnDestroy；使用上述三个钩子。

Package Manager 的 Samples 可导入 Minimal Startup，打开导入后的 Scenes/Startup_Basic.unity。样本默认不参与包编译。Tools → Startup → Check Core Utilities 检查服务、事件与绑定合同。

收尾变更：移除未被当前玩法使用的 GameLoop/Scheduler/Feature 及其演示。使用旧 API 的其他项目需自行保留对应实现或改用项目驱动；本仓库未修改 Firer。
