using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Timers;
using System.Data.SQLite;
using System.IO;

namespace SESMDiscord.Services
{
    public class StartupService
    {
        private readonly IServiceProvider _provider;
        private readonly DiscordSocketClient _discord;
        private readonly CommandService _commands;
        private readonly IConfigurationRoot _config;
        private readonly LoggingService _logging;
        public Timer WatchDogTimer;
        private readonly string connectionString;
        private readonly SQLiteConnection cnn;
        SQLiteCommand command;
        SQLiteDataReader dataReader;
        String sql;
        private readonly ulong id;

        // DiscordSocketClient, CommandService, and IConfigurationRoot are injected automatically from the IServiceProvider
        public StartupService(
            IServiceProvider provider,
            DiscordSocketClient discord,
            CommandService commands,
            IConfigurationRoot config,
            LoggingService logging)
        {
            _provider = provider;
            _config = config;
            _discord = discord;
            _commands = commands;
            _logging = logging;
            connectionString = "Data Source =" + _config["dbConnection"] + " ;Version=3;";
            cnn = new SQLiteConnection(connectionString);
            ulong.TryParse(_config["commandChannelId"], out id);
        }
        public async Task StartAsync()
        {
            string discordToken = _config["tokens:discord"];     // Get the discord token from the config file
            if (string.IsNullOrWhiteSpace(discordToken))
                throw new Exception("Please enter your bot's token into the `_config.yml` file found in the applications root directory.");

            await _discord.LoginAsync(TokenType.Bot, discordToken);     // Login to discord
            await _discord.StartAsync();                                // Connect to the websocket
            await InitTimer();
            await _discord.SetGameAsync("Big Brother is Watching You!");
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _provider);     // Load commands and modules into the command service
        }
        public async void StartProcess(string Path)
        {
            var processExists = Process.GetProcesses().Any(p => p.ProcessName.Contains("Torch.Server"));

            if (!processExists)
            {
                Process.Start(Path, "-autostart");
            }
            await Task.CompletedTask;
        }
        public async Task ChangeStatus(UserStatus userStatus)
        {
            if (_discord.Status != userStatus)
            {
                await _discord.SetStatusAsync(userStatus);
            }
        }
        public async Task InitTimer()
        {
            WatchDogTimer = new Timer();
            WatchDogTimer.Elapsed += new ElapsedEventHandler(WatchDogTimer_Elapsed);
            WatchDogTimer.Interval = 2000; // in miliseconds
            if (File.Exists(_config["dbConnection"]))
            {
                if (_config["watchdog"] == "true")
                {
                    WatchDogTimer.Start();
                }

            }
            else
            {
                await _logging.ManualOnLogAsync("Error", "InitWatchdog", "Database file does not exist");
            }
            await Task.CompletedTask;
        }
        private async void WatchDogTimer_Elapsed(object sender, EventArgs e)
        {
            var processExists = Process.GetProcesses().Any(p => p.ProcessName.Contains("Torch.Server"));

            if (processExists)
            {
                await ChangeStatus(UserStatus.Online);
            }
            else
            {
                await ChangeStatus(UserStatus.DoNotDisturb);
            }
            await UpdateProcesses();
        }
        public async Task UpdateProcesses()
        {
            var processList = Process.GetProcessesByName("Torch.Server");
            var chnl = _discord.GetChannel(id) as IMessageChannel;
            try
            {
                cnn.Open();
                sql = "Select Count(*) From ServerList Where Monitoring = '1';";
                command = new SQLiteCommand(sql, cnn);
                dataReader = command.ExecuteReader();
                dataReader.Read();
                int amountMonitoredProcesses = Int32.Parse(dataReader.GetValue(0).ToString());
                dataReader.Close();
                command.Dispose();
                cnn.Close();
                if (processList.Length < amountMonitoredProcesses)
                {
                    await _logging.ManualOnLogAsync("Info", "UpdateProcesses", processList.Length.ToString() + " / " + amountMonitoredProcesses.ToString());
                    cnn.Open();
                    sql = "Select FilePath,Monitoring From ServerList;";
                    command = new SQLiteCommand(sql, cnn);
                    dataReader = command.ExecuteReader();
                    //List<string> servers = new List<string>();
                    Dictionary<string, int> servers = new Dictionary<string, int>();
                    while (dataReader.Read())
                    {
                        servers.Add(dataReader.GetValue(0).ToString(), Int32.Parse(dataReader.GetValue(1).ToString()));
                    }
                    dataReader.Close();
                    command.Dispose();
                    cnn.Close();
                    foreach (var p in Process.GetProcessesByName("Torch.Server"))
                    {
                        await _logging.ManualOnLogAsync("Info", "UpdateProcesses", p.MainModule.FileName + " is running.");
                        if (servers.Keys.Contains(p.MainModule.FileName))
                        {
                            servers.Remove(p.MainModule.FileName);
                        }
                    }

                    await _logging.ManualOnLogAsync("Info", "UpdateProcesses", "Servers offline: " + servers.Count + " / " + amountMonitoredProcesses);
                    if (servers.Count != 0)
                    {
                        foreach (var element in servers)
                        {
                            if (element.Value == 1)
                            {
                                await _logging.ManualOnLogAsync("Warning", "UpdateProcesses", "Server " + element.Key + " appears to be offline, starting server.");
                                cnn.Open();
                                sql = "Select Args,Name From ServerList Where FilePath ='" + element.Key + "';";
                                command = new SQLiteCommand(sql, cnn);
                                dataReader = command.ExecuteReader();
                                dataReader.Read();
                                var p = new Process();
                                p.StartInfo.FileName = element.Key;
                                p.StartInfo.Arguments = dataReader.GetValue(0).ToString();
                                p.Start();
                                var embed = new EmbedBuilder
                                {
                                    Title = "Starting Server: " + dataReader.GetValue(1).ToString(),
                                    Description = "Server appears to be offline.",
                                };
                                embed.AddField("Status", "Starting server.")
                                    .WithCurrentTimestamp()
                                    .WithColor(Color.Red);
                                await chnl.SendMessageAsync("", false, embed.Build());
                                if (_config["pChannelId"].Length > 0)
                                {
                                    ulong pid = ulong.Parse(_config["pChannelId"]);
                                    var pchnl = _discord.GetChannel(pid) as IMessageChannel;
                                    await pchnl.SendMessageAsync("", false, embed.Build());
                                }

                                dataReader.Close();
                                command.Dispose();
                                cnn.Close();
                            }
                        }

                    }
                }
            }
            catch (Exception e)
            {
                //await _logging.ManualOnLogAsync("Warning", "UpdateProcesses", e.Message);
                await Task.CompletedTask;
            }
            /*
            foreach (var s in processList)
            {
                if (!s.Responding)
                {
                    
                    var p = new Process();
                    p.StartInfo.FileName = s.MainModule.FileName;
                    p.StartInfo.Arguments = "-autostart";


                    cnn.Open();
                    sql = "Select Name From ServerList Where FilePath ='" + s.MainModule.FileName + "';";
                    command = new SQLiteCommand(sql, cnn);
                    dataReader = command.ExecuteReader();
                    dataReader.Read();
                    s.Kill();
                    p.Start();
                    var embed = new EmbedBuilder
                    {
                        Title = "Server: " + dataReader.GetValue(0).ToString(),
                        Description = "Server not responding",
                    };
                    embed.AddField("Status", "Forcibly restarting server")
                        .WithCurrentTimestamp()
                        .WithColor(Color.Red);
                    await chnl.SendMessageAsync("", false, embed.Build());

                    dataReader.Close();
                    command.Dispose();
                    cnn.Close();

                    if (_config["pChannelId"].Length > 0)
                    {
                        ulong pid = ulong.Parse(_config["pChannelId"]);
                        var pchnl = _discord.GetChannel(pid) as IMessageChannel;
                        await pchnl.SendMessageAsync("", false, embed.Build());
                    }

                    await _logging.ManualOnLogAsync("Warning", "UpdateProcesses", "Server not responding, forcibly restarting server.");
                }
            }*/
            await Task.CompletedTask;
        }
        public async Task StartWatchdog()
        {
            WatchDogTimer.Start();
            await Task.CompletedTask;
        }
        public async Task StopWatchdog()
        {
            WatchDogTimer.Stop();
            await Task.CompletedTask;
        }
    }
}
