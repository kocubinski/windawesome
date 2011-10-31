using System;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main()
		{
			bool createdNew;
			using (new System.Threading.Mutex(true, "{BCDA45B7-407E-43F3-82FB-D1F6D6D093FF}", out createdNew))
			{
				if (createdNew) // if the mutex was taken successfully, i.e. this is the first instance of the app running
				{
					Application.EnableVisualStyles();
					Application.SetCompatibleTextRenderingDefault(false);

					Windawesome windawesome = null;
					try
					{
						windawesome = new Windawesome();
						Application.Run(new WindawesomeApplicationContext());
					}
					catch (Exception e)
					{
						System.IO.File.AppendAllLines("log.txt", new[]
							{
								"------------------------------------",
								DateTime.Now.ToString(),
								"Assemblies:",
								System.Reflection.Assembly.GetExecutingAssembly().FullName
							}.
							Concat(System.Reflection.Assembly.GetExecutingAssembly().GetReferencedAssemblies().Select(a => a.FullName)).
							Concat(new[]
								{
									"",
									"OS: " + Environment.OSVersion.VersionString,
									"CLR: " + Environment.Version.ToString(3),
									"64-bit OS: " + Environment.Is64BitOperatingSystem,
									"64-bit process: " + Environment.Is64BitProcess,
									"Elevated: " + Windawesome.isRunningElevated,
									e.ToString(),
									"Inner Exception:",
									e.InnerException != null ? e.InnerException.ToString() : "none"
								}));

						if (windawesome != null)
						{
							windawesome.Quit();
						}
					}
				}
			}
		}

		private class WindawesomeApplicationContext : ApplicationContext
		{
			public WindawesomeApplicationContext()
			{
				Windawesome.WindawesomeExiting += this.ExitThread;
			}
		}
	}
}
