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

		void OnSizeChanging(Size newSize);

		void Show();
		void Hide();
	}
}
