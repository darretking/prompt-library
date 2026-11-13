using System.IO;
using System.IO.Pipes;
using System.Text;

namespace PromptLibrary.App.Startup;

/// <summary>
/// How a second launch hands a team file or link to the running copy: one UTF-8 message (≤ 4 KB) over a named pipe
/// that only the current user can open.
/// </summary>
public static class JoinPipe
{
    public static string Name => "PromptLibrary.Join" + Paths.InstanceSuffix;

    /// <summary>Sends the message; false when nobody answers within the timeout (the caller falls back to the Show signal).</summary>
    public static bool Send(string message, string pipeName, TimeSpan timeout)
    {
        if (JoinArguments.Validate(message) is not { } text) return false;
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect((int)timeout.TotalMilliseconds);
            var bytes = Encoding.UTF8.GetBytes(text);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Starts listening on a background thread; each valid message is passed to <paramref name="onMessage"/> (any thread).</summary>
    public static void Listen(string pipeName, Action<string> onMessage, CancellationToken stop)
    {
        var thread = new Thread(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                        PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
                    server.WaitForConnectionAsync(stop).GetAwaiter().GetResult();
                    if (Read(server) is { } message && JoinArguments.Validate(message) is { } text)
                        onMessage(text);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    Thread.Sleep(200); // a broken connection must not spin; the next client gets a new pipe
                }
            }
        })
        {
            IsBackground = true,
            Name = "PromptLibrary.JoinPipe",
        };
        thread.Start();
    }

    // Reads up to one byte over the limit so an oversized message is seen and dropped; a slow sender is cut off.
    private static string? Read(Stream stream)
    {
        var buffer = new byte[JoinArguments.MaxBytes + 1];
        var total = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            while (total < buffer.Length)
            {
                var n = stream.ReadAsync(buffer.AsMemory(total), timeout.Token).AsTask().GetAwaiter().GetResult();
                if (n == 0) break;
                total += n;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        return total > JoinArguments.MaxBytes ? null : Encoding.UTF8.GetString(buffer, 0, total);
    }
}
