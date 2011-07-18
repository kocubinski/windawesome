using System.Drawing;

namespace Windawesome
{
	public interface IBar
	{
		void InitializeBar(Windawesome windawesome, Config config);
		void Dispose();

		int GetBarHeight();
		Point Location { get; set; }
		Size Size { get; set; }

		void Show();
		void Hide();
		bool Visible { get; set; }
	}
}
