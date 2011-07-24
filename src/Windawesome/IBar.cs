using System;
using System.Drawing;

namespace Windawesome
{
	public interface IBar
	{
		void InitializeBar(Windawesome windawesome, Config config);
		void Dispose();

		IntPtr Handle { get; }

		int GetBarHeight();
		Point Location { get; set; }
		Size Size { get; set; }

		void Show();
		void Hide();
	}
}
