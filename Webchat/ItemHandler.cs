using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static WebSocketChatServer;

public class Item
{
    public string Name { get; set; }
    public decimal Price { get; set; }
    public string DiscordName { get; set; }
    public string CharacterName { get; set; }
    public int Quantity { get; set; }
    public string Email { get; set; }
    public DateTime TimePosted { get; set; } // This will hold the time of posting
}


public class ItemHandler
{
    // Method to handle posting items
    public async Task PostItem(HttpListenerContext context)
    {
        string requestBody = await new StreamReader(context.Request.InputStream).ReadToEndAsync();
        var item = JsonSerializer.Deserialize<Item>(requestBody);

        item.TimePosted = DateTime.UtcNow; // Set the posting time
        SaveItem(item); // Save the item in your storage

        context.Response.StatusCode = 200; // Success
        context.Response.Close();
    }

    // Method to handle getting items
    public async Task GetItems(HttpListenerContext context)
    {
        var items = LoadItems(); // Load items from storage
        var jsonResponse = JsonSerializer.Serialize(items);
        context.Response.ContentType = "application/json";
        await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(jsonResponse));
        context.Response.StatusCode = 200; // Success
        context.Response.Close();
    }

    private void SaveItem(Item item)
    {
        // Implement your storage logic here
    }

    private List<Item> LoadItems()
    {
        // Implement your loading logic here
        return new List<Item>(); // Return the loaded items
    }
}
