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
				System.IO.StreamWriter writer = new System.IO.StreamWriter("log.txt", true);
				writer.WriteLine("------------------------------------");
				writer.WriteLine(DateTime.Now);
				writer.WriteLine(e);
				writer.Close();

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
				windawesome.FormClosed += new FormClosedEventHandler(windawesome_FormClosed);
			}

			void windawesome_FormClosed(object sender, FormClosedEventArgs e)
			{
				this.ExitThread();
			}
		}
	}
}
