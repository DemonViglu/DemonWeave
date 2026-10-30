using System;
using System.Collections.Generic;

namespace DemonViglu.Startup.Editor
{
    public static class PeriodicAcceptance
    {
        private static void Assert(bool value) { if (!value) throw new Exception("Periodic assertion failed."); }
        private static void Near(double a, double b) => Assert(Math.Abs(a - b) < 0.000001);
        private static void Throws<T>(Action action) where T : Exception
        {
            try { action(); } catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
        public static int Run()
        {
            var tests = new Action[] { IntervalsAndElapsed, StaggerAndHitch, PauseClocks, FairBudget,
                CancellationAndNewTasks, ChangeFrequency, FaultAndCleanup, NoPerTickAllocation, Arguments };
            foreach (var test in tests)
                try { test(); } catch (Exception e) { throw new Exception(test.Method.Name, e); }
            return tests.Length;
        }
        private static void IntervalsAndElapsed()
        {
            using (var scheduler = new Scheduler())
            {
                var values = new List<double>();
                scheduler.Repeat(1, values.Add);
                scheduler.Tick(new FrameTime(0.5, 0.5)); Assert(values.Count == 0);
                scheduler.Tick(new FrameTime(0.75, 0.75)); Near(values[0], 1.25);
                scheduler.Tick(new FrameTime(0.75, 0.75)); Near(values[1], 0.75);
            }
        }
        private static void StaggerAndHitch()
        {
            using (var scheduler = new Scheduler())
            {
                int a = 0, b = 0;
                double elapsed = 0;
                scheduler.Repeat(1, dt => { a++; elapsed = dt; }, initialDelay: 0.25);
                scheduler.Repeat(1, dt => b++, initialDelay: 0.75);
                scheduler.Tick(new FrameTime(0.25, 0.25)); Assert(a == 1 && b == 0);
                scheduler.Tick(new FrameTime(9.75, 9.75)); Assert(a == 2 && b == 1); Near(elapsed, 9.75);
                scheduler.Tick(new FrameTime(0.25, 0.25)); Assert(a == 3 && b == 1);
                scheduler.Tick(new FrameTime(0.5, 0.5)); Assert(a == 3 && b == 2);
            }
        }
        private static void PauseClocks()
        {
            using (var scheduler = new Scheduler())
            {
                int scaled = 0, unscaled = 0;
                scheduler.Repeat(1, dt => scaled++, initialDelay: 0);
                scheduler.Repeat(1, dt => unscaled++, useUnscaledTime: true);
                scheduler.Tick(new FrameTime(0, 2)); Assert(scaled == 0 && unscaled == 1);
                scheduler.Tick(new FrameTime(1, 1)); Assert(scaled == 1 && unscaled == 2);
            }
        }
        private static void FairBudget()
        {
            using (var scheduler = new Scheduler(1))
            {
                var trace = new List<int>();
                for (int i = 0; i < 3; i++) { int id = i; scheduler.Repeat(0.1, dt => trace.Add(id)); }
                int delayed = 0;
                scheduler.Delay(0, () => delayed++);
                for (int i = 0; i < 6; i++) scheduler.Tick(new FrameTime(1, 1));
                Assert(string.Join(",", trace) == "0,1,2,0,1,2" && delayed == 1);
            }
        }
        private static void CancellationAndNewTasks()
        {
            using (var scheduler = new Scheduler())
            {
                Scheduler.PeriodicTask self = null;
                int calls = 0, added = 0;
                self = scheduler.Repeat(1, dt =>
                {
                    calls++; self.Dispose();
                    scheduler.Repeat(1, next => added++, initialDelay: 0);
                });
                scheduler.Tick(new FrameTime(1, 1)); Assert(calls == 1 && added == 0);
                scheduler.Tick(new FrameTime(1, 1)); Assert(calls == 1 && added == 1);
                self.Dispose();
                scheduler.Dispose(); Assert(scheduler.Count == 0);
            }
        }
        private static void ChangeFrequency()
        {
            using (var scheduler = new Scheduler())
            {
                var values = new List<double>();
                var task = scheduler.Repeat(1, values.Add);
                scheduler.Tick(new FrameTime(0.5, 0.5));
                task.ChangeInterval(2);
                scheduler.Tick(new FrameTime(1.5, 1.5)); Assert(values.Count == 0);
                scheduler.Tick(new FrameTime(0.5, 0.5)); Near(values[0], 2.5);
                task.Dispose(); Throws<ObjectDisposedException>(() => task.ChangeInterval(1));
            }
        }
        private static void FaultAndCleanup()
        {
            using (var scheduler = new Scheduler(periodicBudget: 2))
            {
                int calls = 0;
                scheduler.Repeat(1, dt => throw new Exception("AI failed"), ownerId: "ai.probe");
                scheduler.Repeat(1, dt => calls++);
                Throws<AggregateException>(() => scheduler.Tick(new FrameTime(1, 1)));
                Assert(calls == 1);
                scheduler.Dispose(); Assert(scheduler.Count == 0);
            }
        }
        private static void NoPerTickAllocation()
        {
            using (var scheduler = new Scheduler())
            {
                scheduler.Repeat(1, dt => { });
                for (int i = 0; i < 20; i++) scheduler.Tick(new FrameTime(1, 1));
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 1000; i++) scheduler.Tick(new FrameTime(1, 1));
                Assert(GC.GetAllocatedBytesForCurrentThread() == before);
            }
        }
        private static void Arguments()
        {
            Throws<ArgumentOutOfRangeException>(() => new Scheduler(0));
            using (var scheduler = new Scheduler())
            {
                Throws<ArgumentOutOfRangeException>(() => scheduler.Repeat(0, dt => { }));
                Throws<ArgumentOutOfRangeException>(() => scheduler.Repeat(double.NaN, dt => { }));
                Throws<ArgumentOutOfRangeException>(() => scheduler.Repeat(1, dt => { }, -1));
            }
        }
    }
}
