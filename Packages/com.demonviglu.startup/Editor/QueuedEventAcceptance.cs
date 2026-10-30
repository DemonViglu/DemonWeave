using System;
using System.Collections.Generic;
using System.Threading;
using DemonViglu.Startup;

namespace DemonViglu.Startup.Editor
{
    public static class QueuedEventAcceptance
    {
        public static void RunBatch()
        {
            try { UnityEngine.Debug.Log("QUEUED_EVENTS_PASS cases=" + Run()); UnityEditor.EditorApplication.Exit(0); }
            catch (Exception error) { UnityEngine.Debug.LogException(error); UnityEditor.EditorApplication.Exit(1); }
        }
        private static void Assert(bool condition) { if (!condition) throw new Exception("Queue assertion failed."); }
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        public static int Run()
        {
            var tests = new Action[] { DeferredAndBudgeted, ReentrantPostWaits, OverflowAndCancellation,
                CancelDuringDrain, ExceptionsAndRecursion, DisposeDuringDrain, OwningThreadOnly,
                 InvalidArguments };
            foreach (var test in tests)
                try { test(); } catch (Exception e) { throw new Exception(test.Method.Name, e); }
            return tests.Length;
        }
        private static void DeferredAndBudgeted()
        {
            using (var bus = new EventBus())
            {
                var values = new List<int>();
                bus.Subscribe<int>(values.Add);
                bus.Post(1); bus.Post(2); bus.Post(3);
                Assert(values.Count == 0);
                Assert(bus.Flush(2) == 2 && bus.PendingCount == 1);
                Assert(bus.Flush(2) == 1 && string.Join(",", values) == "1,2,3");
            }
        }
        private static void ReentrantPostWaits()
        {
            using (var bus = new EventBus())
            {
                int calls = 0;
                bus.Subscribe<int>(value => { calls++; if (value == 1) bus.Post(2); });
                bus.Post(1);
                bus.Flush(128);
                Assert(calls == 1 && bus.PendingCount == 1);
                bus.Flush(128);
                Assert(calls == 2 && bus.PendingCount == 0);
            }
        }
        private static void OverflowAndCancellation()
        {
            using (var bus = new EventBus(1))
            {
                var token = bus.Post(1);
                Assert(!bus.TryPost(2, out var refused) && refused == null);
                Throws<InvalidOperationException>(() => bus.Post(2));
                token.Dispose(); token.Dispose();
                Assert(bus.PendingCount == 0 && bus.TryPost(3, out var accepted));
                Assert(bus.Flush(1) == 1);
            }
        }
        private static void CancelDuringDrain()
        {
            using (var bus = new EventBus())
            {
                IDisposable second = null;
                int calls = 0;
                bus.Subscribe<int>(value => { calls++; second.Dispose(); });
                bus.Post(1); second = bus.Post(2);
                Assert(bus.Flush(2) == 1 && calls == 1 && bus.PendingCount == 0);
            }
        }
        private static void ExceptionsAndRecursion()
        {
            using (var bus = new EventBus())
            {
                int calls = 0;
                bus.Subscribe<int>(value => { calls++; if (value == 1) bus.Flush(1); });
                bus.Post(1); bus.Post(2);
                Throws<AggregateException>(() => bus.Flush(2));
                Assert(calls == 2 && bus.PendingCount == 0 && bus.Flush(2) == 0);
            }
        }
        private static void DisposeDuringDrain()
        {
            using (var bus = new EventBus())
            {
                int calls = 0;
                bus.Subscribe<int>(value => { calls++; bus.Dispose(); });
                bus.Post(1); var second = bus.Post(2); bus.Post(3);
                Assert(bus.Flush(2) == 1 && calls == 1 && bus.PendingCount == 0);
                second.Dispose();
                Throws<ObjectDisposedException>(() => bus.Post(4));
            }
        }
        private static void OwningThreadOnly()
        {
            using (var bus = new EventBus())
            {
                var token = bus.Post(1);
                Exception result = null;
                var thread = new Thread(() =>
                {
                    try { token.Dispose(); } catch (Exception e) { result = e; }
                });
                thread.Start(); thread.Join();
                Assert(result is InvalidOperationException && bus.PendingCount == 1);
                token.Dispose();
                Assert(bus.PendingCount == 0);
            }
        }
        private static void InvalidArguments()
        {
            Throws<ArgumentOutOfRangeException>(() => new EventBus(0));
            using (var bus = new EventBus())
            {
                Throws<ArgumentNullException>(() => bus.Post<string>(null));
                Throws<ArgumentOutOfRangeException>(() => bus.Flush(0));
            }
        }
    }
}
