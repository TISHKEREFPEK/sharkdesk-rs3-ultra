using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace SharkDesk;

public class App : Application
{
	protected override async void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);
		if (e.Args.Contains("--monitor-probe"))
		{
			List<object> samples = new List<object>();
			for (int i = 0; i < 6; i++)
			{
				try
				{
					using MouseDevice device = await MouseDevice.Connect();
					List<object> list = samples;
					list.Add(await device.ReadBattery());
				}
				catch (Exception ex)
				{
					samples.Add(new
					{
						error = ex.Message,
						at = DateTimeOffset.UtcNow
					});
				}
				if (i < 5)
				{
					await Task.Delay(3000);
				}
			}
			File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "monitor-probe.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Shutdown(0);
			return;
		}
		if (e.Args.Contains("--probe"))
		{
			try
			{
				using (MouseDevice device = await MouseDevice.Connect())
				{
					MouseSettings value = await device.ReadSettings();
					File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe.json"), JsonSerializer.Serialize(value, new JsonSerializerOptions
					{
						WriteIndented = true
					}));
					Shutdown(0);
				}
				return;
			}
			catch (Exception ex2)
			{
				File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe.json"), JsonSerializer.Serialize(new
				{
					error = ex2.Message
				}));
				Shutdown(1);
				return;
			}
		}
		new MainWindow().Show();
	}

	[STAThread]
	[DebuggerNonUserCode]
	[GeneratedCode("PresentationBuildTasks", "10.0.14.0")]
	public static void Main()
	{
		new App().Run();
	}
}
