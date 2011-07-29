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

		private class WindawesomeApplicationContext : ApplicationContext
		{
			public WindawesomeApplicationContext()
			{
				Windawesome.WindawesomeExiting += this.ExitThread;
			}
		}
	}
}
