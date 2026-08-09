using System;
using System.IO;

namespace Sayra.Backend.Api
{
    public static class EnvLoader
    {
        public static void Load()
        {
            try
            {
                var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
                FileInfo? envFile = null;

                // Traverse up to 6 parent directories to find a .env file
                for (int i = 0; i < 6; i++)
                {
                    if (currentDir == null) break;

                    var file = new FileInfo(Path.Combine(currentDir.FullName, ".env"));
                    if (file.Exists)
                    {
                        envFile = file;
                        break;
                    }
                    currentDir = currentDir.Parent;
                }

                // If not found in parent folders, check the current working directory as well
                if (envFile == null)
                {
                    var file = new FileInfo(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
                    if (file.Exists)
                    {
                        envFile = file;
                    }
                }

                if (envFile == null)
                {
                    Console.WriteLine("[EnvLoader] No .env file found in search paths.");
                    return;
                }

                Console.WriteLine($"[EnvLoader] Loading environment variables from: {envFile.FullName}");

                var lines = File.ReadAllLines(envFile.FullName);
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("#")) continue;

                    var eqIndex = trimmedLine.IndexOf('=');
                    if (eqIndex <= 0) continue;

                    var key = trimmedLine.Substring(0, eqIndex).Trim();
                    var val = trimmedLine.Substring(eqIndex + 1).Trim();

                    // Strip inline comment if any, but ONLY if the comment starts outside of quotes
                    if (val.Contains('#'))
                    {
                        bool inDoubleQuotes = false;
                        bool inSingleQuotes = false;
                        int commentIndex = -1;
                        for (int k = 0; k < val.Length; k++)
                        {
                            char c = val[k];
                            if (c == '"' && !inSingleQuotes) inDoubleQuotes = !inDoubleQuotes;
                            else if (c == '\'' && !inDoubleQuotes) inSingleQuotes = !inDoubleQuotes;
                            else if (c == '#' && !inDoubleQuotes && !inSingleQuotes)
                            {
                                commentIndex = k;
                                break;
                            }
                        }
                        if (commentIndex >= 0)
                        {
                            val = val.Substring(0, commentIndex).Trim();
                        }
                    }

                    // Strip surrounding quotes
                    if (val.Length >= 2 &&
                        ((val.StartsWith("\"") && val.EndsWith("\"")) ||
                         (val.StartsWith("'") && val.EndsWith("'"))))
                    {
                        val = val.Substring(1, val.Length - 2);
                    }

                    // Only set if not already set in environment
                    if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                    {
                        Environment.SetEnvironmentVariable(key, val);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EnvLoader] Failed to load .env file: {ex.Message}");
            }
        }
    }
}
