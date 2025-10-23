
namespace MS_SQL_Proxy;


// ncat --listen 1433 --proxy-type tcp --proxy 192.168.1.32:2022
// socat TCP-LISTEN:1433,fork TCP:192.168.1.32:2022
class Program
{
    static System.Threading.CancellationTokenSource cts = new System.Threading.CancellationTokenSource();
    
    static async System.Threading.Tasks.Task<int> Main(string[] args)
    {
        System.Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        System.Net.Sockets.TcpListener listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Parse("192.168.1.32"), 1433);
        listener.Start();
        
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                System.Net.Sockets.TcpClient client = await listener.AcceptTcpClientAsync();
                System.Net.Sockets.TcpClient server = new System.Net.Sockets.TcpClient();
                await server.ConnectAsync("192.168.1.32", 2022);
                
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    System.Net.Sockets.NetworkStream clientStream = client.GetStream();
                    System.Net.Sockets.NetworkStream serverStream = server.GetStream();
                    
                    System.Threading.Tasks.Task clientToServer = clientStream.CopyToAsync(serverStream, cts.Token);
                    System.Threading.Tasks.Task serverToClient = serverStream.CopyToAsync(clientStream, cts.Token);
                    
                    try
                    {
                        await System.Threading.Tasks.Task.WhenAll(clientToServer, serverToClient);
                    }
                    catch (System.OperationCanceledException)
                    {
                        // Cancellation requested
                    }
                    finally
                    {
                        client.Close();
                        server.Close();
                    }
                }, cts.Token);
            }
        }
        catch (System.OperationCanceledException)
        {
            // Expected on shutdown
        }
        finally
        {
            listener.Stop();
        }
        
        System.Console.WriteLine(" --- Proxy stopped --- ");
        return 0;
    }
}