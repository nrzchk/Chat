using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class ChatClient : IDisposable
{
    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;
    private bool isDisposed;
    private readonly object disconnectLock = new object();

    public string ClientIP { get; }
    public bool IsConnected => client?.Connected == true;

    public event Action<string> MessageReceived;

    public ChatClient(string ipAddress)
    {
        if (!IPAddress.TryParse(ipAddress, out _))
            throw new ArgumentException("Неверный формат IP-адреса");

        this.ClientIP = ipAddress;
    }

    public void Connect(string serverIP, int port)
    {
        try
        {
            if (IsConnected) return;

            var localEndPoint = new IPEndPoint(IPAddress.Parse(ClientIP), 0);
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(localEndPoint);

            var result = socket.BeginConnect(serverIP, port, null, null);
            if (!result.AsyncWaitHandle.WaitOne(3000))
            {
                throw new TimeoutException("Таймаут подключения");
            }

            socket.EndConnect(result);
            client = new TcpClient { Client = socket };
            stream = client.GetStream();

            // Проверяем, не отклонил ли сервер подключение
            CheckServerRejection();

            receiveThread = new Thread(ReceiveMessages) { IsBackground = true };
            receiveThread.Start();
        }
        catch (Exception ex)
        {
            Dispose();
            throw new Exception($"Ошибка подключения: {ex.Message}");
        }
    }

    private void CheckServerRejection()
    {
        byte[] buffer = new byte[1024];
        if (client.Available > 0)
        {
            int bytesRead = client.GetStream().Read(buffer, 0, buffer.Length);
            string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
            if (response.StartsWith("!REJECT:"))
            {
                Dispose();
                throw new Exception(response.Substring(8));
            }
        }
    }

    private void ReceiveMessages()
    {
        try
        {
            byte[] buffer = new byte[4096];
            while (IsConnected)
            {
                int bytesRead;
                try
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                }
                catch (IOException) { break; }

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                MessageReceived?.Invoke(message);
            }
        }
        catch (Exception ex)
        {
            MessageReceived?.Invoke($"Ошибка соединения: {ex.Message}");
        }
        finally
        {
            Dispose();
        }
    }

    public void SendMessage(string message)
    {
        lock (disconnectLock)
        {
            if (!IsConnected || string.IsNullOrEmpty(message)) return;

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex)
            {
                MessageReceived?.Invoke($"Ошибка отправки: {ex.Message}");
                Dispose();
            }
        }
    }

    public void Disconnect()
    {
        Dispose();
    }

    public void Dispose()
    {
        lock (disconnectLock)
        {
            if (isDisposed) return;
            isDisposed = true;

            try
            {
                receiveThread?.Join(500);
                stream?.Close();
                client?.Close();
            }
            finally
            {
                receiveThread = null;
                stream = null;
                client = null;
            }
        }
    }
}
