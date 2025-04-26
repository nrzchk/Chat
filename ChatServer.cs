using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class ChatServer : IDisposable
{
    private TcpListener server;
    private List<TcpClient> clients = new List<TcpClient>();
    private HashSet<string> connectedIPs = new HashSet<string>();
    private readonly object ipLock = new object();
    private readonly object clientsLock = new object();
    private bool isRunning;
    private Thread acceptThread;
    private bool isStartupMessageSent;

    public event Action<string> MessageReceived;

    public void StartServer(string ip, int port)
    {
        if (isRunning)
        {
            if (!isStartupMessageSent)
            {
                MessageReceived?.Invoke("Сервер уже запущен");
                isStartupMessageSent = true;
            }
            return;
        }
         
        try
        {
            IPAddress ipAddress = IPAddress.Parse(ip);
            server = new TcpListener(ipAddress, port);
            server.Start();
            isRunning = true;
            isStartupMessageSent = false;

            acceptThread = new Thread(AcceptClients)
            {
                IsBackground = true,
                Name = "AcceptClientsThread"
            };
            acceptThread.Start();

            MessageReceived?.Invoke($"Сервер запущен на {ip}:{port}");
            isStartupMessageSent = true;
        }
        catch (Exception ex)
        {
            MessageReceived?.Invoke($"Ошибка запуска сервера: {ex.Message}");
            throw;
        }
    }
    public void SendMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return;

       
        string serverViewMessage = $"Сервер(Вы): {message}";
        
        string clientsViewMessage = $"Сервер: {message}";

        // Показываем серверу его сообщение
        MessageReceived?.Invoke(serverViewMessage);

        // Рассылаем клиентам
        BroadcastMessage(clientsViewMessage, null, false);
    }

    private void AcceptClients()
    {
        while (isRunning)
        {
            try
            {
                TcpClient client = server.AcceptTcpClient();
                var clientEndPoint = (IPEndPoint)client.Client.RemoteEndPoint;
                string clientIP = clientEndPoint.Address.ToString();

                lock (ipLock)
                {
                    if (connectedIPs.Contains(clientIP))
                    {
                        RejectClient(client, $"IP {clientIP} уже используется");
                        continue;
                    }
                    connectedIPs.Add(clientIP);
                }

                lock (clientsLock) clients.Add(client);

                MessageReceived?.Invoke($"Новое подключение: {clientIP}");

                ThreadPool.QueueUserWorkItem(_ => HandleClient(client));
            }
            catch (SocketException) when (!isRunning)
            {
                break;
            }
            catch (Exception ex)
            {
                if (isRunning)
                    MessageReceived?.Invoke($"Ошибка приема подключения: {ex.Message}");
            }
        }
    }

    private void RejectClient(TcpClient client, string message)
    {
        try
        {
            byte[] data = Encoding.UTF8.GetBytes($"!REJECT:{message}");
            client.GetStream().Write(data, 0, data.Length);
            client.Close();
            MessageReceived?.Invoke($"Отклонено подключение: {message}");
        }
        catch { }
    }

    private void HandleClient(TcpClient client)
    {
        string clientIP = null;
        try
        {
            clientIP = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[4096];

            client.ReceiveTimeout = 300000;

            while (isRunning && client.Connected)
            {
                int bytesRead;
                try
                {
                    bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;
                }
                catch (IOException) { break; }

                string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                string fullMessage = $"[{clientIP}]: {message}";

                // Отправляем сообщение всем клиентам и в UI один раз
                BroadcastMessage(fullMessage, client, true);
            }
        }
        catch (Exception ex)
        {
            MessageReceived?.Invoke($"Ошибка клиента {clientIP}: {ex.Message}");
        }
        finally
        {
            if (clientIP != null)
            {
                lock (ipLock) connectedIPs.Remove(clientIP);
                MessageReceived?.Invoke($"Отключение: {clientIP}");
            }

            lock (clientsLock) clients.Remove(client);
            client.Close();
        }
    }

    private void BroadcastMessage(string message, TcpClient sender, bool includeUI)
    {
        byte[] data = Encoding.UTF8.GetBytes(message);

        if (includeUI)
        {
            MessageReceived?.Invoke(message);
        }

        lock (clientsLock)
        {
            foreach (var client in clients.ToArray())
            {
                if (client == sender || !client.Connected) continue;

                try
                {
                    client.GetStream().Write(data, 0, data.Length);
                }
                catch
                {
                    // Игнорируем ошибки отправки
                }
            }
        }
    }

    public void StopServer()
    {
        if (!isRunning) return;

        isRunning = false;
        try
        {
            server?.Stop();
        }
        catch { }

        lock (clientsLock)
        {
            foreach (var client in clients)
            {
                try { client.Close(); } catch { }
            }
            clients.Clear();
        }

        lock (ipLock) connectedIPs.Clear();
    }

    public void Dispose()
    {
        StopServer();
    }
}
