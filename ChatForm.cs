using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

public partial class ChatForm : Form
{
    private TextBox txtServerIP, txtServerPort, txtClientIP, txtMessage;
    private Button btnStartServer, btnConnect, btnSend;
    private ListBox lstMessages;
    private ChatServer server;
    private ChatClient client;

    public ChatForm()
    {
        InitializeComponent();
        SetupEventHandlers();
    }

    private void InitializeComponent()
    {
        this.Text = "Чат с уникальными IP";
        this.Size = new System.Drawing.Size(500, 450);
        this.FormClosing += (s, e) => OnFormClosing();

        // Server controls
        new Label { Text = "Сервер IP:", Left = 10, Top = 10, Width = 60 }.AddTo(this);
        txtServerIP = new TextBox { Left = 80, Top = 10, Width = 100, Text = "127.0.0.1" }.AddTo(this);

        new Label { Text = "Порт:", Left = 190, Top = 10, Width = 40 }.AddTo(this);
        txtServerPort = new TextBox { Left = 240, Top = 10, Width = 50, Text = "5000" }.AddTo(this);
        btnStartServer = new Button { Left = 300, Top = 10, Width = 150, Text = "Запустить сервер" }.AddTo(this);

        // Client controls
        new Label { Text = "Ваш IP:", Left = 10, Top = 40, Width = 60 }.AddTo(this);
        txtClientIP = new TextBox { Left = 80, Top = 40, Width = 100, Text = "127.0.0.2" }.AddTo(this);
        btnConnect = new Button { Left = 300, Top = 40, Width = 150, Text = "Подключиться" }.AddTo(this);

        // Chat controls
        lstMessages = new ListBox { Left = 10, Top = 70, Width = 470, Height = 250 }.AddTo(this);
        txtMessage = new TextBox { Left = 10, Top = 330, Width = 380 }.AddTo(this);
        btnSend = new Button { Left = 400, Top = 330, Width = 80, Text = "Отправить" }.AddTo(this);
    }

    private void SetupEventHandlers()
    {
        btnStartServer.Click += (s, e) => StartServer();
        btnConnect.Click += (s, e) => ConnectToServer();
        btnSend.Click += (s, e) => SendMessage();
    }

    private void StartServer()
    {
        try
        {
            if (server != null)
            {
                // Убираем дублирующее сообщение
                return;
            }

            if (!int.TryParse(txtServerPort.Text, out int port) || port < 1 || port > 65535)
            {
                MessageBox.Show("Порт должен быть от 1 до 65535");
                return;
            }

            server = new ChatServer();
            server.MessageReceived += (msg) =>
            {
                this.Invoke((Action)(() => lstMessages.Items.Add(msg)));
            };

            server.StartServer(txtServerIP.Text, port);
            btnStartServer.Enabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка запуска сервера: {ex.Message}");
            server = null;
        }
    }

    private void ConnectToServer()
    {
        try
        {
            if (client != null && client.IsConnected)
            {
                MessageBox.Show("Уже подключено!");
                return;
            }

            string clientIP = txtClientIP.Text.Trim();

            client = new ChatClient(clientIP);
            client.MessageReceived += msg =>
            {
                this.Invoke((Action)(() =>
                {
                    if (msg.StartsWith("!REJECT:"))
                    {
                        // Не показываем сообщение об успешном подключении при ошибке
                        MessageBox.Show(msg.Substring(8), "Ошибка подключения");
                    }
                    else
                    {
                        lstMessages.Items.Add(msg);
                    }
                }));
            };

            client.Connect(txtServerIP.Text, int.Parse(txtServerPort.Text));

            // Показываем сообщение только если не было ошибки
            if (client.IsConnected)
            {
                MessageBox.Show($"Успешно подключено с IP: {clientIP}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ошибка подключения",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            client = null;
        }
    }

    private void SendMessage()
    {
        if (string.IsNullOrWhiteSpace(txtMessage.Text))
        {
            MessageBox.Show("Сообщение не может быть пустым");
            return;
        }

        try
        {
            string message = txtMessage.Text;

            if (client != null && client.IsConnected)
            {
                client.SendMessage(message);
                lstMessages.Items.Add($"Вы: {message}");
            }
            else if (server != null)
            {
                // Сервер сразу увидит свое сообщение через MessageReceived
                server.SendMessage(message);
            }
            else
            {
                MessageBox.Show("Нет активного подключения");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка отправки: {ex.Message}");
        }
        finally
        {
            txtMessage.Clear();
        }
    }

    private void OnFormClosing()
    {
        server?.StopServer();
        client?.Disconnect();
    }
}

public static class ControlExtensions
{
    public static T AddTo<T>(this T control, Control parent) where T : Control
    {
        parent.Controls.Add(control);
        return control;
    }
}
