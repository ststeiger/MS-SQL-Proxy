
namespace MS_SQL_Proxy
{
    
    using System;
    

    class TweakedTcpProxy 
    {
        private static readonly System.Threading.CancellationTokenSource cts = 
            new System.Threading.CancellationTokenSource();

        private static readonly System.Buffers.ArrayPool<byte> bufferPool = 
            System.Buffers.ArrayPool<byte>.Shared;

        private const int BUFFER_SIZE = 8192;
        private const int LISTEN_PORT = 1433;
        private const string LISTEN_IP = "192.168.1.32";
        private const string TARGET_HOST = "192.168.1.32";
        private const int TARGET_PORT = 2022;

        public static async System.Threading.Tasks.Task<int> Test()
        {
            System.Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            // Increase ThreadPool size to prevent starvation under load
            System.Threading.ThreadPool.SetMinThreads(200, 200);

            System.Net.Sockets.Socket listenerSocket = 
                new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.InterNetwork,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Tcp
            );

            listenerSocket.SetSocketOption(
                System.Net.Sockets.SocketOptionLevel.Socket,
                System.Net.Sockets.SocketOptionName.ReuseAddress, 
                true
            );

            listenerSocket.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Parse(LISTEN_IP), LISTEN_PORT));
            listenerSocket.Listen(backlog: 1024);

            System.Console.WriteLine($"Listening on {LISTEN_IP}:{LISTEN_PORT} → {TARGET_HOST}:{TARGET_PORT}");

            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    System.Net.Sockets.Socket clientSocket = await listenerSocket.AcceptAsync(cts.Token);
                    _ = HandleConnectionAsync(clientSocket);
                }
            }
            catch (System.OperationCanceledException)
            {
                // graceful shutdown
            }
            finally
            {
                listenerSocket.Close();
            }

            System.Console.WriteLine("Proxy stopped.");
            return 0;
        } // End Main 

        private static async System.Threading.Tasks.Task HandleConnectionAsync(
            System.Net.Sockets.Socket clientSocket
        )
        {
            using (clientSocket)
            {
                System.Net.Sockets.Socket serverSocket = 
                    new System.Net.Sockets.Socket(
                        System.Net.Sockets.AddressFamily.InterNetwork,
                        System.Net.Sockets.SocketType.Stream,
                        System.Net.Sockets.ProtocolType.Tcp
                );

                // Apply low-latency tuning
                clientSocket.NoDelay = true;
                clientSocket.ReceiveBufferSize = BUFFER_SIZE;
                clientSocket.SendBufferSize = BUFFER_SIZE;

                serverSocket.NoDelay = true;
                serverSocket.ReceiveBufferSize = BUFFER_SIZE;
                serverSocket.SendBufferSize = BUFFER_SIZE;

                try
                {
                    await serverSocket.ConnectAsync(TARGET_HOST, TARGET_PORT, cts.Token);

                    _ = PumpAsync(clientSocket, serverSocket, "C→S");
                    _ = PumpAsync(serverSocket, clientSocket, "S→C");
                }
                catch (System.Exception ex)
                {
                    System.Console.WriteLine($"[{System.DateTime.Now:T}] Connection failed: {ex.Message}");
                    clientSocket.Close();
                    serverSocket.Close();
                }
            }
        }

        private static async System.Threading.Tasks.Task PumpAsync(
            System.Net.Sockets.Socket source,
            System.Net.Sockets.Socket destination, 
            string direction
        )
        {
            byte[] buffer = bufferPool.Rent(BUFFER_SIZE);
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    int bytesRead = await source.ReceiveAsync(
                        buffer,
                        System.Net.Sockets.SocketFlags.None, 
                        cts.Token
                    );

                    if (bytesRead == 0)
                        break;

                    int bytesSent = 0;
                    while (bytesSent < bytesRead)
                    {
                        int sent = await destination.SendAsync(
                            buffer.AsMemory(bytesSent, bytesRead - bytesSent),
                            System.Net.Sockets.SocketFlags.None, 
                            cts.Token
                        );

                        if (sent == 0)
                            return;
                        bytesSent += sent;
                    }
                }
            }
            catch (System.OperationCanceledException)
            {
                // normal shutdown
            }
            catch (System.Net.Sockets.SocketException ex)
            {
                // typical disconnects: ConnectionReset, BrokenPipe, etc.
                if (ex.SocketErrorCode != System.Net.Sockets.SocketError.ConnectionReset 
                    && 
                    ex.SocketErrorCode != System.Net.Sockets.SocketError.Shutdown
                )
                    System.Console.WriteLine($"[{System.DateTime.Now:T}] {direction} socket error: {ex.SocketErrorCode}");
            }
            finally
            {
                bufferPool.Return(buffer);
                source.Close();
                destination.Close();
            }
        }
    }

}
