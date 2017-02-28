using Elastic.Installer.Domain.Process;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using Elastic.Installer.Domain.Kibana.Configuration.EnvironmentBased;
using Elastic.Installer.Domain.Kibana.Configuration.FileBased;

namespace Elastic.Installer.Domain.Kibana.Process
{
	public class KibanaProcess : ProcessBase
	{
		private RxFileSystemWatcher _fileSystemWatcher;

		public string JsPath { get; set; }

		public string ConfigFile { get; set; }

		public string Host { get; set; }

		public string LogFile { get; set; }

		public int? Port { get; set; }

		public KibanaProcess() : this(null) { }

		public KibanaProcess(IEnumerable<string> args) : base(args)
		{
			this.HomeDirectory = (this.HomeDirectory
				?? Environment.GetEnvironmentVariable(KibanaEnvironmentVariables.KIBANA_HOME_ENV_VAR, EnvironmentVariableTarget.Machine)
				?? Directory.GetParent(".").FullName).TrimEnd('\\');

			this.ConfigDirectory = (this.ConfigDirectory
				?? Environment.GetEnvironmentVariable(KibanaEnvironmentVariables.KIBANA_CONFIG_ENV_VAR, EnvironmentVariableTarget.Machine)
				?? Path.Combine(this.HomeDirectory, "config")).TrimEnd('\\');

			this.JsPath = Path.Combine(this.HomeDirectory, @"src\cli");
			this.ConfigFile = Path.Combine(this.ConfigDirectory, "kibana.yml");
			this.ProcessExe = Path.Combine(this.HomeDirectory, @"node\node.exe");

			var yamlConfig = KibanaYamlConfiguration.FromFolder(this.ConfigDirectory);
			this.LogFile = yamlConfig.Settings.LoggingDestination;
		}

		protected override List<string> GetArguments()
		{
			var arguments = this.AdditionalArguments.Concat(new[]
			{
				"--no-warnings",
				$"\"{this.JsPath}\"",
				$"--config \"{this.ConfigFile}\""
			})
			.ToList();

			return arguments;
		}

		protected override List<string> ParseArguments(IEnumerable<string> args)
		{
			var newArgs = new List<string>();
			if (args == null)
				return newArgs;
			var nextArgIsConfigPath = false;
			foreach (var arg in args)
			{
				if (arg == "--config" || arg == "-c")
					nextArgIsConfigPath = true;
				if (nextArgIsConfigPath)
				{
					nextArgIsConfigPath = false;
					this.ConfigDirectory = arg;
				}
				else
					newArgs.Add(arg);

			}
			return newArgs;
		}

		public override void Start()
		{
			if (this.LogFile != "stdout")
			{
				var fileInfo = new FileInfo(LogFile);
				var logDirectory = fileInfo.DirectoryName;
				var seekTo = fileInfo.Exists ? fileInfo.Length : 0;

				// When a log file is specified, Kibana does not write to stdout so
				// watch the log file for the started notification
				_fileSystemWatcher = new RxFileSystemWatcher(c =>
				{
					c.Path = logDirectory;
					c.Filter = fileInfo.Name;
					c.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.LastAccess;
				});

				this.Disposables.Add(_fileSystemWatcher);

				_fileSystemWatcher.Changed
					.TakeWhile(c => !this.Started)
					.Subscribe(f =>
					{
						using (var fileStream = new FileStream(LogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
						using (var reader = new StreamReader(fileStream))
						{
							reader.BaseStream.Seek(seekTo, SeekOrigin.Begin);
							string line;

							while ((line = reader.ReadLine()) != null)
								HandleMessage(ConsoleOut.Out(line));

							Interlocked.CompareExchange(ref seekTo, reader.BaseStream.Position, seekTo);
						}
					});

				_fileSystemWatcher.Start();
			}

			base.Start();
		}

		protected override void HandleMessage(ConsoleOut consoleOut)
		{
			var message = new KibanaMessage(consoleOut.Data);
			if (this.Started || string.IsNullOrWhiteSpace(message.Message)) return;
	
			string host; int? port;
		    if (message.TryGetStartedConfirmation(out host, out port))
		    {
				this._fileSystemWatcher?.Stop();
			    this.Host = host;
			    this.Port = port;

				this.BlockingSubject.OnNext(this.StartedHandle);
				this.Started = true;
				this.StartedHandle.Set();
			}
		}
	}
}
