using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

internal static class RoadhogTestRunner
{
    private const string WorkerArgument = "--test-worker";
    private const string WorkersOption = "--test-workers=";
    private const string FilterOption = "--test-filter=";
    private const string ResultPrefix = "ROADHOG_TEST_RESULT ";

    // Keep tests that use the desktop or launch real subprocesses on one worker.
    public static Func<Task> RunSerially(Func<Task> run) => new ScheduledTest(run, Schedule.Serial).RunAsync;

    // Run tests with tight process deadlines before starting the worker pool.
    public static Func<Task> InIsolation(Func<Task> run) => new ScheduledTest(run, Schedule.Isolated).RunAsync;

    // Start known long cases with the first batch to reduce time spent waiting on the last worker.
    public static Func<Task> StartEarly(Func<Task> run) => new ScheduledTest(run, Schedule.Early).RunAsync;

    public static async Task<int> RunAsync((string Name, Func<Task> Run)[] tests, string[] args)
    {
        if (args.Length == 1 && args[0] == WorkerArgument)
            return await RunWorkerAsync(tests);

        var filter = FindOption(args, FilterOption)?.Trim();
        var filtered = !string.IsNullOrWhiteSpace(filter);
        var workerOption = FindOption(args, WorkersOption);
        var workers = filtered ? 1 : Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
        if (args.Contains("--test-workers", StringComparer.OrdinalIgnoreCase) ||
            (workerOption is not null && (!int.TryParse(workerOption, out workers) || workers < 1)))
        {
            Console.Error.WriteLine("--test-workers requires a positive integer.");
            return 2;
        }

        var selected = Enumerable.Range(0, tests.Length)
            .Where(index => !filtered || tests[index].Name.Contains(filter!, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (selected.Length == 0)
        {
            Console.WriteLine("FAILED no tests matched the requested filter.");
            return 2;
        }

        var timer = Stopwatch.StartNew();
        workers = Math.Min(workers, Math.Min(Environment.ProcessorCount, selected.Length));
        var (passed, failed, runnerFailed) = workers == 1
            ? await RunSerialAsync(tests, selected)
            : await RunParallelAsync(tests, selected, workers);
        var incomplete = selected.Length - passed - failed;
        Console.WriteLine($"Roadhog tests: {passed} passed, {failed} failed, {incomplete} incomplete in {timer.Elapsed.TotalSeconds:F1}s.");
        if (failed > 0)
            Console.WriteLine($"FAILED {failed} test(s).");
        if (runnerFailed || incomplete > 0 || failed > 0)
            return 1;

        Console.WriteLine("All Roadhog tests passed.");
        return 0;
    }

    private static string? FindOption(string[] args, string prefix) => args
        .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static async Task<(int Passed, int Failed, bool RunnerFailed)> RunSerialAsync(
        (string Name, Func<Task> Run)[] tests, int[] selected)
    {
        var passed = 0;
        foreach (var index in selected)
        {
            if (await RunCaseAsync(tests[index]))
                passed++;
        }
        return (passed, selected.Length - passed, false);
    }

    private static async Task<(int Passed, int Failed, bool RunnerFailed)> RunParallelAsync(
        (string Name, Func<Task> Run)[] tests, int[] selected, int workers)
    {
        var scheduled = selected.Select(index => (Index: index, Schedule: (tests[index].Run.Target as ScheduledTest)?.Schedule)).ToArray();
        var isolated = scheduled.Where(test => test.Schedule == Schedule.Isolated).Select(test => test.Index).ToArray();
        var serial = new Queue<int>(scheduled.Where(test => test.Schedule == Schedule.Serial).Select(test => test.Index));
        var pending = new ConcurrentQueue<int>(scheduled
            .Where(test => test.Schedule is null or Schedule.Early)
            .OrderByDescending(test => test.Schedule == Schedule.Early)
            .Select(test => test.Index));
        workers = Math.Min(workers, pending.Count + (serial.Count == 0 ? 0 : 1));
        if (workers < 2)
            return await RunSerialAsync(tests, selected);

        Console.WriteLine($"Running {selected.Length} Roadhog tests with {workers} worker processes " +
            $"({serial.Count} tests share one worker, {isolated.Length} run before the pool).");
        var isolatedResult = await RunSerialAsync(tests, isolated);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            var results = await Task.WhenAll(Enumerable.Range(0, workers).Select(worker =>
                DispatchAsync(worker + 1, worker == 0 ? serial : new Queue<int>(), pending, tests, cancellation.Token)));
            return (isolatedResult.Passed + results.Sum(result => result.Passed),
                isolatedResult.Failed + results.Sum(result => result.Failed),
                cancellation.IsCancellationRequested || results.Any(result => result.RunnerFailed));
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    private static async Task<(int Passed, int Failed, bool RunnerFailed)> DispatchAsync(
        int worker, Queue<int> serial, ConcurrentQueue<int> pending, (string Name, Func<Task> Run)[] tests,
        CancellationToken cancellationToken)
    {
        var passed = 0;
        var failed = 0;
        int? active = null;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Roadhog.Tests.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            start.ArgumentList.Add(WorkerArgument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the test worker.");
            try
            {
                var exited = process.WaitForExitAsync(cancellationToken);
                process.StandardInput.AutoFlush = true;
                while (serial.TryDequeue(out var index) || pending.TryDequeue(out index))
                {
                    active = index;
                    cancellationToken.ThrowIfCancellationRequested();
                    await process.StandardInput.WriteLineAsync(index.ToString().AsMemory(), cancellationToken);
                    while (true)
                    {
                        var read = process.StandardOutput.ReadLineAsync(cancellationToken).AsTask();
                        if (await Task.WhenAny(read, exited) == exited || await read is not { } line)
                            throw new InvalidOperationException("Worker exited before reporting its test result.");
                        if (line == ResultPrefix + index + " PASS")
                        {
                            passed++;
                            break;
                        }
                        if (line == ResultPrefix + index + " FAIL")
                        {
                            failed++;
                            break;
                        }
                        Console.WriteLine(line);
                    }
                    active = null;
                }
                process.StandardInput.Close();
                await exited;
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Worker exited with code {process.ExitCode}.");
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
            return (passed, failed, false);
        }
        catch (Exception exception)
        {
            var testName = active is int index ? $" while running {tests[index].Name}" : string.Empty;
            Console.Error.WriteLine($"Test worker {worker} stopped{testName}. {exception.Message}");
            return (passed, failed, true);
        }
    }

    private static async Task<int> RunWorkerAsync((string Name, Func<Task> Run)[] tests)
    {
        if (!Console.IsInputRedirected || !Console.IsOutputRedirected)
            return 2;

        Console.OutputEncoding = Encoding.UTF8;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            if (!int.TryParse(line, out var index) || index < 0 || index >= tests.Length)
                return 2;
            var passed = await RunCaseAsync(tests[index]);
            Console.WriteLine(ResultPrefix + index + (passed ? " PASS" : " FAIL"));
        }
        return 0;
    }

    private static async Task<bool> RunCaseAsync((string Name, Func<Task> Run) test)
    {
        try
        {
            await test.Run().ConfigureAwait(false);
            Console.WriteLine("PASS " + test.Name);
            return true;
        }
        catch (Exception exception)
        {
            Console.WriteLine("FAIL " + test.Name + Environment.NewLine + "     " + exception.Message);
            return false;
        }
    }

    private enum Schedule { Serial, Isolated, Early }

    private sealed class ScheduledTest(Func<Task> run, Schedule schedule)
    {
        public Schedule Schedule { get; } = schedule;
        public Task RunAsync() => run();
    }
}
