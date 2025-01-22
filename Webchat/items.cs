using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WebchatApp // Added namespace to avoid conflicts
{
    // Ensure there's only one definition of the Item class
    public class Item
    {
        public string ItemName { get; set; }
        public decimal Price { get; set; }
    }

    public class ItemForumServer
    {
        private static readonly string ItemsFilePath = "items.json";
        private static List<Item> Items = new List<Item>();

        public static async Task Mai(string[] args) // Renamed method to Main
        {
            LoadItems();

            using HttpListener httpListener = new HttpListener();
            httpListener.Prefixes.Add("http://127.0.0.1:8080/");
            httpListener.Start();
            Console.WriteLine("Server started at http://127.0.0.1:8080");

            while (true)
            {
                HttpListenerContext context = await httpListener.GetContextAsync();
                await HandleRequest(context);
            }
        }

        private static async Task HandleRequest(HttpListenerContext context)
        {
            switch (context.Request.Url.AbsolutePath)
            {
                case "/post-item":
                    await HandlePostItem(context);
                    break;
                case "/items":
                    ServeItems(context);
                    break;
                case "/items.html":
                    ServeFile(context, "items.html");
                    break;
                default:
                    ServeFile(context, "items.html"); // Serve items page by default
                    break;
            }
        }

        private static async Task HandlePostItem(HttpListenerContext context)
        {
            try
            {
                string requestBody = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
                var newItem = JsonSerializer.Deserialize<Item>(requestBody);

                if (newItem != null && !string.IsNullOrWhiteSpace(newItem.ItemName) && newItem.Price > 0)
                {
                    Items.Add(newItem);
                    SaveItems();
                    Console.WriteLine($"Item posted: {newItem.ItemName}, Price: {newItem.Price}");
                    context.Response.StatusCode = 201; // Created
                }
                else
                {
                    context.Response.StatusCode = 400; // Bad Request
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error posting item: {ex.Message}");
                context.Response.StatusCode = 500; // Internal Server Error
            }
            finally
            {
                context.Response.Close();
            }
        }

        private static void ServeItems(HttpListenerContext context)
        {
            var json = JsonSerializer.Serialize(Items);
            byte[] content = Encoding.UTF8.GetBytes(json);
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = content.Length;
            context.Response.OutputStream.Write(content, 0, content.Length);
            context.Response.OutputStream.Close();
        }

        private static void ServeFile(HttpListenerContext context, string fileName)
        {
            if (!File.Exists(fileName))
            {
                context.Response.StatusCode = 404; // Not Found
                context.Response.Close();
                return;
            }

            byte[] content = File.ReadAllBytes(fileName);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = content.Length;
            context.Response.OutputStream.Write(content, 0, content.Length);
            context.Response.OutputStream.Close();
        }

        private static void LoadItems()
        {
            if (File.Exists(ItemsFilePath))
            {
                string json = File.ReadAllText(ItemsFilePath);
                Items = JsonSerializer.Deserialize<List<Item>>(json) ?? new List<Item>();
            }
        }

        private static void SaveItems()
        {
            string json = JsonSerializer.Serialize(Items);
            File.WriteAllText(ItemsFilePath, json);
        }
    }
}
