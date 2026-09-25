using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using StardewModdingAPI;

namespace StardewAgentBridge;

internal sealed class PublicServer : IDisposable
{
    private sealed class MainThreadCall
    {
        public Func<object> Work { get; init; } = null!;
        public TaskCompletionSource<object> Completion { get; init; } = null!;
    }

    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentQueue<MainThreadCall> mainThreadCalls = new();
    private readonly IMonitor monitor;
    private readonly string token;
    private readonly string actorId;
    private readonly ObservationProjector projector;
    private readonly OperationHost operations;
    private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private Task? loopTask;
    private ulong currentTick;

    public PublicServer(int port, string token, string actorId, ObservationProjector projector, OperationHost operations, IMonitor monitor)
    {
        this.token = token;
        this.actorId = actorId;
        this.projector = projector;
        this.operations = operations;
        this.monitor = monitor;
        this.listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        this.listener.Start();
        this.loopTask = Task.Run(this.AcceptLoopAsync);
    }

    public void Tick(ulong tick)
    {
        this.currentTick = tick;
        while (this.mainThreadCalls.TryDequeue(out var call))
        {
            try { call.Completion.TrySetResult(call.Work()); }
            catch (Exception ex) { call.Completion.TrySetException(ex); }
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!this.stop.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
            try
            {
                context = await this.listener.GetContextAsync();
                _ = Task.Run(() => this.HandleAsync(context));
            }
            catch (HttpListenerException) when (this.stop.IsCancellationRequested) { }
            catch (ObjectDisposedException) when (this.stop.IsCancellationRequested) { }
            catch (Exception ex)
            {
                this.monitor.Log($"Bridge listener error: {ex}", LogLevel.Error);
                if (context is not null)
                    context.Response.Close();
            }
        }
    }

    private async Task HandleAsync(HttpListenerContext ctx)
    {
        try
        {
            if (!string.Equals(ctx.Request.Headers["X-Stardew-Agent-Token"], this.token, StringComparison.Ordinal))
            {
                await WriteJson(ctx, 401, new { error = "UNAUTHORIZED" });
                return;
            }

            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (ctx.Request.HttpMethod == "GET" && path == "/health")
            {
                await WriteJson(ctx, 200, new { ok = true, actor_id = this.actorId, tick = this.currentTick });
                return;
            }

            if (ctx.Request.HttpMethod == "GET" && path == "/observe")
            {
                var result = await OnMainThread(() => (object)this.projector.Project());
                await WriteJson(ctx, 200, result);
                return;
            }

            if (ctx.Request.HttpMethod == "POST" && path == "/operation")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                var request = JsonSerializer.Deserialize<OperationRequest>(body, this.json)
                              ?? throw new InvalidDataException("Invalid OperationRequest");
                var result = await OnMainThread(() => (object)this.operations.Submit(request, this.currentTick));
                await WriteJson(ctx, 202, result);
                return;
            }

            if (ctx.Request.HttpMethod == "GET" && path.StartsWith("/operation/", StringComparison.Ordinal))
            {
                var raw = path["/operation/".Length..];
                if (!Guid.TryParse(raw, out var id))
                {
                    await WriteJson(ctx, 400, new { error = "INVALID_OPERATION_ID" });
                    return;
                }
                var result = this.operations.Get(id);
                if (result is null)
                {
                    await WriteJson(ctx, 404, new { error = "NOT_FOUND" });
                    return;
                }
                await WriteJson(ctx, 200, result);
                return;
            }

            await WriteJson(ctx, 404, new { error = "NOT_FOUND" });
        }
        catch (TimeoutException)
        {
            await WriteJson(ctx, 503, new { error = "MAIN_THREAD_TIMEOUT" });
        }
        catch (Exception ex)
        {
            this.monitor.Log($"Request failed: {ex}", LogLevel.Error);
            await WriteJson(ctx, 500, new { error = "INTERNAL_ERROR", detail = ex.Message });
        }
    }

    private async Task<object> OnMainThread(Func<object> work)
    {
        var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        this.mainThreadCalls.Enqueue(new MainThreadCall { Work = work, Completion = tcs });
        return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
    }

    private async Task WriteJson(HttpListenerContext ctx, int status, object payload)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload, this.json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = data.Length;
        await ctx.Response.OutputStream.WriteAsync(data);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        this.stop.Cancel();
        if (this.listener.IsListening)
            this.listener.Stop();
        this.listener.Close();
        try { this.loopTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        this.stop.Dispose();
    }
}
