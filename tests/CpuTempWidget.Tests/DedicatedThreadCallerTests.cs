using CpuTempWidget.Services;
using Xunit;

namespace CpuTempWidget.Tests;

public class DedicatedThreadCallerTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(5);

    [Fact]
    public void Call_RunsOnTheNamedDedicatedThread()
    {
        using var caller = new DedicatedThreadCaller<string>("test SDK thread", () => Thread.CurrentThread.Name!);

        var r = caller.Call(Long);

        Assert.Equal(CallStatus.Ok, r.Status);
        Assert.Equal("test SDK thread", r.Value);
    }

    [Fact]
    public void AllCalls_ShareOneThread()
    {
        using var caller = new DedicatedThreadCaller<int>("t", () => Environment.CurrentManagedThreadId);

        int first = caller.Call(Long).Value;
        int second = caller.Call(Long).Value;

        Assert.Equal(first, second);
        Assert.NotEqual(Environment.CurrentManagedThreadId, first);
    }

    [Fact]
    public void HungCall_TimesOut_ThenFailsFastAsBusy_AndRecoversWhenItReturns()
    {
        using var release = new ManualResetEventSlim(false);
        int calls = 0;
        using var caller = new DedicatedThreadCaller<int>("t", () =>
        {
            int n = Interlocked.Increment(ref calls);
            if (n == 1) release.Wait();      // first call hangs until released
            return n;
        });

        Assert.Equal(CallStatus.TimedOut, caller.Call(Short).Status);

        // While the first call is still stuck, new calls must not start another one or block.
        var started = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(CallStatus.Busy, caller.Call(Long).Status);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(1), "Busy must return immediately");
        Assert.Equal(1, Volatile.Read(ref calls));

        release.Set();                       // the native call finally returns

        // Eventually calls work again (give the worker a moment to publish completion).
        CallResult<int> recovered = default;
        SpinWait.SpinUntil(() => (recovered = caller.Call(Long)).Status == CallStatus.Ok, Long);
        Assert.Equal(CallStatus.Ok, recovered.Status);
        Assert.Equal(2, recovered.Value);
    }

    [Fact]
    public void ExceptionInWork_IsReportedAsFaulted_AndTheThreadSurvives()
    {
        int n = 0;
        using var caller = new DedicatedThreadCaller<int>("t", () =>
        {
            if (Interlocked.Increment(ref n) == 1) throw new InvalidOperationException("boom");
            return 7;
        });

        var first = caller.Call(Long);
        Assert.Equal(CallStatus.Faulted, first.Status);
        Assert.Equal("boom", first.Error!.Message);

        var second = caller.Call(Long);
        Assert.Equal(CallStatus.Ok, second.Status);
        Assert.Equal(7, second.Value);
    }

    [Fact]
    public void Dispose_RunsOnExitOnTheWorkerThread_AndLaterCallsAreRejected()
    {
        int workerId = 0, exitId = -1;
        var caller = new DedicatedThreadCaller<int>("t",
            () => workerId = Environment.CurrentManagedThreadId,
            onExit: () => exitId = Environment.CurrentManagedThreadId);
        caller.Call(Long);

        caller.Dispose();

        Assert.Equal(workerId, exitId);      // e.g. rm_uninit must run on the thread that did rm_init
        Assert.Equal(CallStatus.Disposed, caller.Call(Short).Status);
    }

    [Fact]
    public void Dispose_WithAHungCall_ReturnsInsteadOfBlockingForever()
    {
        using var release = new ManualResetEventSlim(false);
        var caller = new DedicatedThreadCaller<int>("t", () => { release.Wait(); return 1; });
        Assert.Equal(CallStatus.TimedOut, caller.Call(Short).Status);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        caller.Dispose();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4), "Dispose must give up on a hung native call");
        release.Set();
    }
}
