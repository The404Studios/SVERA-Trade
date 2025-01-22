using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class WebSocketChatServa
{
    private static readonly ConcurrentDictionary<string, WebSocket> Clients = new(); // Store connected clients
    private const string UsersFilePath = "users.json"; // User storage file
    private static readonly Dictionary<string, string> Users = new(); // Dictionary to store registered users

    static async Task MA1N(string[] args)
    {
        LoadUsers(); // Load users from file

        using HttpListener httpListener = new HttpListener();
        httpListener.Prefixes.Add("http://127.0.0.1:8080/");
        httpListener.Start();
        Console.WriteLine("Server started at http://127.0.0.1:8080");

        while (true)
        {
            HttpListenerContext context = await httpListener.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                await HandleWebSocket(context); // Handle WebSocket requests
            }
            else
            {
                switch (context.Request.Url.AbsolutePath)
                {
                    case "/login":
                        await HandleLogin(context); // Handle login requests
                        break;
                    case "/register":
                        await HandleRegistration(context); // Handle registration requests
                        break;
                    case "/":
                    case "/chat.html":
                        ServeFile(context, "chat.html"); // Serve the chat HTML file
                        break;
                    default:
                        context.Response.StatusCode = 404; // Not Found
                        context.Response.Close();
                        break;
                }
            }
        }
    }

    private static async Task HandleWebSocket(HttpListenerContext context)
    {
        HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(null);
        WebSocket webSocket = wsContext.WebSocket;
        string clientId = context.Request.QueryString["username"] ?? Guid.NewGuid().ToString();

        // Add the client to the dictionary
        Clients[clientId] = webSocket;
        Console.WriteLine($"{clientId} connected.");

        // Continuously receive messages and broadcast to all clients
        byte[] buffer = new byte[1024 * 4];
        while (webSocket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                string message = $"{clientId}: {Encoding.UTF8.GetString(buffer, 0, result.Count)}";
                Console.WriteLine($"Received: {message}");
                await BroadcastMessage(message);
            }
        }

        // Remove client on disconnect
        Clients.TryRemove(clientId, out _);
        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected", CancellationToken.None);
        Console.WriteLine($"{clientId} disconnected.");
    }

    private static async Task BroadcastMessage(string message)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        foreach (var client in Clients.Values)
        {
            if (client.State == WebSocketState.Open)
            {
                await client.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }

    private static async Task HandleLogin(HttpListenerContext context)
    {
        string requestBody = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
        var loginDetails = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
        if (loginDetails != null && loginDetails.TryGetValue("email", out string email) && loginDetails.TryGetValue("password", out string password))
        {
            if (Users.TryGetValue(email, out var storedPassword) && storedPassword == ComputeHash(password))
            {
                context.Response.Redirect($"/chat.html?username={Uri.EscapeDataString(email)}");
                Console.WriteLine($"User {email} logged in successfully.");
            }
            else
            {
                context.Response.StatusCode = 401; // Unauthorized
            }
        }
        else
        {
            context.Response.StatusCode = 400; // Bad Request
        }
        context.Response.Close();
    }

    private static async Task HandleRegistration(HttpListenerContext context)
    {
        string requestBody = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
        var registrationDetails = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);
        if (registrationDetails != null && registrationDetails.TryGetValue("email", out string email) && registrationDetails.TryGetValue("password", out string password))
        {
            if (Users.ContainsKey(email))
            {
                context.Response.StatusCode = 409; // Conflict - User already exists
            }
            else
            {
                Users[email] = ComputeHash(password);
                SaveUsers(); // Save new user
                context.Response.StatusCode = 201; // Created
                Console.WriteLine($"User {email} registered successfully.");
            }
        }
        else
        {
            context.Response.StatusCode = 400; // Bad Request
        }
        context.Response.Close();
    }

    private static void LoadUsers()
    {
        if (File.Exists(UsersFilePath))
        {
            string json = File.ReadAllText(UsersFilePath);
            var users = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (users != null)
            {
                foreach (var user in users)
                {
                    Users[user.Key] = user.Value;
                }
            }
        }
    }

    private static void SaveUsers()
    {
        var json = JsonSerializer.Serialize(Users);
        File.WriteAllText(UsersFilePath, json);
    }

    private static string ComputeHash(string input)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(bytes);
    }

    private static void ServeFile(HttpListenerContext context, string filename)
    {
        string path = Path.Combine(Directory.GetCurrentDirectory(), filename);
        if (File.Exists(path))
        {
            byte[] content = File.ReadAllBytes(path);
            context.Response.ContentLength64 = content.Length;
            context.Response.OutputStream.Write(content, 0, content.Length);
            context.Response.OutputStream.Close();
            Console.WriteLine($"Served file: {filename}");
        }
        else
        {
            context.Response.StatusCode = 404; // Not Found
            context.Response.Close();
            Console.WriteLine($"File not found: {filename}");
        }
    }
}
