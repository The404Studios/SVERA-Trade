using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

class WebSocketChatServer
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<WebSocket>> RoomClients = new();
    private static readonly Dictionary<string, string> Users = new();
    private static readonly HashSet<string> ActiveUsers = new();
    private static readonly List<Item> ForumItems = new();
    private const string UsersFilePath = "users.json";
    private const string ItemsFilePath = "items.json"; // File to store forum items
    private const string LogFilePath = "server.log";

    static async Task Main(string[] args)
    {
        LoadUsers();
        LoadItems(); // Load forum items

        using HttpListener httpListener = new HttpListener();
        httpListener.Prefixes.Add("http://127.0.0.1:8080/");
        httpListener.Start();
        Log("Server started at http://127.0.0.1:8080");

        while (true)
        {
            HttpListenerContext context = await httpListener.GetContextAsync();
            await HandleRequest(context);
        }
    }

    private static async Task HandleRequest(HttpListenerContext context)
    {
        Log($"Received request: {context.Request.HttpMethod} {context.Request.Url}");

        switch (context.Request.Url.AbsolutePath)
        {
            case "/login":
                await HandleLogin(context);
                break;
            case "/register":
                await HandleRegistration(context);
                break;
            case "/rooms":
                ServeRooms(context);
                break;
            case "/change-password":
                await HandleChangePassword(context);
                break;
            case "/forum-items":
                ServeForumItems(context); // New endpoint for fetching forum items
                break;
            case "/post-item":
                await HandlePostItem(context); // New endpoint for posting forum items
                break;
            case "/register.html":
            case "/login.html":
            case "/profile.html":
            case "/chat.html":
            case "/room.html":
            case "/forum.html": // Serve forum.html
                ServeFile(context, context.Request.Url.AbsolutePath.Substring(1)); // Remove leading '/'
                break;
            default:
                if (context.Request.IsWebSocketRequest)
                {
                    await HandleWebSocket(context);
                }
                else
                {
                    ServeFile(context, "login.html");
                }
                break;
        }
    }

    private static async Task HandleWebSocket(HttpListenerContext context)
    {
        HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(null);
        WebSocket webSocket = wsContext.WebSocket;

        string roomName = context.Request.QueryString["room"] ?? "default"; // Default room name if none is provided
        var clients = RoomClients.GetOrAdd(roomName, _ => new ConcurrentBag<WebSocket>());
        clients.Add(webSocket);

        Log($"User connected to room: {roomName}");

        await HandleChat(webSocket, roomName);
    }

    private static void ServeRooms(HttpListenerContext context)
    {
        var json = JsonSerializer.Serialize(new { rooms = RoomClients.Keys });
        byte[] content = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = content.Length;
        context.Response.OutputStream.Write(content, 0, content.Length);
        context.Response.OutputStream.Close();
    }

    private static async Task HandleChangePassword(HttpListenerContext context)
    {
        // Implementation for changing password
        context.Response.StatusCode = 501; // Not Implemented
        context.Response.Close();
    }

    private static void ServeFile(HttpListenerContext context, string fileName)
    {
        if (!File.Exists(fileName))
        {
            context.Response.StatusCode = 404; // Not Found
            context.Response.Close();
            Log($"File not found: {fileName}");
            return;
        }

        byte[] content = File.ReadAllBytes(fileName);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = content.Length;
        context.Response.OutputStream.Write(content, 0, content.Length);
        context.Response.OutputStream.Close();
        Log($"Served file: {fileName}");
    }

    private static async Task HandleChat(WebSocket webSocket, string roomName)
    {
        byte[] buffer = new byte[1024 * 4];
        WebSocketReceiveResult result;

        while ((result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None)).MessageType != WebSocketMessageType.Close)
        {
            string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
            Console.WriteLine($"Received message in room {roomName}: {message}");
            Log($"Received message in room {roomName}: {message}");
            await BroadcastMessage(message, roomName);
        }

        RoomClients[roomName].TryTake(out webSocket);
        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnected", CancellationToken.None);
        Log($"User disconnected from room: {roomName}");
    }

    private static async Task BroadcastMessage(string message, string roomName)
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);
        foreach (var client in RoomClients[roomName])
        {
            if (client.State == WebSocketState.Open)
            {
                await client.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        Log($"Broadcasted message in room {roomName}: {message}");
    }

    private static async Task HandleRegistration(HttpListenerContext context)
    {
        try
        {
            string requestBody = await new StreamReader(context.Request.InputStream).ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                Log("Request body is empty.");
                context.Response.StatusCode = 400; // Bad Request
                context.Response.Close();
                return;
            }

            var registrationDetails = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            if (registrationDetails == null ||
                !registrationDetails.TryGetValue("email", out string email) ||
                !registrationDetails.TryGetValue("password", out string password))
            {
                Log("Registration details are missing or invalid.");
                context.Response.StatusCode = 400; // Bad Request
                context.Response.Close();
                return;
            }

            string passwordHash = ComputeHash(password);

            if (Users.ContainsKey(email))
            {
                Log($"User {email} already exists.");
                context.Response.StatusCode = 409; // Conflict - user already exists
                context.Response.Close();
                return;
            }

            Users[email] = passwordHash;
            SaveUsers();
            Log($"User {email} registered successfully.");
            context.Response.StatusCode = 201; // Created
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Log($"Error during registration: {ex.Message}");
            context.Response.StatusCode = 500; // Internal Server Error
            context.Response.Close();
        }
    }

    private static async Task HandleLogin(HttpListenerContext context)
    {
        try
        {
            string requestBody = await new StreamReader(context.Request.InputStream).ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                Log("Request body is empty.");
                context.Response.StatusCode = 400; // Bad Request
                context.Response.Close();
                return;
            }

            var loginDetails = JsonSerializer.Deserialize<Dictionary<string, string>>(requestBody);

            if (loginDetails == null ||
                !loginDetails.TryGetValue("email", out string email) ||
                !loginDetails.TryGetValue("password", out string password))
            {
                Log("Login details are missing or invalid.");
                context.Response.StatusCode = 400; // Bad Request
                context.Response.Close();
                return;
            }

            if (Users.TryGetValue(email, out var storedPassword) && storedPassword == ComputeHash(password))
            {
                ActiveUsers.Add(email);
                context.Response.Redirect("/chat.html?username=" + Uri.EscapeDataString(email));
                context.Response.Close();
                Log($"User {email} logged in successfully.");
                return;
            }

            Log($"Login failed for user: {email}");
            context.Response.StatusCode = 401; // Unauthorized
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Log($"Error during login: {ex.Message}");
            context.Response.StatusCode = 500; // Internal Server Error
            context.Response.Close();
        }
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

    private static void LoadItems()
    {
        if (File.Exists(ItemsFilePath))
        {
            string json = File.ReadAllText(ItemsFilePath);
            var items = JsonSerializer.Deserialize<List<Item>>(json);
            if (items != null)
            {
                ForumItems.AddRange(items);
            }
        }
    }

    private static void SaveUsers()
    {
        var json = JsonSerializer.Serialize(Users);
        File.WriteAllText(UsersFilePath, json);
    }

    private static void SaveItems()
    {
        var json = JsonSerializer.Serialize(ForumItems);
        File.WriteAllText(ItemsFilePath, json);
    }

    private static void ServeForumItems(HttpListenerContext context)
    {
        var json = JsonSerializer.Serialize(ForumItems);
        byte[] content = Encoding.UTF8.GetBytes(json);
        context.Response.ContentType = "application/json";
        context.Response.ContentLength64 = content.Length;
        context.Response.OutputStream.Write(content, 0, content.Length);
        context.Response.OutputStream.Close();
        Log($"Served forum items.");
    }

    private static async Task HandlePostItem(HttpListenerContext context)
    {
        try
        {
            string requestBody = await new StreamReader(context.Request.InputStream).ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(requestBody))
            {
                Log("Request body is empty.");
                context.Response.StatusCode = 400; // Bad Request
                context.Response.Close();
                return;
            }

            var itemDetails = JsonSerializer.Deserialize<Item>(requestBody);

            if (itemDetails == null)
            {
                Log("Item details are missing or invalid.");
                context.Response.StatusCode = 400; // Bad Request
                context.Response.Close();
                return;
            }

            ForumItems.Add(itemDetails);
            SaveItems(); // Save the new item to the file
            Log($"Item posted: {itemDetails.Name} for {itemDetails.Price} USD.");
            context.Response.StatusCode = 201; // Created
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Log($"Error during posting item: {ex.Message}");
            context.Response.StatusCode = 500; // Internal Server Error
            context.Response.Close();
        }
    }

    private static void Log(string message)
    {
        Console.WriteLine(message);
        File.AppendAllText(LogFilePath, $"{DateTime.UtcNow}: {message}\n");
    }

    private static string ComputeHash(string input)
    {
        using SHA256 sha256Hash = SHA256.Create();
        byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
        StringBuilder builder = new StringBuilder();
        foreach (byte b in bytes)
        {
            builder.Append(b.ToString("x2"));
        }
        return builder.ToString();
    }

    private class Item
    {
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string Email { get; set; }
        public string DiscordName { get; set; }
        public string CharacterName { get; set; }
        public DateTime TimeOfPosting { get; set; }
        public int Quantity { get; set; }
    }
}
