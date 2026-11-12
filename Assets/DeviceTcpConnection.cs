using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

// Unity 2022/.NET Standard compatible: no newer ConnectAsync(token)/WaitAsync dependency.
internal static class DeviceTcpConnection
{
    // Read-only UI information; TcpClient.Connected is last-known state, not a live health probe.
    public static bool IsOpen(TcpClient client)
    { try { return client != null && client.Connected; } catch (ObjectDisposedException) { return false; } }
    public static string Describe(TcpClient client)
    {
        try
        {
            if (!IsOpen(client)) return "未连接";
            if (!(client.Client.RemoteEndPoint is IPEndPoint endpoint)) return "端点未知";
            var address = endpoint.Address;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return new IPEndPoint(address, endpoint.Port).ToString();
        }
        catch (ObjectDisposedException) { return "未连接"; }
        catch (SocketException) { return "未连接"; }
    }
    public static bool AbsentOrMatches(TcpClient client, string host, int port) => !IsOpen(client) || Matches(client, host, port);

    public static async Task<TcpClient> OpenAsync(string host, int port, float timeoutSeconds, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("设备IP不能为空");
        if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (float.IsNaN(timeoutSeconds) || float.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
        cancel.ThrowIfCancellationRequested();
        var client = new TcpClient();
        bool transferred = false;
        try
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                Task connecting = client.ConnectAsync(host, port);
                Task expired = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), deadline.Token);
                if (await Task.WhenAny(connecting, expired).ConfigureAwait(false) != connecting)
                {
                    client.Close();
                    _ = ObserveAsync(connecting); // Observe a late failure without delaying UI/cancellation.
                    cancel.ThrowIfCancellationRequested();
                    throw new TimeoutException($"连接 {host}:{port} 超时（{timeoutSeconds:0.##}秒）");
                }
                deadline.Cancel();
                await connecting.ConfigureAwait(false);
                cancel.ThrowIfCancellationRequested();
                client.NoDelay = true;
                transferred = true;
                return client;
            }
        }
        finally { if (!transferred) client.Close(); }
    }

    internal static async Task ObserveAsync(Task task) { try { await task.ConfigureAwait(false); } catch { } }

    public static bool Matches(TcpClient client, string host, int port)
    {
        try
        {
            if (client == null || !client.Connected || !(client.Client.RemoteEndPoint is IPEndPoint endpoint)) return false;
            if (!IPAddress.TryParse(host, out var expected)) return false;
            var actual = endpoint.Address;
            if (actual.IsIPv4MappedToIPv6) actual = actual.MapToIPv4();
            if (expected.IsIPv4MappedToIPv6) expected = expected.MapToIPv4();
            return actual.Equals(expected) && endpoint.Port == port;
        }
        catch (ObjectDisposedException) { return false; }
        catch (SocketException) { return false; }
    }
}
