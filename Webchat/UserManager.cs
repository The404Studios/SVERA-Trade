using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public class UserManager
{
    private readonly Dictionary<string, string> users = new();
    private const string UsersFilePath = "users.json"; // Path to store user credentials

    public UserManager()
    {
        LoadUsers();
    }

    public bool ValidateUser(string username, string password)
    {
        if (users.TryGetValue(username, out var storedPassword))
        {
            return storedPassword == ComputeHash(password);
        }
        return false;
    }

    public bool RegisterUser(string username, string password)
    {
        if (!users.ContainsKey(username))
        {
            users[username] = ComputeHash(password);
            SaveUsers();
            return true;
        }
        return false;
    }

    private void LoadUsers()
    {
        if (File.Exists(UsersFilePath))
        {
            string json = File.ReadAllText(UsersFilePath);
            var loadedUsers = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (loadedUsers != null)
            {
                foreach (var user in loadedUsers)
                {
                    users[user.Key] = user.Value;
                }
            }
        }
    }

    private void SaveUsers()
    {
        string json = JsonSerializer.Serialize(users);
        File.WriteAllText(UsersFilePath, json);
    }

    private string ComputeHash(string input)
    {
        using SHA256 sha256 = SHA256.Create();
        byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        StringBuilder result = new StringBuilder();
        foreach (var b in bytes)
        {
            result.Append(b.ToString("x2"));
        }
        return result.ToString();
    }
}
