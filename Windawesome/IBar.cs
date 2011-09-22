using System;

namespace Windawesome
{
	public interface IBar
	{
		void InitializeBar(Windawesome windawesome);
		void Dispose();

		IntPtr Handle { get; }

		Monitor Monitor { get; }

		int GetBarHeight();

		void OnClientWidthChanging(int newWidth);

		void Show();
		void Hide();

		void Refresh();
	}
}
