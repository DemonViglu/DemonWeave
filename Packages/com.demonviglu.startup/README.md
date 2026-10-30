# Startup 0.2：项目装配与统一驱动

场景只放一个项目自己的应用根组件。它继承AppHost，直接写服务注册、模块初始化和退出顺序；不再挂另一个AppHost或绑定Composition资产。

```csharp
[UnityEngine.DefaultExecutionOrder(-10000)]
public sealed class MyApplication : DemonViglu.Startup.Unity.AppHost
{
    protected override void Configure(DemonViglu.Startup.ServiceRegistry services)
    {
        services.Add(new MyService(), takeOwnership: true); // IDisposable服务，退出时释放
    }
    protected override void StopServices()
    {
        // 如需先结束玩法，在这里处理；此时服务和事件仍然可用。
    }
}
```

AppHost提供Services、Events、Loop、Scheduler、IsRunning，拒绝重复应用根，启动失败清理已注册资源。Configure结束后禁止继续注册；takeOwnership服务按注册逆序释放，最后关闭事件总线。应用根必须在Hierarchy根层级。普通玩法使用Unity Update/FixedUpdate；事件队列由应用根LateUpdate自动Flush一批（每帧最多128条），项目无需额外接线。

## 工具按需使用

- EventBus：Publish立即同步派发，Post排队，两者使用同一套Subscribe监听。Subscribe与Post均返回可释放令牌，分别用于退订和取消待派发消息。不需要事件基类或额外Messaging包。
- ServiceRegistry：显式注册/查询、重复检查及资源释放，没有反射创建或依赖注入。
- ServiceBinding：只用于会切换的场景/玩家提供者；Bind返回释放令牌，不能同时绑两个。普通固定服务直接注册。
- Feature：可选Module合同，只有Initialize(ServiceRegistry)与Dispose。项目按明确顺序调用，不自动发现模块或按字符串排序。
- GameLoop：按phase/order注册回调，由AppHost自动驱动。Fixed/Update/Late分别在对应Unity回调中执行，PostPhysics由WaitForFixedUpdate驱动，仅支持默认自动固定步长物理。它只保证已注册回调之间的顺序，不排序项目所有MonoBehaviour。
- Scheduler：由AppHost.Update自动推进，先于Loop.Update执行；支持Delay/Repeat、取消和降频预算。缩放计时随游戏暂停，非缩放计时继续。

```csharp
// 在项目Configure或StartServices中注册即可，无需写Tick。
Loop.Register(LoopPhase.Update, time => UpdateGameplay(time.Delta));
Scheduler.Repeat(0.2, elapsed => Think(elapsed));
Scheduler.Delay(2, () => Events.Post(new Ready()));
```

## 示例与迁移

Tools → Startup → Open Basic Example：空项目应用根和原生Update。
Tools → Startup → Open Loop and Scheduler Example：演示注册回调与降频任务，驱动和释放统一由AppHost完成。示例场景在包内Samples/Scenes。

从0.1升级：删除旧AppHost组件，让项目Composition继承AppHost；Configure改为protected override；原Feature启动/退出改为项目显式Initialize/Dispose；Kernel.App.Services/Events改为应用根Services/Events。删除旧driver/overlay挂件。Kernel、AppContext、AppComposition和自动依赖排序已移除，没有兼容壳。

DemonWeave应使用自己项目的应用根完成上述接入；本次只更新Firer，不改另一个项目。

## 立即与排队事件

```csharp
Events.Subscribe<MyMessage>(OnMessage);
Events.Publish(message); // 当前调用内执行监听者
var cancellation = Events.Post(message); // 应用根下一次LateUpdate批次派发
// 不再需要该消息时cancellation.Dispose()。
```

单独使用纯C# EventBus时，由宿主调用Flush(maxMessages)，Unity应用根已自动处理。默认等待容量1024条，可通过EventBus构造参数调整；满时TryPost返回false，Post抛错。Flush只消费开始时的批次，监听回调中新增消息不会加入当前批次，禁止回调中递归Flush。Post不保证下一渲染帧，更不是定时器；定时需求仍用Scheduler。

全部操作限所属线程。派发异常会继续处理其余监听者/批次消息，最后聚合抛出；应用根记录异常，不自动重试已派发消息。退出丢弃尚未消费的消息；必须完成的退出通知使用Publish或项目明确处理，不能依赖下一帧。消息不深复制，建议使用不可变数据。

AppHost的Awake、Update、FixedUpdate、LateUpdate、OnDisable和OnDestroy是基础接线；派生项目不要另写同名Unity消息，初始化/清理使用Configure、StartServices、StopServices，逐帧工作注册到Loop。Loop与Scheduler同时注册进Services，Feature可直接获取同一实例。退出先StopServices，再释放调度/循环及服务，最后关闭事件。
