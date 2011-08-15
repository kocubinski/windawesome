using System;
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
			using (new System.Threading.Mutex(true, "Global\\{BCDA45B7-407E-43F3-82FB-D1F6D6D093FF}", out createdNew))
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
								e.ToString()
							});

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
