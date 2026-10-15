using System.Diagnostics;
using System.Text.Json;
using System.Windows.Automation;
using TaskTodo.App.Tests.Support;
using TaskTodo.Infrastructure.Tests.Support;

namespace TaskTodo.App.Tests;

[Collection("Desktop")]
public sealed class PerformanceObservationTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    [Trait("Category", "Performance")]
    public async Task Thousand_tasks_and_repeated_capsule_switches_record_cost_without_window_growth()
    {
        using var storage = new TemporaryTaskTodoStorage();
        var elapsed = Stopwatch.StartNew();
        var artifactDirectory = Environment.GetEnvironmentVariable("TASKTODO_TEST_ARTIFACTS") ?? AppContext.BaseDirectory;
        Directory.CreateDirectory(artifactDirectory);
        var progressPath = Path.Combine(artifactDirectory, "performance-progress.log");
        File.WriteAllText(progressPath, "");
        void Progress(string stage)
        {
            var message = $"{elapsed.Elapsed.TotalMilliseconds:F0} ms: {stage}";
            output.WriteLine(message);
            File.AppendAllText(progressPath, message + Environment.NewLine);
        }

        Progress("Preparing isolated SQLite data.");
        var preparation = Stopwatch.StartNew();
        Guid[] listIds;
        await using (var service = await storage.OpenServiceAsync())
        {
            var snapshot = await service.GetSnapshotAsync();
            listIds = [snapshot.Lists[0].Id,
                (await service.CreateListAsync(Guid.NewGuid(), "性能清单 B")).Id,
                (await service.CreateListAsync(Guid.NewGuid(), "性能清单 C")).Id];
            for (var index = 0; index < 1000; index++)
                await service.CreateTaskAsync(Guid.NewGuid(), listIds[0], $"性能观测任务 {index + 1:D4} — 中文 English");
            await service.SaveSettingsAsync(snapshot.Settings with { IsAlwaysOnTop = false });
        }
        preparation.Stop();
        Progress("Prepared 1000 tasks and 3 lists; starting app.");

        var startup = Stopwatch.StartNew();
        using var process = DesktopProcessProbe.Start(storage.RootPath);
        Progress($"App PID {process.Id}; waiting for main window.");
        try
        {
            var main = await DesktopProcessProbe.WaitForWindow(process, "TaskTodo");
            startup.Stop();
            Progress("Main window ready; obtaining task scroll pattern.");
            // The task list exposes its own ScrollPattern; avoid searching through task rows.
            var scrollElement = main.FindFirst(TreeScope.Children,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "TaskScroll"));
            if (scrollElement is null)
            {
                var child = TreeWalker.RawViewWalker.GetFirstChild(main);
                for (var index = 0; child is not null && index < 15; index++)
                {
                    Progress($"Direct UIA child: {child.Current.ClassName}, id={child.Current.AutomationId}, name={child.Current.Name}");
                    child = TreeWalker.RawViewWalker.GetNextSibling(child);
                }
            }
            Assert.NotNull(scrollElement);
            var scroll = (ScrollPattern)scrollElement.GetCurrentPattern(ScrollPattern.Pattern);
            var scrolling = Stopwatch.StartNew();
            for (var index = 0; index < 10; index++)
            {
                Progress($"Scroll {index + 1}/10: requesting bottom.");
                scroll.SetScrollPercent(ScrollPattern.NoScroll, 100);
                await DesktopProcessProbe.WaitFor(() => scroll.Current.VerticalScrollPercent >= 99);
                Progress($"Scroll {index + 1}/10: requesting top.");
                scroll.SetScrollPercent(ScrollPattern.NoScroll, 0);
                await DesktopProcessProbe.WaitFor(() => scroll.Current.VerticalScrollPercent <= 1);
            }
            scrolling.Stop();
            Progress("Scrolling complete; docking.");
            DesktopProcessProbe.Invoke(DesktopProcessProbe.ById(main, "DockToEdge"));
            var rail = await DesktopProcessProbe.WaitForWindow(process, "TaskTodo 胶囊");
            var initialWindowCount = DesktopProcessProbe.CountWindows(process.Id);
            var cycles = Stopwatch.StartNew();
            for (var index = 0; index < 30; index++)
            {
                Progress($"Capsule cycle {index + 1}/30: opening list {index % listIds.Length + 1}.");
                var capsule = DesktopProcessProbe.ById(rail, $"OpenList_{listIds[index % listIds.Length]:D}");
                Assert.NotNull(capsule);
                DesktopProcessProbe.Invoke(capsule);
                main = await DesktopProcessProbe.WaitForWindow(process, "TaskTodo");
                Progress($"Capsule cycle {index + 1}/30: main visible, docking.");
                var dock = DesktopProcessProbe.ById(main, "DockToEdge");
                await DesktopProcessProbe.WaitFor(() => dock.Current.IsEnabled);
                DesktopProcessProbe.Invoke(dock);
                rail = await DesktopProcessProbe.WaitForWindow(process, "TaskTodo 胶囊");
                Progress($"Capsule cycle {index + 1}/30: dock visible.");
            }
            cycles.Stop();
            Progress("Capsule cycles complete; sampling idle resources.");
            await Task.Delay(1000); // Finish debounced settings and animation before observing idle.
            var idleSamples = new List<IdleSample>();
            for (var index = 0; index < 3; index++)
            {
                process.Refresh();
                var cpuBefore = process.TotalProcessorTime;
                var idle = Stopwatch.StartNew();
                await Task.Delay(2000);
                process.Refresh();
                idle.Stop();
                var cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
                var sample = new IdleSample(index + 1, idle.Elapsed.TotalMilliseconds,
                    cpuMs, cpuMs / idle.Elapsed.TotalMilliseconds * 100);
                idleSamples.Add(sample);
                Progress($"Idle sample {sample.Index}/3: CPU {sample.CpuMs:F1} ms over {sample.SampleMs:F1} ms ({sample.OneCorePercent:F2}% of one core).");
            }
            var finalWindowCount = DesktopProcessProbe.CountWindows(process.Id);
            var report = new
            {
                taskCount = 1000,
                listCount = listIds.Length,
                capsuleCycles = 30,
                preparationMs = preparation.Elapsed.TotalMilliseconds,
                startupToWindowMs = startup.Elapsed.TotalMilliseconds,
                scrollRoundTrips = 10,
                scrollRoundTripsMs = scrolling.Elapsed.TotalMilliseconds,
                capsuleCyclesMs = cycles.Elapsed.TotalMilliseconds,
                idleSampleMs = idleSamples.Sum(sample => sample.SampleMs),
                idleCpuMs = idleSamples.Sum(sample => sample.CpuMs),
                idleCpuOneCorePercent = idleSamples.Sum(sample => sample.CpuMs) / idleSamples.Sum(sample => sample.SampleMs) * 100,
                idleSamples,
                workingSetBytes = process.WorkingSet64,
                privateBytes = process.PrivateMemorySize64,
                handleCount = process.HandleCount,
                initialWindowCount,
                finalWindowCount,
            };
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            output.WriteLine(json);
            if (Environment.GetEnvironmentVariable("TASKTODO_TEST_ARTIFACTS") is { } artifacts)
            {
                Directory.CreateDirectory(artifacts);
                await File.WriteAllTextAsync(Path.Combine(artifacts, "performance-observation.json"), json);
            }
            Assert.Equal(initialWindowCount, finalWindowCount);
            Progress("Observation complete; window count unchanged.");
        }
        finally { await DesktopProcessProbe.Stop(process); }
    }

    private sealed record IdleSample(int Index, double SampleMs, double CpuMs, double OneCorePercent);
}
