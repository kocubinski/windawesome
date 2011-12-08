using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

namespace Windawesome
{
	public static class UpdateChecker
	{
		public static void CheckForUpdate()
		{
			const string xmlUrl = "http://dl.dropbox.com/u/52551418/app_version.xml";

			Task.Factory.StartNew(() =>
				{
					Version newVersion = null;
					string url = null;

					try
					{
						var xmlDocument = new XmlDocument();
						xmlDocument.Load(xmlUrl);

						var versionNode = xmlDocument.SelectNodes("/Windawesome/version")[0];
						newVersion = new Version(versionNode.InnerText);

						var urlNode = xmlDocument.SelectNodes("/Windawesome/url")[0];
						url = urlNode.InnerText;
					}
					catch
					{
					}

					return Tuple.Create(newVersion, url);
				}).ContinueWith(t =>
					{
						var newVersion = t.Result.Item1;
						var url = t.Result.Item2;

						if (newVersion != null)
						{
							var currentVersion = new Version(Application.ProductVersion);

							if (currentVersion.CompareTo(newVersion) < 0)
							{
								var result = MessageBox.Show(
									"There is a new version of Windawesome. Would you like to open the website to download it?",
									"New version available",
									MessageBoxButtons.YesNo,
									MessageBoxIcon.Question);
								if (result == DialogResult.Yes)
								{
									Utilities.RunApplication(url);
								}
							}
						}
					}, TaskScheduler.FromCurrentSynchronizationContext());
		}
	}
}
