using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]

namespace Windawesome
{
	static class Program
	{
		private static Windawesome windawesome;

		[STAThread]
		static void Main()
		{
			bool createdNew;
			using (new Mutex(true, "{BCDA45B7-407E-43F3-82FB-D1F6D6D093FF}", out createdNew))
			{
				if (createdNew) // if the mutex was taken successfully, i.e. this is the first instance of the app running
				{
					Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
					Thread.CurrentThread.Priority = ThreadPriority.Highest;

					Application.EnableVisualStyles();
					Application.SetCompatibleTextRenderingDefault(false);

					Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
					Application.ThreadException += OnApplicationThreadException;
					AppDomain.CurrentDomain.UnhandledException += (_, e) => OnException(e.ExceptionObject as Exception);

					windawesome = new Windawesome();
					Application.Run(new WindawesomeApplicationContext());

					Application.ThreadException -= OnApplicationThreadException;
				}
			}
		}

		private static void OnApplicationThreadException(object sender, ThreadExceptionEventArgs e)
		{
			OnException(e.Exception);
		}

		private static void OnException(Exception e)
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
						"Elevated: " + SystemAndProcessInformation.isRunningElevated,
						e.ToString(),
						"Inner Exception:",
						e.InnerException != null ? e.InnerException.ToString() : "none"
					}));

			if (windawesome != null)
			{
				MessageBox.Show("An exception has occurred. Windawesome will now close. " +
					"Please see the log file in the program directory and post an issue on the website " +
						"if you think this is a bug.",
					"Exception occurred");
				windawesome.Quit();
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
