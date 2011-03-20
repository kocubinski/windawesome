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
				Application.Run(new WindawesomeApplicationContext(windawesome));
			}
			catch (Exception e)
			{
				System.IO.File.AppendAllLines("log.txt", new string[]
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
			internal WindawesomeApplicationContext(Windawesome windawesome)
			{
				Windawesome.WindawesomeExiting += Windawesome_WindawesomeExiting;
			}

			private void Windawesome_WindawesomeExiting()
			{
				this.ExitThread();
			}
		}
	}
}
