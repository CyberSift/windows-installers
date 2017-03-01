﻿using System.IO;
using System.IO.Abstractions;
using System.Linq;
using Elastic.Installer.Domain.Session;

namespace Elastic.Installer.Domain.Kibana.Model.Tasks
{
	public class RemovePluginsTask : KibanaInstallationTask
	{
		public RemovePluginsTask(string[] args, ISession session) 
			: base(args, session) { }

		public RemovePluginsTask(KibanaInstallationModel model, ISession session, IFileSystem fileSystem) 
			: base(model, session, fileSystem) { }

		protected override bool ExecuteTask()
		{
			var installDirectory = this.InstallationModel.KibanaEnvironmentState.HomeDirectory;
			var configDirectory = this.InstallationModel.KibanaEnvironmentState.ConfigDirectory;
			var configFile = Path.Combine(configDirectory, "kibana.yml");
			var provider = this.InstallationModel.PluginsModel.PluginStateProvider;

			var plugins = provider.InstalledPlugins(installDirectory, configDirectory);

			if (plugins.Count == 0)
			{
				this.Session.Log("No existing plugins to remove");
				return true;
			}

			var ticksPerPlugin = new[] { 20, 1930, 50 };
			var totalTicks = plugins.Count * ticksPerPlugin.Sum();
			

			this.Session.SendActionStart(totalTicks, ActionName, "Removing existing Kibana plugins", "Kibana plugin: [1]");
			foreach (var plugin in plugins)
			{
				this.Session.SendProgress(ticksPerPlugin[0], $"removing {plugin}");
				provider.Remove(ticksPerPlugin[1], installDirectory, configDirectory, plugin, "--config", configFile);
				this.Session.SendProgress(ticksPerPlugin[2], $"removed {plugin}");
			}
			return true;
		}
	}
}