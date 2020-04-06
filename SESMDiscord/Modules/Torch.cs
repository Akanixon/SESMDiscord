using System;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
//Discord Libs
using Discord;
using Discord.Commands;
using Microsoft.Extensions.Configuration;
using SESMDiscord.Services;
using System.Data.SQLite;
using System.Text.RegularExpressions;
using SESMDiscord.CustomPreconditionAttributes;
using System.Threading;

namespace SESMDiscord.Modules
{
    [RequireRole("⚒️ Operator")]
    [Group("torch"), Name("Torch")]
    public class Torch : ModuleBase<SocketCommandContext>
    {
        private readonly StartupService _service;
        private readonly IConfigurationRoot _config;
        private readonly LoggingService _logging;
        private readonly string connectionString;
        private readonly SQLiteConnection cnn;
        SQLiteCommand command;
        SQLiteDataReader dataReader;
        String sql;

        public Torch(IConfigurationRoot config, StartupService service, LoggingService logging)
        {
            _config = config;
            _service = service;
            _logging = logging;
            connectionString = "Data Source =" + _config["dbConnection"] + " ;Version=3;";
            cnn = new SQLiteConnection(connectionString);
        }

        [Command]
        [Summary("Arguments that can be used with this command")]
        public async Task Default()
        {
            var embed = new EmbedBuilder
            {
                Title = "Torch",
                Description = "Arguments that can be used with this command"
            };

            embed.AddField("start", _config["prefix"] + "torch start [serverID]")
                .AddField("kill", _config["prefix"] + "torch kill [serverID]")
                .AddField("restart", _config["prefix"] + "torch restart [serverID]")
                .AddField("servers", _config["prefix"] + "torch servers Lists all servers and their [serverID]")
                .AddField("list", _config["prefix"] + "torch list List all running servers managed by this bot")
                .AddField("watchdog", _config["prefix"] + "torch watchdog true / false")
                .AddField("register", _config["prefix"] + "torch register [ServerName] [ID/Alias] [filepath] [args] [monitoring boolean] |")
                .AddField("_", "register is used to register new servers")
                .AddField("setupdb", _config["prefix"] + "torch setupdb Performs the initial database setup")
                .AddField("monitoring", _config["prefix"] + "torch monitoring [serverID] [boolean] Sets the active monitoring flag for a server")
                .WithCurrentTimestamp()
                .WithColor(Color.Blue);

            await ReplyAsync("", false, embed.Build());
        }

        [Command("start", RunMode = RunMode.Async)]
        public async Task StartAsync(string id)
        {
            cnn.Open();

            sql = Regex.IsMatch(id, @"^\d+$") ? "Select FilePath,Name,Args From ServerList Where Id = " + id + ";" : "Select FilePath,Name,Args From ServerList Where Alias = '" + id + "';";

            command = new SQLiteCommand(sql, cnn);

            dataReader = command.ExecuteReader();
            dataReader.Read();

            var embed = new EmbedBuilder
            {
                Title = "Starting Server: " + dataReader.GetValue(1).ToString()

            };

            bool start = true;

            foreach (var p in Process.GetProcessesByName("Torch.Server"))
            {
                if (p.MainModule.FileName == dataReader.GetValue(0).ToString())
                {
                    start = false;
                }
            }
            if (start)
            {
                var p = new Process();
                p.StartInfo.FileName = dataReader.GetValue(0).ToString();
                p.StartInfo.Arguments = dataReader.GetValue(2).ToString();
                p.Start();

                embed.Description = "This can take up to 5 minutes.";
            }
            else
            {
                embed.Description = "This server is already running";
            }

            dataReader.Close();
            command.Dispose();
            cnn.Close();

            embed.WithCurrentTimestamp()
                 .WithColor(Color.Green);

            await ReplyAsync("", false, embed.Build());

        }

        [Command("servers", RunMode = RunMode.Async)]
        public async Task Servers()
        {
            var embed = new EmbedBuilder
            {
                Title = "Servers",
                Description = " ",
            };

            cnn.Open();

            sql = "Select Name,Alias From ServerList;";

            command = new SQLiteCommand(sql, cnn);

            dataReader = command.ExecuteReader();

            while (dataReader.Read())
            {
                embed.AddField("ID: " + dataReader.GetValue(1), "Servername: " + dataReader.GetValue(0), false);
            }
            embed.WithCurrentTimestamp()
                .WithColor(Color.Blue);

            await ReplyAsync("", false, embed.Build());
        }

        [Command("kill", RunMode = RunMode.Async)]
        public async Task StopAsync(string id)
        {
            var processList = Process.GetProcessesByName("Torch.Server");
            var processExists = Process.GetProcesses().Any(p => p.ProcessName.Contains("Torch.Server"));

            cnn.Open();

            sql = Regex.IsMatch(id, @"^\d+$") ? "Select FilePath,Name From ServerList Where Id = " + id + ";" : "Select FilePath,Name,Args From ServerList Where Alias = '" + id + "';";

            command = new SQLiteCommand(sql, cnn);

            dataReader = command.ExecuteReader();
            dataReader.Read();

            var embed = new EmbedBuilder
            {
                Title = "Killing Server: " + dataReader.GetValue(1).ToString()
            };

            foreach (var p in processList)
            {

                if (p.MainModule.FileName == dataReader.GetValue(0).ToString())
                {
                    p.Kill();
                    embed.Description = "Warning: This can lead to file corruption.";
                }

            }

            dataReader.Close();
            command.Dispose();
            cnn.Close();

            embed.WithCurrentTimestamp()
                .WithColor(Color.Red);
            await ReplyAsync("", false, embed.Build());
        }

        [Command("list", RunMode = RunMode.Async)]
        public async Task ListAsync()
        {
            var embed = new EmbedBuilder
            {
                Title = "Currently Running Servers:"
            };

            var processes = Process.GetProcessesByName("Torch.Server");

            foreach (var p in processes)
            {
                try
                {

                    var name = string.Empty;
                    var cpu = new PerformanceCounter("Process", "% Processor Time", p.ProcessName, true);
                    var ram = new PerformanceCounter("Process", "Private Bytes", p.ProcessName, true);

                    // Getting first initial values
                    cpu.NextValue();
                    ram.NextValue();

                    Thread.Sleep(100);

                    double CPU = Math.Round(cpu.NextValue() / Environment.ProcessorCount, 2);
                    // Returns number of MB consumed by application
                    float RAM = ram.NextValue() / 1024 / 1024;
                    cnn.Open();

                    sql = "Select Name From ServerList Where FilePath = @FilePath;";

                    command = new SQLiteCommand(sql, cnn);
                    command.Parameters.AddWithValue("@FilePath", p.MainModule.FileName);

                    dataReader = command.ExecuteReader();
                    dataReader.Read();

                    embed.AddField("PID: " + p.Id.ToString(), "ServerName: " + dataReader.GetValue(0) + "\nCPU: " + CPU.ToString("0.00") + " % " + "RAM: " + RAM.ToString("0.00") + " MB");

                    dataReader.Close();
                    command.Dispose();
                    cnn.Close();

                }
                catch (Exception e)
                {
                    await _logging.ManualOnLogAsync("Error", "torch.list", e.Message);
                }

            }

            embed.WithCurrentTimestamp()
                            .WithColor(Color.Red);
            await ReplyAsync("", false, embed.Build());
        }

        [Command("restart", RunMode = RunMode.Async)]
        public async Task RestartAsync(string id)
        {
            await StopAsync(id);
            await StartAsync(id);
        }

        [Command("watchdog", RunMode = RunMode.Async)]
        public async Task ToggleWatchdog(bool arg)
        {
            var embed = new EmbedBuilder
            {
                Title = "Watchdog",
            };
            switch (arg)
            {
                case true:
                    await _service.StartWatchdog();

                    embed.Description = "Watchdog has been started";
                    embed.WithColor(Color.Green);
                    break;
                case false:
                    await _service.StopWatchdog();

                    embed.Description = "Watchdog has been stoped";
                    embed.WithColor(Color.Red);
                    break;
                default:
                    embed.Description = "Invalid imput";
                    embed.WithColor(Color.Red);
                    break;
            }
            embed.WithCurrentTimestamp();

            await ReplyAsync("", false, embed.Build());
        }

        [Command("register", RunMode = RunMode.Async)]
        public async Task RegisterServer(string name, string alias, string filePath, string args, bool monitoring)
        {
            cnn.Open();

            sql = "Insert Into ServerList (Name,Alias,FilePath,Args,Monitoring)";
            sql += " Values(@Name,@Alias,@FilePath,@Args,@Monitoring)";

            command = new SQLiteCommand(sql, cnn);
            command.Parameters.AddWithValue("@Name", name);
            command.Parameters.AddWithValue("@Alias", alias);
            command.Parameters.AddWithValue("@FilePath", filePath);
            command.Parameters.AddWithValue("@Args", args);
            command.Parameters.AddWithValue("@Monitoring", monitoring);

            try
            {
                command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                await _logging.ManualOnLogAsync("Error", "torch.register", e.Message);
            }

            command.Dispose();
            cnn.Close();

            var embed = new EmbedBuilder
            {
                Title = "Registering Server: " + name
            };

            embed.WithCurrentTimestamp()
                .WithColor(Color.Green);
            await ReplyAsync("", false, embed.Build());
            await Task.CompletedTask;
        }

        [Command("remove", RunMode = RunMode.Async)]
        public async Task RemoveServer(string id)
        {
            cnn.Open();
            sql = "DELETE From ServerList Where Alias = @alias";
            command = new SQLiteCommand(sql, cnn);
            command.Parameters.AddWithValue("@alias", id);

            try
            {
                command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                await _logging.ManualOnLogAsync("Error", "torch.remove", e.Message);
            }

            command.Dispose();
            cnn.Close();

            var embed = new EmbedBuilder
            {
                Title = "Removing Server: " + id.ToUpper()
            };

            embed.WithCurrentTimestamp()
                .WithColor(Color.Red);
            await ReplyAsync("", false, embed.Build());
            await Task.CompletedTask;
        }

        [Command("setupDb", RunMode = RunMode.Async)]
        public async Task SetupDb()
        {
            var embed = new EmbedBuilder
            {
                Title = "Setting up Database"
            };
            SQLiteConnection.CreateFile("database.db");
            SQLiteConnection.ClearAllPools();
            try
            {
                cnn.Open();

                sql = "CREATE TABLE ServerList (Id INTEGER PRIMARY KEY AUTOINCREMENT,Name TEXT NOT NULL,Alias TEXT NOT NULL,FilePath TEXT NOT NULL,Args TEXT NOT NULL DEFAULT '-autostart',Monitoring INTEGER NOT NULL DEFAULT 0);";

                command = new SQLiteCommand(sql, cnn);
                command.ExecuteNonQuery();

                command.Dispose();
                cnn.Close();


                embed.WithCurrentTimestamp()
                    .WithColor(Color.Green);
            }
            catch (Exception e)
            {
                embed = new EmbedBuilder
                {
                    Title = "Setting up Database: FAILED"
                };

                embed.WithCurrentTimestamp()
                    .WithColor(Color.Red);
                await _logging.ManualOnLogAsync("Error", "SetupDB", e.Message);
            }
            finally
            {
                await ReplyAsync("", false, embed.Build());
            }

            await Task.CompletedTask;
        }

        [Command("monitoring", RunMode = RunMode.Async)]
        public async Task ToggleMonitoring(string id, bool state)
        {
            cnn.Open();

            sql = "UPDATE ServerList SET Monitoring = @state WHERE Alias = @id;";

            command = new SQLiteCommand(sql, cnn);
            command.Parameters.AddWithValue("@id", id.ToUpper());
            command.Parameters.AddWithValue("@state", state);

            try
            {
                command.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                await _logging.ManualOnLogAsync("Error", "torch.monitoring", e.Message);
            }

            command.Dispose();
            cnn.Close();

            cnn.Open();

            sql = "Select Name,Monitoring From ServerList Where Alias = @id;";

            command = new SQLiteCommand(sql, cnn);
            command.Parameters.AddWithValue("@id", id.ToUpper());

            dataReader = command.ExecuteReader();
            dataReader.Read();

            var embed = new EmbedBuilder
            {
                Title = "Monitoring Server: " + dataReader.GetValue(0),
                Description = "Status: " + dataReader.GetValue(1)
            };

            dataReader.Close();
            command.Dispose();
            cnn.Close();

            embed.WithCurrentTimestamp()
                .WithColor(Color.Green);
            await ReplyAsync("", false, embed.Build());
        }
    }
}
